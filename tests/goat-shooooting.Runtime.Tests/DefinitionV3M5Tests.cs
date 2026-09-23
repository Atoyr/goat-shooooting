using System.Numerics;
using System.Text.Json;
using GoatShooooting.Core;
using GoatShooooting.Definitions;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class DefinitionV3M5Tests
{
    [Fact]
    public void CompilerAdaptsLegacyEnemyAndOrdersExplicitPartsParentFirst()
    {
        var compiled = Compile(
        [
            Actor(
                new ActorPartDefinition { Id = "wing", ParentPartId = "core" },
                new ActorPartDefinition { Id = "core" })
        ]);

        var legacy = compiled.Get(compiled.ResolveActor("enemy"));
        var explicitActor = compiled.Get(compiled.ResolveActor("carrier"));

        Assert.Empty(legacy.Parts);
        Assert.Equal("enemy", legacy.Definition.EnemyId);
        Assert.Equal(["core", "wing"], explicitActor.Parts.Select(static part => part.Definition.Id));
        Assert.Equal(-1, explicitActor.Parts[0].ParentHandle);
        Assert.Equal(0, explicitActor.Parts[1].ParentHandle);
    }

    [Fact]
    public void FactoryCreatesPartEntitiesAndMountedWeaponAtDeterministicTransforms()
    {
        var compiled = Compile(
        [
            Actor(new ActorPartDefinition
            {
                Id = "turret",
                OffsetX = 10,
                OffsetY = 5,
                Hurtboxes = [Circle("body", 4)],
                Hardpoints = [new HardpointDefinition { Id = "muzzle", OffsetY = 3, WeaponId = "weapon" }]
            })
        ]);
        var world = new World();
        var root = new EnemyFactory().CreateActor(
            world, compiled, compiled.ResolveActor("carrier"), new Vector2(100, 50));

        var part = Assert.Single(world.Query<ActorPartComponent>());
        var mount = Assert.Single(world.Query<ParentTransformComponent>());
        Assert.True(root.Id < part.Id && part.Id < mount.Id);
        Assert.Equal(new Vector2(110, 55), part.Get<TransformComponent>().Position);
        Assert.Equal(new Vector2(110, 58), mount.Get<TransformComponent>().Position);
        Assert.Equal(compiled.ResolveWeapon("weapon").Value, mount.Get<WeaponHolderComponent>().WeaponHandle);

        root.Get<TransformComponent>().Position = new Vector2(120, 70);
        new ActorTransformSystem().Update(world);

        Assert.Equal(new Vector2(130, 75), part.Get<TransformComponent>().Position);
        Assert.Equal(new Vector2(130, 78), mount.Get<TransformComponent>().Position);
    }

    [Fact]
    public void DamageRoutesSharedIndependentForwardedAndIndestructibleParts()
    {
        var compiled = Compile(
        [
            Actor(
                new ActorPartDefinition { Id = "shared", Hurtboxes = [Circle("shared", 3)] },
                new ActorPartDefinition
                {
                    Id = "armor",
                    OffsetX = 10,
                    HealthPolicy = "independent",
                    MaximumHealth = 10,
                    DamageForwardingRatio = 0.5f,
                    Hurtboxes = [Circle("armor", 3)]
                },
                new ActorPartDefinition
                {
                    Id = "ghost",
                    OffsetX = -10,
                    HealthPolicy = "indestructible",
                    Hurtboxes = [Circle("ghost", 3)]
                })
        ]);
        var world = new World();
        var root = new EnemyFactory().CreateActor(world, compiled, compiled.ResolveActor("carrier"), Vector2.Zero);
        var parts = world.Query<ActorPartComponent>().ToDictionary(entity => entity.Get<ActorPartComponent>().PartId);
        var damage = new DamageSystem();
        var telemetry = new SimulationTelemetry();
        var events = new GameEventBuffer();
        events.BeginTick(1);

        damage.Update([new DamageEvent(parts["armor"], 4)], telemetry, events, world);
        damage.Update([new DamageEvent(parts["shared"], 3)], telemetry, events, world);
        damage.Update([new DamageEvent(parts["ghost"], 99)], telemetry, events, world);

        Assert.Equal(6, parts["armor"].Get<HealthComponent>().Current);
        Assert.Equal(5, root.Get<HealthComponent>().Current);
        Assert.False(parts["ghost"].Has<PendingDestroyComponent>());
    }

    [Fact]
    public void CollisionUsesMultipleHurtboxShapesAndDisabledPartsAreExcluded()
    {
        var world = new World();
        var partState = new ActorPartComponent(
            1, 1, 0, "part", Vector2.Zero, 0, ActorPartHealthPolicy.Independent,
            0, true, 1, true, 0, "enemy", null, null);
        var target = world.CreateEntity()
            .Add(new TransformComponent(new Vector2(50, 50)))
            .Add(new ColliderComponent(25, CollisionLayer.Enemy))
            .Add(new HurtboxSetComponent(
            [
                new HurtboxShapeData("circle", HurtboxShape.Circle, new Vector2(-15, 0), 3, 0, 0, 0, 0, 3),
                new HurtboxShapeData("box", HurtboxShape.Obb, new Vector2(15, 0), 0, 8, 20, 0, 30, 11)
            ]))
            .Add(partState)
            .Add(new HealthComponent(10));
        var projectiles = new ProjectileStore();
        projectiles.QueueSpawn(new ProjectileSpawnCommand(
            1, ProjectileTeam.Player, "shot", new Vector2(65, 20), new Vector2(0, 60), 1, 2, 5, "shot"));
        projectiles.CommitSpawns();
        new ProjectileMovementSystem().Update(projectiles, world, 1, 100, 100, new SimulationTelemetry());
        var grid = new ActorSpatialGrid();
        grid.Rebuild(world);
        var events = new GameEventBuffer();
        events.BeginTick(1);

        Assert.Same(target, Assert.Single(new ProjectileCollisionSystem().Detect(
            projectiles, grid, new SimulationTelemetry(), events)).Target);

        partState.Enabled = false;
        var other = new ProjectileStore();
        other.QueueSpawn(new ProjectileSpawnCommand(
            1, ProjectileTeam.Player, "shot", new Vector2(65, 50), Vector2.Zero, 1, 2, 5, "shot"));
        other.CommitSpawns();
        grid.Rebuild(world);
        Assert.Empty(new ProjectileCollisionSystem().Detect(other, grid, new SimulationTelemetry(), events));
    }

    [Fact]
    public void LockOnConsumesTargetCapacityAsStableSlotsAndPhaseSignalsToggleParts()
    {
        var lockWeapon = new WeaponDefinition
        {
            SchemaVersion = 2,
            Id = "lock",
            ActionType = "lock-on",
            Pattern = new CapabilityDefinition
            {
                Type = "spread",
                Parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["projectileCount"] = JsonSerializer.SerializeToElement(1),
                    ["spreadDegrees"] = JsonSerializer.SerializeToElement(0)
                }
            },
            LockOn = new LockOnWeaponDefinition { MaximumTargets = 3, Range = 500 },
            Emitters = [new EmitterDefinition { Id = "shot", ProjectileId = "bullet", FireInterval = 1 }]
        };
        var actor = Actor(new ActorPartDefinition
        {
            Id = "lock-node",
            OffsetY = 50,
            LockCapacity = 3,
            Hurtboxes = [Circle("lock", 4)]
        });
        var compiled = Compile([actor], [lockWeapon]);
        var world = new World();
        var owner = world.CreateEntity()
            .Add(new TransformComponent(new Vector2(0, 100)))
            .Add(new PlayerComponent("player", 1))
            .Add(new ShipComponent(
                "player", 1, 1, 1, 1, 0, [], [],
                specialWeaponId: "lock",
                specialWeaponHandle: compiled.ResolveWeapon("lock").Value))
            .Add(new WeaponRuntimeComponent());
        _ = owner;
        var root = new EnemyFactory().CreateActor(
            world, compiled, compiled.ResolveActor("carrier"), new Vector2(0, 0));
        var part = Assert.Single(world.Query<ActorPartComponent>());
        var weaponSystem = new AdvancedWeaponSystem(
            new BulletFactory(), RuntimeCapabilityRegistry.CreateBuiltIn());

        weaponSystem.Update(
            world, compiled, new MutableInputState { Special = true }, 1,
            new SimulationTelemetry(), new ProjectileStore());

        Assert.Equal([part.Id, part.Id, part.Id],
            owner.Get<WeaponRuntimeComponent>().States["lock"].LockedTargetEntityIds);

        var phase = new CompiledBossPhaseDefinition(
            new BossPhaseDefinition { Id = "phase", Hp = 1, TimeLimit = 1 },
            null, [], [], [new CompiledPartSignal("disable", [0])]);
        ActorPartSignalSystem.Apply(world, root.Id, phase);
        Assert.False(part.Get<ActorPartComponent>().Enabled);
    }

    private static ActorDefinition Actor(params ActorPartDefinition[] parts) => new()
    {
        Id = "carrier",
        EnemyId = "enemy",
        Tags = ["carrier"],
        Parts = parts
    };

    private static HurtboxDefinition Circle(string id, float radius) => new()
    {
        Id = id,
        Shape = "circle",
        Radius = radius
    };

    private static CompiledCatalog Compile(
        IReadOnlyList<ActorDefinition> actors,
        IReadOnlyList<WeaponDefinition>? additionalWeapons = null)
    {
        var source = TestDefinitions.Create(enemyHp: 10);
        var definitions = new DefinitionCatalog(
            source.Game,
            source.Players.Values,
            source.Enemies.Values,
            source.Bullets.Values,
            source.Weapons.Values.Concat(additionalWeapons ?? Array.Empty<WeaponDefinition>()),
            source.Stages.Values,
            actors: actors);
        return new DefinitionCompiler().Compile(definitions, RuntimeCapabilityRegistry.CreateBuiltIn());
    }
}
