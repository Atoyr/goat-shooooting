using System.Numerics;
using System.Text.Json;
using GoatShooooting.Core;
using GoatShooooting.Definitions;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class ProjectileProgramTests
{
    [Fact]
    public void ProgramSleepsUntilWakeThenAimsSplitsAndDespawns()
    {
        var compiled = Compile("wait-aim-split", new[]
        {
            Node("stop", "set-speed", ("value", Json("0.0"))),
            Node("wait", "wait-frames", ("value", Json("2"))),
            Node("aim", "aim-at", ("target", Json("\"player-snapshot\""))),
            Node("split", "split", ("count", Json("{\"parameter\":\"count\"}")),
                ("projectileId", Json("\"child\"")), ("speed", Json("120.0")))
        }, Parameter("count", 3, 3));
        var diagnostic = compiled.Diagnostics.Get("program", "wait-aim-split");
        Assert.Equal("projectile", diagnostic.Domain);
        Assert.Equal("3", diagnostic.ResolvedParameters["count"]);
        Assert.Equal(3, diagnostic.EstimatedSpawnBudget);
        Assert.Contains("projectile:parent/programSlot", diagnostic.ReferenceChain);
        var store = new ProjectileStore();
        store.ConfigurePrograms(compiled, null);
        var parent = compiled.Get(compiled.ResolveProjectile("parent"));
        new BulletFactory().Create(store, parent, Vector2.Zero, Vector2.UnitY, CollisionLayer.Enemy, 1);
        store.CommitSpawns();
        var world = new World();
        world.CreateEntity()
            .Add(new PlayerComponent("player", 1))
            .Add(new TransformComponent(new Vector2(100, 0)))
            .Add(new ColliderComponent(5, CollisionLayer.Player));
        var system = new ProjectileProgramSystem();

        system.Update(store, world, 0);
        Assert.Equal(2, store.GetSnapshot(0).WakeFrame);
        Assert.Equal(Vector2.Zero, store.GetSnapshot(0).Velocity);
        system.Update(store, world, 1);
        Assert.Equal(0, store.PendingSpawnCount);
        system.Update(store, world, 2);
        Assert.Equal(3, store.PendingSpawnCount);
        Assert.True(store.GetSnapshot(0).PendingRemoval);
        var events = new GameEventBuffer();
        events.BeginTick(2);
        store.CommitSpawns(events);
        store.CommitRemovals();

        Assert.Equal(3, store.ActiveCount);
        Assert.All(Enumerable.Range(0, 3), index =>
        {
            var child = store.GetSnapshot(index);
            Assert.Equal("child", child.DefinitionId);
            Assert.NotEqual(0, child.SpawnLineageId);
            Assert.Equal("split", child.SourceNodeId);
        });
        Assert.All(events.Events.OfType<ProjectileSpawnedEvent>(), spawned =>
        {
            Assert.Equal("wait-aim-split", spawned.ProgramId);
            Assert.Equal("split", spawned.SourceNodeId);
        });
    }

    [Fact]
    public void UnknownProjectileOpcodeFailsDuringDefinitionCompilation()
    {
        var definitions = CreateDefinitions("invalid", new[] { Node("bad", "execute-csharp") });

        var exception = Assert.Throws<DefinitionValidationException>(() =>
            new DefinitionCompiler().Compile(definitions, RuntimeCapabilityRegistry.CreateBuiltIn()));

        Assert.Contains("unknown projectile opcode", exception.Message);
        Assert.Contains("bad", exception.Message);
    }

    [Fact]
    public void ProgramTurnsAndTransformsWithoutScanningAgain()
    {
        var compiled = Compile("turn-transform", new[]
        {
            Node("turn", "add-angle", ("value", Json("90.0"))),
            Node("transform", "transform", ("projectileId", Json("\"child\"")))
        });
        var store = new ProjectileStore();
        store.ConfigurePrograms(compiled, null, runSeed: 42, stageInstance: 1);
        new BulletFactory().Create(
            store,
            compiled.Get(compiled.ResolveProjectile("parent")),
            Vector2.Zero,
            Vector2.UnitY,
            CollisionLayer.Enemy,
            1);
        store.CommitSpawns();

        new ProjectileProgramSystem().Update(store, new World(), 0);

        var snapshot = store.GetSnapshot(0);
        Assert.Equal("child", snapshot.DefinitionId);
        Assert.Null(snapshot.ProgramHandle);
        Assert.Equal(-80, snapshot.Velocity.X, precision: 3);
        Assert.Equal(0, snapshot.Velocity.Y, precision: 3);
    }

    [Fact]
    public void StraightLegacyProjectileKeepsProgramFreeFastPath()
    {
        var compiled = new DefinitionCompiler().Compile(TestDefinitions.Create(), RuntimeCapabilityRegistry.CreateBuiltIn());
        var store = new ProjectileStore();
        store.ConfigurePrograms(compiled, null);
        new BulletFactory().Create(
            store, compiled.Get(compiled.ResolveProjectile("bullet")), Vector2.Zero, Vector2.UnitY,
            CollisionLayer.Enemy, 1);
        store.CommitSpawns();

        var snapshot = store.GetSnapshot(0);
        Assert.Null(snapshot.ProgramHandle);
        Assert.Equal(ProjectileMotionKernel.Linear, snapshot.MotionKernel);
    }

    [Fact]
    public void RecursiveProjectileSpawnGraphFailsStaticBudgetValidation()
    {
        var definitions = CreateDefinitions("recursive", new[]
        {
            Node("again", "emit-ring", ("count", Json("1")),
                ("projectileId", Json("\"parent\"")))
        });

        var exception = Assert.Throws<DefinitionValidationException>(() =>
            new DefinitionCompiler().Compile(definitions, RuntimeCapabilityRegistry.CreateBuiltIn()));

        Assert.Contains("cycle", exception.Message);
        Assert.Contains("recursive", exception.Message);
    }

    private static CompiledCatalog Compile(
        string id,
        IReadOnlyList<ProgramNodeDefinition> nodes,
        params KeyValuePair<string, ProgramParameterDefinition>[] parameters) =>
        new DefinitionCompiler().Compile(
            CreateDefinitions(id, nodes, parameters), RuntimeCapabilityRegistry.CreateBuiltIn());

    private static DefinitionCatalog CreateDefinitions(
        string id,
        IReadOnlyList<ProgramNodeDefinition> nodes,
        params KeyValuePair<string, ProgramParameterDefinition>[] parameters)
    {
        var baseline = TestDefinitions.Create();
        var program = new ProgramDefinition
        {
            Id = id,
            Domain = "projectile",
            Parameters = parameters.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal),
            EntryPoints = new Dictionary<string, IReadOnlyList<ProgramNodeDefinition>> { ["onSpawn"] = nodes }
        };
        var parent = new ProjectileDefinition
        {
            Id = "parent",
            Speed = 80,
            Damage = 1,
            HitRadius = 2,
            Lifetime = 5,
            VisualId = "parent",
            ProgramSlot = new SemanticProgramSlotDefinition { SlotId = "test.parent", DefaultProgramId = id }
        };
        var child = new ProjectileDefinition
        {
            Id = "child",
            Speed = 120,
            Damage = 1,
            HitRadius = 2,
            Lifetime = 5,
            VisualId = "child"
        };
        return new DefinitionCatalog(
            baseline.Game,
            baseline.Players.Values,
            baseline.Enemies.Values,
            baseline.Bullets.Values,
            baseline.Weapons.Values,
            baseline.Stages.Values,
            projectiles: new[] { parent, child },
            visuals: new[]
            {
                new VisualDefinition { Id = "parent", AssetId = "parent" },
                new VisualDefinition { Id = "child", AssetId = "child" }
            },
            programs: new[] { program });
    }

    private static KeyValuePair<string, ProgramParameterDefinition> Parameter(string id, int value, int maximum) =>
        KeyValuePair.Create(id, new ProgramParameterDefinition
        {
            Type = "integer",
            Default = JsonSerializer.SerializeToElement(value),
            Minimum = 1,
            Maximum = maximum
        });

    private static ProgramNodeDefinition Node(string id, string op, params (string Name, JsonElement Value)[] values) => new()
    {
        NodeId = id,
        Op = op,
        Arguments = values.ToDictionary(static item => item.Name, static item => item.Value, StringComparer.Ordinal)
    };

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
}
