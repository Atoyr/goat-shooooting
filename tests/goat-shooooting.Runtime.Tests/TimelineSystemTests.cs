using System.Numerics;
using System.Text.Json;
using GoatShooooting.Core;
using GoatShooooting.Definitions;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class TimelineSystemTests
{
    [Fact]
    public void MotionTimelineProducesGoldenPositionsAndDiscardsCompletedRunner()
    {
        var pattern = Motion("motion",
            Command("enter", P(("duration", 1), ("x", 60), ("y", 0))),
            Command("wait", P(("duration", 0.5f))),
            Command("move-to", P(("duration", 0.5f), ("x", 80), ("y", 20), ("easing", "ease-in-out"))),
            Command("move-by", P(("duration", 0.5f), ("x", 20), ("y", -10), ("space", "world"))),
            Command("leave", P(("duration", 0.5f), ("x", 100), ("y", -50))));
        var definitions = WithPatterns(TestDefinitions.Create(), new[] { pattern });
        var world = new World();
        var enemy = world.CreateEntity()
            .Add(new EnemyComponent("enemy"))
            .Add(new TransformComponent(Vector2.Zero))
            .Add(new VelocityComponent(Vector2.Zero))
            .Add(new MotionTimelineComponent("motion"));
        var system = new MotionTimelineSystem();

        system.Update(world, definitions, null, 0.5f);
        Assert.Equal(new Vector2(30, 0), enemy.Get<TransformComponent>().Position);
        system.Update(world, definitions, null, 0.5f);
        Assert.Equal(new Vector2(60, 0), enemy.Get<TransformComponent>().Position);
        system.Update(world, definitions, null, 0.5f);
        Assert.Equal(new Vector2(60, 0), enemy.Get<TransformComponent>().Position);
        system.Update(world, definitions, null, 0.5f);
        Assert.Equal(new Vector2(80, 20), enemy.Get<TransformComponent>().Position);
        system.Update(world, definitions, null, 0.5f);
        Assert.Equal(new Vector2(100, 10), enemy.Get<TransformComponent>().Position);
        system.Update(world, definitions, null, 0.5f);

        Assert.Equal(new Vector2(100, -50), enemy.Get<TransformComponent>().Position);
        Assert.True(enemy.Has<PendingDestroyComponent>());
    }

    [Fact]
    public void FollowPathOrbitAndPlayerSnapshotUseExplicitCoordinateContracts()
    {
        var points = new[] { new { x = 0, y = 0 }, new { x = 20, y = 0 } };
        var pattern = Motion("motion",
            Command("move-to", P(("duration", 1), ("x", 10), ("y", 20), ("space", "local"))),
            Command("follow-path", P(("duration", 1), ("points", points), ("reference", "player-snapshot"))),
            Command("orbit", P(("duration", 1), ("x", 0), ("y", 0), ("radius", 20),
                ("startAngleDegrees", 0), ("revolutions", 0.25f), ("reference", "player-snapshot"))));
        var definitions = WithPatterns(TestDefinitions.Create(), new[] { pattern });
        var world = new World();
        var player = world.CreateEntity()
            .Add(new PlayerComponent("player", 1))
            .Add(new TransformComponent(new Vector2(100, 100)));
        var enemy = world.CreateEntity()
            .Add(new EnemyComponent("enemy"))
            .Add(new TransformComponent(new Vector2(10, 10)))
            .Add(new VelocityComponent(Vector2.UnitY))
            .Add(new MotionTimelineComponent("motion"));
        var system = new MotionTimelineSystem();

        system.Update(world, definitions, null, 1);
        Assert.Equal(new Vector2(20, 30), enemy.Get<TransformComponent>().Position);
        system.Update(world, definitions, null, 0.5f);
        Assert.Equal(new Vector2(100, 100), enemy.Get<TransformComponent>().Position);
        player.Get<TransformComponent>().Position = new Vector2(400, 400);
        system.Update(world, definitions, null, 0.5f);
        Assert.Equal(new Vector2(120, 100), enemy.Get<TransformComponent>().Position);
        system.Update(world, definitions, null, 1);
        Assert.True(Vector2.Distance(new Vector2(400, 420), enemy.Get<TransformComponent>().Position) < 0.001f);
        system.Update(world, definitions, null, 0);
        Assert.False(enemy.Has<MotionTimelineComponent>());
    }

    [Fact]
    public void AttackTimelineRunsFireParallelRepeatAndStopAsFiniteTracks()
    {
        var child = Attack("child",
            Fire("weapon"),
            Command("wait", P(("duration", 0.1f))),
            Fire("weapon"));
        var cancelled = Attack("cancelled",
            Command("wait", P(("duration", 0.2f))),
            Fire("weapon"));
        var root = Attack("root",
            Reference("start-pattern", "cancelled"),
            Reference("parallel", "child"),
            Fire("weapon"),
            Command("wait", P(("duration", 0.1f))),
            Reference("stop-pattern", "cancelled"),
            Reference("repeat", "child", 2));
        var definitions = WithPatterns(TestDefinitions.Create(), new[] { child, cancelled, root });
        var world = new World();
        var enemy = world.CreateEntity()
            .Add(new EnemyComponent("enemy"))
            .Add(new TransformComponent(new Vector2(50, 50)))
            .Add(new VelocityComponent(Vector2.UnitY))
            .Add(new WeaponHolderComponent("weapon"))
            .Add(new AttackTimelineComponent(new[] { "root" }));
        var projectiles = new ProjectileStore();
        var weapons = new AdvancedWeaponSystem(new BulletFactory(), RuntimeCapabilityRegistry.CreateBuiltIn());
        var system = new AttackTimelineSystem(weapons);
        var telemetry = new SimulationTelemetry();

        for (var tick = 0; tick < 12 && enemy.Has<AttackTimelineComponent>(); tick++)
        {
            system.Update(world, definitions, null, 0.1f, telemetry, projectiles);
            projectiles.CommitSpawns();
        }

        Assert.False(enemy.Has<AttackTimelineComponent>());
        Assert.Equal(7, projectiles.ActiveCount);
        Assert.Equal(7, telemetry.EnemyBulletsSpawned);
    }

    [Fact]
    public void RandomArcIsSeededAndDifficultyTagsFilterEmitters()
    {
        var weapon = ProjectileWeapon("random", new EmitterDefinition
        {
            Id = "random",
            ProjectileId = "bullet",
            FireInterval = 0.1f,
            AngleSource = "fixed",
            FixedAngleDegrees = 30,
            Distribution = "random-arc",
            ProjectileCount = 5,
            SpreadDegrees = 60,
            DifficultyTags = new[] { "expert" }
        });
        var baseline = TestDefinitions.Create();
        var definitions = WithWeapons(
            baseline,
            new[] { weapon },
            new[] { new DifficultyDefinition { Id = "expert" } });

        var first = FireVelocities(definitions, weapon.Id, 42, "expert");
        var second = FireVelocities(definitions, weapon.Id, 42, "expert");
        var different = FireVelocities(definitions, weapon.Id, 43, "expert");
        var filtered = FireVelocities(definitions, weapon.Id, 42, null);

        Assert.Equal(first, second);
        Assert.NotEqual(first, different);
        Assert.Equal(5, first.Length);
        Assert.Empty(filtered);
    }

    [Theory]
    [InlineData("single", 1)]
    [InlineData("fan", 3)]
    [InlineData("ring", 3)]
    [InlineData("arc", 3)]
    [InlineData("layers", 3)]
    public void StandardDistributionsProduceDeclaredProjectileCount(string distribution, int expected)
    {
        var weapon = ProjectileWeapon("distribution", new EmitterDefinition
        {
            Id = "emitter",
            ProjectileId = "bullet",
            FireInterval = 0.1f,
            AngleSource = "current-heading",
            Distribution = distribution,
            ProjectileCount = expected,
            SpreadDegrees = 90
        });
        var definitions = WithWeapons(TestDefinitions.Create(), new[] { weapon });

        var velocities = FireVelocities(definitions, weapon.Id, 0, null, Vector2.UnitX);

        Assert.Equal(expected, velocities.Length);
        if (distribution == "single") Assert.True(velocities[0].X > 0);
    }

    [Fact]
    public void RangeLayersAndAccelerationConnectEmitterToProjectileMotion()
    {
        var weapon = ProjectileWeapon("speed", new EmitterDefinition
        {
            Id = "speed",
            ProjectileId = "bullet",
            FireInterval = 0.1f,
            SpeedMode = "range",
            MinimumSpeedMultiplier = 0.5f,
            MaximumSpeedMultiplier = 1.5f,
            SpeedLayerCount = 3
        });
        var accelerating = ProjectileWeapon("accelerating", new EmitterDefinition
        {
            Id = "speed",
            ProjectileId = "bullet",
            FireInterval = 0.1f,
            SpeedMode = "accelerating",
            AccelerationPerSecond = 20
        });
        var decelerating = ProjectileWeapon("decelerating", new EmitterDefinition
        {
            Id = "speed",
            ProjectileId = "bullet",
            FireInterval = 0.1f,
            SpeedMode = "decelerating",
            AccelerationPerSecond = -300
        });
        var definitions = WithWeapons(TestDefinitions.Create(), new[] { weapon, accelerating, decelerating });
        var speeds = FireVelocities(definitions, weapon.Id, 0, null).Select(static velocity => velocity.Length()).ToArray();
        Assert.Equal(new[] { 50f, 100f, 150f }, speeds);

        var world = new World();
        var owner = world.CreateEntity()
            .Add(new EnemyComponent("enemy"))
            .Add(new TransformComponent(new Vector2(100, 100)))
            .Add(new VelocityComponent(Vector2.UnitY));
        var store = new ProjectileStore();
        var system = new AdvancedWeaponSystem(new BulletFactory(), RuntimeCapabilityRegistry.CreateBuiltIn());
        system.FireOnce(world, definitions, owner, accelerating.Id, new SimulationTelemetry(), store);
        store.CommitSpawns();
        new ProjectileMovementSystem().Update(store, world, 0.5f, 800, 600, new SimulationTelemetry());

        Assert.InRange(store.GetSnapshot(0).Velocity.Length(), 109.999f, 110.001f);

        var stopped = new ProjectileStore();
        system.FireOnce(world, definitions, owner, decelerating.Id, new SimulationTelemetry(), stopped);
        stopped.CommitSpawns();
        new ProjectileMovementSystem().Update(stopped, world, 0.5f, 800, 600, new SimulationTelemetry());
        Assert.Equal(Vector2.Zero, stopped.GetSnapshot(0).Velocity);
    }

    [Fact]
    public void AimAtPlayerAndRotatingAngleSourcesUseRuntimeState()
    {
        var aimed = ProjectileWeapon("aimed", new EmitterDefinition
        {
            Id = "aim",
            ProjectileId = "bullet",
            FireInterval = 0.1f,
            AngleSource = "aim-at-player"
        });
        var rotating = ProjectileWeapon("rotating", new EmitterDefinition
        {
            Id = "rotate",
            ProjectileId = "bullet",
            FireInterval = 0.1f,
            AngleSource = "rotating",
            RotationDegreesPerShot = 90
        });
        var definitions = WithWeapons(TestDefinitions.Create(), new[] { aimed, rotating });
        var aimedVelocity = FireVelocities(definitions, aimed.Id, 0, null).Single();
        Assert.True(aimedVelocity.Y > 0);

        var world = new World();
        var owner = world.CreateEntity()
            .Add(new EnemyComponent("enemy"))
            .Add(new TransformComponent(new Vector2(100, 100)))
            .Add(new VelocityComponent(Vector2.UnitY));
        var store = new ProjectileStore();
        var system = new AdvancedWeaponSystem(new BulletFactory(), RuntimeCapabilityRegistry.CreateBuiltIn());
        system.FireOnce(world, definitions, owner, rotating.Id, new SimulationTelemetry(), store);
        system.FireOnce(world, definitions, owner, rotating.Id, new SimulationTelemetry(), store);
        store.CommitSpawns();

        Assert.True(store.GetSnapshot(0).Velocity.Y > 0);
        Assert.True(store.GetSnapshot(1).Velocity.X < 0);
    }

    [Fact]
    public void DefinitionValidationRejectsUnknownCommandShortIntervalAndSpawnUnderstatement()
    {
        var baseline = TestDefinitions.Create();
        var unknown = Attack("bad", Command("script", P()));
        var shortWait = Attack("bad", Command("wait", P(("duration", 0.001f))));
        var weapon = ProjectileWeapon("wide", new EmitterDefinition
        {
            Id = "wide",
            ProjectileId = "bullet",
            FireInterval = 0.1f,
            Distribution = "ring",
            ProjectileCount = 3
        });
        var weaponCatalog = WithWeapons(baseline, new[] { weapon });
        var understated = Attack("bad", new TimelineCommandDefinition
        {
            Type = "fire",
            Parameters = P(("weaponId", "wide")),
            MaximumSpawnCount = 2
        });
        var sameTick = Attack("bad", new TimelineCommandDefinition
        {
            Type = "fire",
            MaximumSpawnCount = DefinitionCatalog.MaximumSameTickSpawnCount + 1
        });

        Assert.Contains("unsupported", Assert.Throws<DefinitionValidationException>(
            () => WithPatterns(baseline, new[] { unknown })).Message);
        Assert.Contains("minimum interval", Assert.Throws<DefinitionValidationException>(
            () => WithPatterns(baseline, new[] { shortWait })).Message);
        Assert.Contains("below weapon", Assert.Throws<DefinitionValidationException>(
            () => WithPatterns(weaponCatalog, new[] { understated })).Message);
        Assert.Contains("same-tick", Assert.Throws<DefinitionValidationException>(
            () => WithPatterns(baseline, new[] { sameTick })).Message);
    }

    [Fact]
    public void PausedSimulationFreezesTimelineAndRestartDiscardsItsEntityState()
    {
        var baseline = TestDefinitions.Create(spawnTime: 0, enemySpeed: 0);
        var pattern = Motion("motion", Command("wait", P(("duration", 1))));
        var enemyDefinition = baseline.GetEnemy("enemy") with { MotionPatternId = "motion" };
        var definitions = new DefinitionCatalog(
            baseline.Game, baseline.Players.Values, new[] { enemyDefinition }, baseline.Bullets.Values,
            baseline.Weapons.Values, baseline.Stages.Values, patterns: new[] { pattern });
        var simulation = new ShootingSimulation(new MemoryDefinitionRepository(definitions), new MutableInputState());
        simulation.Tick(default);
        simulation.Tick(default);
        var enemy = simulation.World.Query<EnemyComponent, MotionTimelineComponent>().Single();
        var elapsed = enemy.Get<MotionTimelineComponent>().Elapsed;

        simulation.SetPaused(true);
        simulation.Tick(default);
        Assert.Equal(elapsed, enemy.Get<MotionTimelineComponent>().Elapsed);

        simulation.Restart();
        Assert.DoesNotContain(enemy, simulation.World.Entities);
    }

    private static Vector2[] FireVelocities(
        DefinitionCatalog definitions,
        string weaponId,
        long seed,
        string? difficultyId,
        Vector2? heading = null)
    {
        var world = new World();
        _ = world.CreateEntity()
            .Add(new PlayerComponent("player", 1))
            .Add(new TransformComponent(new Vector2(100, 200)))
            .Add(new ColliderComponent(1, CollisionLayer.Player));
        var owner = world.CreateEntity()
            .Add(new EnemyComponent("enemy"))
            .Add(new TransformComponent(new Vector2(100, 100)))
            .Add(new VelocityComponent(heading ?? Vector2.UnitY));
        var store = new ProjectileStore();
        var system = new AdvancedWeaponSystem(
            new BulletFactory(), RuntimeCapabilityRegistry.CreateBuiltIn(), new SeededRandomSource(seed), difficultyId);
        system.FireOnce(world, definitions, owner, weaponId, new SimulationTelemetry(), store);
        store.CommitSpawns();
        return Enumerable.Range(0, store.ActiveCount).Select(index => store.GetSnapshot(index).Velocity).ToArray();
    }

    private static WeaponDefinition ProjectileWeapon(string id, EmitterDefinition emitter) => new()
    {
        SchemaVersion = 2,
        Id = id,
        ActionType = "projectile",
        Pattern = new CapabilityDefinition { Type = "spread", Parameters = P(("projectileCount", 1), ("spreadDegrees", 0)) },
        Emitters = new[] { emitter }
    };

    private static PatternDefinition Motion(string id, params TimelineCommandDefinition[] commands) =>
        new() { Id = id, Kind = "motion", Commands = commands };

    private static PatternDefinition Attack(string id, params TimelineCommandDefinition[] commands) =>
        new() { Id = id, Kind = "attack", Commands = commands };

    private static TimelineCommandDefinition Command(
        string type,
        IReadOnlyDictionary<string, JsonElement> parameters) =>
        new() { Type = type, Parameters = parameters };

    private static TimelineCommandDefinition Fire(string weaponId) => new()
    {
        Type = "fire",
        Parameters = P(("weaponId", weaponId)),
        MaximumSpawnCount = 1
    };

    private static TimelineCommandDefinition Reference(string type, string patternId, int repeatCount = 1) => new()
    {
        Type = type,
        PatternId = patternId,
        RepeatCount = repeatCount
    };

    private static IReadOnlyDictionary<string, JsonElement> P(params (string Name, object Value)[] values) =>
        values.ToDictionary(static value => value.Name, static value => JsonSerializer.SerializeToElement(value.Value), StringComparer.Ordinal);

    private static DefinitionCatalog WithPatterns(DefinitionCatalog baseline, IEnumerable<PatternDefinition> patterns) => new(
        baseline.Game, baseline.Players.Values, baseline.Enemies.Values, baseline.Bullets.Values,
        baseline.Weapons.Values, baseline.Stages.Values,
        baseline.Ships.Values.Where(ship => !baseline.Players.ContainsKey(ship.Id)),
        baseline.Projectiles.Values.Where(projectile => !baseline.Bullets.ContainsKey(projectile.Id)),
        baseline.Items.Values, patterns, baseline.Bosses.Values, baseline.RuleSets.Values,
        baseline.Difficulties.Values, baseline.Visuals.Values, baseline.Audio.Values);

    private static DefinitionCatalog WithWeapons(
        DefinitionCatalog baseline,
        IEnumerable<WeaponDefinition> weapons,
        IEnumerable<DifficultyDefinition>? difficulties = null) => new(
            baseline.Game, baseline.Players.Values, baseline.Enemies.Values, baseline.Bullets.Values,
            baseline.Weapons.Values.Concat(weapons), baseline.Stages.Values,
            baseline.Ships.Values.Where(ship => !baseline.Players.ContainsKey(ship.Id)),
            baseline.Projectiles.Values.Where(projectile => !baseline.Bullets.ContainsKey(projectile.Id)),
            baseline.Items.Values, baseline.Patterns.Values, baseline.Bosses.Values,
            baseline.RuleSets.Values, difficulties ?? baseline.Difficulties.Values, baseline.Visuals.Values, baseline.Audio.Values);
}
