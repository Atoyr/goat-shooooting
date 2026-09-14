using System.Numerics;
using System.Text.Json;
using GoatShooooting.Core;
using GoatShooooting.Definitions;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class SpecialGaugeAndRankSystemTests
{
    [Fact]
    public void GaugeChargesFromTypedEventsActivatesStageAndCancelsProjectiles()
    {
        var rules = Rules(Special(
            ("activation", "\"staged\""), ("stageCost", "20"), ("maximumLevel", "3"),
            ("damageCharge", "1"), ("killCharge", "5"), ("grazeCharge", "2"),
            ("cancelCharge", "3"), ("itemCharge", "4"), ("lockCharge", "2"),
            ("drainPerSecond", "10"), ("scoreMultiplier", "2"),
            ("cancelProjectiles", "true"), ("visualCue", "\"drive-flare\""), ("audioCue", "\"drive-on\"")));
        var capabilities = RuntimeCapabilityRegistry.CreateBuiltIn();
        var system = SpecialGaugeSystem.Create(rules, capabilities);
        var state = new RunState();
        system.Reset(state, 0, specialPressed: false);
        var events = new GameEventBuffer();
        events.BeginTick(0);
        events.Publish((frame, sequence) => new EnemyDamagedEvent(frame, sequence, 2, 10));
        events.Publish((frame, sequence) => new EnemyDestroyedEvent(frame, sequence, 2, "enemy"));
        events.Publish((frame, sequence) => new PlayerGrazedEvent(frame, sequence, 1, 1));
        events.Publish((frame, sequence) => new ProjectileCancelledEvent(frame, sequence, 1));
        events.Publish((frame, sequence) => new ItemCollectedEvent(frame, sequence, 1, "item", 1));
        events.Publish((frame, sequence) => new TargetsLockedEvent(frame, sequence, 1, 3));
        var projectiles = EnemyProjectileStore();
        var telemetry = new SimulationTelemetry();

        system.Observe(events.Events, state, 1, projectiles, telemetry, events);
        Assert.Equal(30, state.Gauge);

        events.BeginTick(1);
        var world = new World();
        var player = CreatePlayer(world);
        system.BeginTick(
            new InputFrame(0, 0, InputButtons.Special),
            SimulationTiming.TickDurationSeconds,
            state,
            player,
            projectiles,
            telemetry,
            events);

        Assert.Equal(SpecialGaugePhase.Active, state.SpecialPhase);
        Assert.Equal(1, state.SpecialLevel);
        Assert.Equal(2, state.SpecialScoreMultiplier);
        Assert.True(projectiles.GetSnapshot(0).PendingRemoval);
        var activated = Assert.Single(events.Events.OfType<SpecialActivatedEvent>());
        Assert.Equal("drive-flare", activated.VisualCue);
        Assert.Equal("drive-on", activated.AudioCue);

        var focusProjectiles = EnemyProjectileStore(ProjectileCancelResistance.Soft, ProjectileCancelResistance.Hard);
        events.BeginTick(2);
        system.BeginTick(
            new InputFrame(0, 0, InputButtons.Focus),
            SimulationTiming.TickDurationSeconds,
            state,
            player,
            focusProjectiles,
            telemetry,
            events);
        Assert.True(focusProjectiles.GetSnapshot(0).PendingRemoval);
        Assert.False(focusProjectiles.GetSnapshot(1).PendingRemoval);
        Assert.Single(events.Events.OfType<ProjectileCancelledEvent>());
        var cancelled = Assert.Single(events.Events.OfType<ProjectileCancelledEvent>());
        Assert.Equal("special-gauge", cancelled.Source);
        Assert.Equal(0, cancelled.X);
        Assert.Equal(0, cancelled.Y);

        events.BeginTick(3);
        events.Publish((frame, sequence) => new EnemyDestroyedEvent(frame, sequence, 2, "enemy", 100));
        ScoreRulePipeline.Create(rules, capabilities).Apply(events.Events, state, events);
        Assert.Equal(200, state.Score);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BombAndDeathEndAnActiveGauge(bool bomb)
    {
        var rules = Rules(Special(
            ("activation", "\"manual\""), ("stageCost", "10"), ("maximumLevel", "1"),
            ("drainPerSecond", "1"), ("cooldownSeconds", "2"),
            ("endOnBomb", "true"), ("endOnDeath", "true")));
        var system = SpecialGaugeSystem.Create(rules, RuntimeCapabilityRegistry.CreateBuiltIn());
        var state = new RunState();
        system.Reset(state, 10, specialPressed: false);
        var events = new GameEventBuffer();
        events.BeginTick(0);
        var player = CreatePlayer(new World());
        system.BeginTick(new InputFrame(0, 0, InputButtons.Special), 0, state, player,
            new ProjectileStore(), new SimulationTelemetry(), events);
        Assert.Equal(SpecialGaugePhase.Active, state.SpecialPhase);

        events.BeginTick(1);
        if (bomb)
            events.Publish((frame, sequence) => new BombUsedEvent(frame, sequence, player.Id));
        else
            events.Publish((frame, sequence) => new PlayerDiedEvent(frame, sequence, player.Id, 1));
        system.Observe(events.Events, state, player.Id, new ProjectileStore(), new SimulationTelemetry(), events);

        Assert.Equal(SpecialGaugePhase.Cooldown, state.SpecialPhase);
        Assert.Equal(0, state.Gauge);
        Assert.Equal(bomb ? "bomb" : "death", Assert.Single(events.Events.OfType<SpecialEndedEvent>()).Reason);
    }

    [Fact]
    public void FullAutomaticGaugeActivatesFromTheEventThatFillsIt()
    {
        var rules = Rules(Special(
            ("activation", "\"automatic\""), ("stageCost", "10"), ("maximumLevel", "1"),
            ("killCharge", "10"), ("drainPerSecond", "5")));
        var system = SpecialGaugeSystem.Create(rules, RuntimeCapabilityRegistry.CreateBuiltIn());
        var state = new RunState();
        system.Reset(state, 0, specialPressed: false);
        var events = new GameEventBuffer();
        events.BeginTick(0);
        events.Publish((frame, sequence) => new EnemyDestroyedEvent(frame, sequence, 2, "enemy"));

        system.Observe(events.Events, state, 1, new ProjectileStore(), new SimulationTelemetry(), events);

        Assert.Equal(SpecialGaugePhase.Active, state.SpecialPhase);
        Assert.Equal(1, state.SpecialLevel);
        Assert.Single(events.Events.OfType<SpecialActivatedEvent>());
    }

    [Fact]
    public void DifficultyRankAndSpecialComposeForTheSameWeaponPattern()
    {
        var definitions = TestDefinitions.Create(bulletSpeed: 100, cooldown: 0.5f, enemyHp: 10);
        var state = new RunState();
        var rank = new RankRule(0.5, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0.5f, 0.25, 0, 0, string.Empty);
        var special = new SpecialGaugeRule("manual", 10, 1, 0, 0, 0, 0, 0, 0, 1, 0, 0,
            true, true, 2, 0.5f, 2, false, false, string.Empty, string.Empty, string.Empty);
        var difficulty = new DifficultyDefinition
        {
            Id = "expert",
            ProjectileSpeedMultiplier = 2,
            FireIntervalMultiplier = 0.5f,
            EnemyHpMultiplier = 1.5f,
            AdditionalProjectileCount = 2,
            PatternTags = new[] { "dense" }
        };
        var modifiers = new RunModifierState();
        var rankSystem = RankSystem.Create(Rules(null, Rank(("initial", "0.5"))),
            RuntimeCapabilityRegistry.CreateBuiltIn());
        rankSystem.Reset(state);
        var specialSystem = SpecialGaugeSystem.Create(Rules(Special(
            ("activation", "\"manual\""), ("stageCost", "10"), ("damageMultiplier", "2"),
            ("fireIntervalMultiplier", "0.5"), ("drainPerSecond", "1"))),
            RuntimeCapabilityRegistry.CreateBuiltIn());
        specialSystem.Reset(state, 10, specialPressed: false);
        var activationEvents = new GameEventBuffer();
        activationEvents.BeginTick(0);
        specialSystem.BeginTick(new InputFrame(0, 0, InputButtons.Special), 0, state,
            CreatePlayer(new World()), new ProjectileStore(), new SimulationTelemetry(), activationEvents);
        modifiers.Configure(difficulty, rank, special, state);
        var capabilities = RuntimeCapabilityRegistry.CreateBuiltIn();
        var world = new World();
        var enemy = new EnemyFactory(capabilities, modifiers).Create(
            world, definitions.GetEnemy("enemy"), new Vector2(100, 100));
        enemy.Add(new WeaponHolderComponent("weapon"));
        var system = new AdvancedWeaponSystem(new BulletFactory(capabilities), capabilities, modifiers: modifiers);
        var projectiles = new ProjectileStore();

        system.Update(world, definitions, new MutableInputState(), 0, new SimulationTelemetry(), projectiles);
        projectiles.CommitSpawns();

        Assert.Equal(15, enemy.Get<HealthComponent>().Current);
        Assert.Equal(5, projectiles.ActiveCount);
        Assert.All(Enumerable.Range(0, projectiles.ActiveCount).Select(projectiles.GetSnapshot),
            projectile => Assert.InRange(projectile.Velocity.Length(), 299.99f, 300.01f));
        Assert.True(modifiers.IsPatternEnabled(new[] { "dense" }));
        Assert.Equal(2, modifiers.PlayerDamageMultiplier);
        Assert.Equal(0.5f, modifiers.PlayerFireIntervalMultiplier);
        Assert.Equal(2, modifiers.ScoreMultiplier);
        Assert.InRange(
            enemy.Get<WeaponRuntimeComponent>().States["weapon/primary"].CooldownRemaining,
            0.18749f,
            0.18751f);
    }

    [Fact]
    public void RankSourcesLossesAndClampAreObservable()
    {
        var rankCapability = Rank(
            ("initial", "0.5"), ("minimum", "0"), ("maximum", "1"),
            ("damageGain", "0.1"), ("grazeGain", "0.2"), ("bombLoss", "0.4"));
        var rules = Rules(null, rankCapability);
        var capabilities = RuntimeCapabilityRegistry.CreateBuiltIn();
        var system = RankSystem.Create(rules, capabilities);
        var state = new RunState();
        system.Reset(state);
        var events = new GameEventBuffer();
        events.BeginTick(0);
        events.Publish((frame, sequence) => new EnemyDamagedEvent(frame, sequence, 2, 10));
        events.Publish((frame, sequence) => new PlayerGrazedEvent(frame, sequence, 1, 3));
        events.Publish((frame, sequence) => new BombUsedEvent(frame, sequence, 1));

        system.Observe(events.Events, 0, state, new World(), TestDefinitions.Create(),
            new ProjectileStore(), new SimulationTelemetry(), events);

        Assert.Equal(1, state.Rank);
        var changed = Assert.Single(events.Events.OfType<RankChangedEvent>());
        Assert.Equal(0.5, changed.Previous);
        Assert.Equal(1, changed.Current);
    }

    [Fact]
    public void RankCanMapEnemyDestructionToRevengeProjectiles()
    {
        var rules = Rules(null, Rank(
            ("initial", "1"), ("maximum", "1"), ("revengeEvery", "0.5"),
            ("revengeCount", "1"), ("revengeProjectileId", "\"bullet\"")));
        var capabilities = RuntimeCapabilityRegistry.CreateBuiltIn();
        var system = RankSystem.Create(rules, capabilities);
        var state = new RunState();
        system.Reset(state);
        var world = new World();
        var enemy = world.CreateEntity().Add(new TransformComponent(new Vector2(10, 20)));
        var events = new GameEventBuffer();
        events.BeginTick(0);
        events.Publish((frame, sequence) => new EnemyDestroyedEvent(frame, sequence, enemy.Id, "enemy"));
        var projectiles = new ProjectileStore();

        system.Observe(events.Events, 0, state, world, TestDefinitions.Create(), projectiles,
            new SimulationTelemetry(), events);
        projectiles.CommitSpawns();

        Assert.Equal(2, projectiles.ActiveCount);
        Assert.All(Enumerable.Range(0, projectiles.ActiveCount).Select(projectiles.GetSnapshot),
            projectile => Assert.Equal(ProjectileTeam.Enemy, projectile.Team));
    }

    [Fact]
    public void TimeAttackUsesInitialResourcesAndStopsOnItsFrameBoundary()
    {
        var baseline = TestDefinitions.Create(spawnTime: 10, playerLives: 2, playerBombs: 2);
        var rules = new RuleSetDefinition
        {
            Id = "sprint",
            StageRouteId = "sprint-route",
            StageIds = new[] { "stage" },
            InitialLives = 4,
            InitialBombs = 5,
            InitialPower = 7,
            InitialGauge = 9,
            MaximumGauge = 10,
            TimeLimitSeconds = 2f / SimulationTiming.TicksPerSecond,
            ClearCondition = "time-attack"
        };
        var catalog = CopyWithRules(baseline, rules);
        var simulation = new ShootingSimulation(
            new MemoryDefinitionRepository(catalog),
            new MutableInputState(),
            new RunConfiguration("test", 1, ruleSetId: "sprint"));

        Assert.Equal(4, simulation.Player.Get<LivesComponent>().Remaining);
        Assert.Equal(5, simulation.Player.Get<BombComponent>().Remaining);
        Assert.Equal(7, simulation.RunState.Power);
        Assert.Equal(9, simulation.DebugSnapshot.Gauge);
        simulation.Tick(default);
        simulation.Tick(default);
        simulation.Tick(default);

        Assert.Equal(SimulationStatus.TimeExpired, simulation.Status);
        Assert.Single(simulation.Events.Events.OfType<TimeAttackEndedEvent>());
        Assert.Equal(simulation.RunState.Rank, simulation.DebugSnapshot.Rank);
        Assert.Equal(simulation.DebugSnapshot.Rank, simulation.ReplayMetadata.Rank);
        Assert.Equal(simulation.DebugSnapshot.Gauge, simulation.ReplayMetadata.Gauge);
    }

    [Fact]
    public void InvalidGaugeRankAndTimeAttackDefinitionsFailValidation()
    {
        var baseline = TestDefinitions.Create();
        var invalidGauge = Rules(Special(("activation", "\"typo\"")));
        var invalidRank = Rules(null, Rank(("minimum", "1"), ("initial", "0"), ("maximum", "2"))) with { Id = "rank" };
        var invalidTime = Rules() with { Id = "time", ClearCondition = "time-attack" };
        var missingCancelItem = Rules(Special(("cancelItemId", "\"missing\""))) with { Id = "cancel-item" };
        var capabilities = RuntimeCapabilityRegistry.CreateBuiltIn();

        Assert.Throws<DefinitionValidationException>(() =>
            new CapabilityValidator().Validate(CopyWithRules(baseline, invalidGauge), capabilities));
        Assert.Throws<DefinitionValidationException>(() =>
            new CapabilityValidator().Validate(CopyWithRules(baseline, invalidRank), capabilities));
        Assert.Throws<DefinitionValidationException>(() => CopyWithRules(baseline, invalidTime));
        Assert.Throws<DefinitionValidationException>(() => CopyWithRules(baseline, missingCancelItem));
    }

    private static RuleSetDefinition Rules(
        CapabilityDefinition? special = null,
        CapabilityDefinition? rank = null) => new()
        {
            Id = "rules",
            StageRouteId = "route",
            StageIds = new[] { "stage" },
            MaximumGauge = 100,
            SpecialGaugeRule = special,
            RankRule = rank
        };

    private static CapabilityDefinition Special(params (string Name, string Json)[] values) =>
        Capability("radiant-drive", values);

    private static CapabilityDefinition Rank(params (string Name, string Json)[] values) =>
        Capability("dynamic-rank", values);

    private static CapabilityDefinition Capability(string type, params (string Name, string Json)[] values) => new()
    {
        Type = type,
        Parameters = values.ToDictionary(
            static pair => pair.Name,
            static pair => JsonDocument.Parse(pair.Json).RootElement.Clone(),
            StringComparer.Ordinal)
    };

    private static Entity CreatePlayer(World world) => world.CreateEntity()
        .Add(new TransformComponent(Vector2.Zero))
        .Add(new InvincibilityComponent(1));

    private static ProjectileStore EnemyProjectileStore(
        params ProjectileCancelResistance[] resistances)
    {
        var result = new ProjectileStore();
        if (resistances.Length == 0) resistances = new[] { ProjectileCancelResistance.Soft };
        foreach (var resistance in resistances)
        {
            result.QueueSpawn(new ProjectileSpawnCommand(
                2,
                ProjectileTeam.Enemy,
                "shot",
                Vector2.Zero,
                Vector2.UnitY,
                1,
                1,
                10,
                "shot",
                CancelResistance: resistance));
        }
        result.CommitSpawns();
        return result;
    }

    private static DefinitionCatalog CopyWithRules(DefinitionCatalog baseline, params RuleSetDefinition[] rules) => new(
        baseline.Game,
        baseline.Players.Values,
        baseline.Enemies.Values,
        baseline.Bullets.Values,
        baseline.Weapons.Values,
        baseline.Stages.Values,
        Array.Empty<ShipDefinition>(),
        Array.Empty<ProjectileDefinition>(),
        baseline.Items.Values,
        baseline.Patterns.Values,
        baseline.Bosses.Values,
        rules,
        baseline.Difficulties.Values,
        baseline.Visuals.Values,
        baseline.Audio.Values);
}
