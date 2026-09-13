using System.Numerics;
using GoatShooooting.Core;
using GoatShooooting.Definitions;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class BossPhaseSystemTests
{
    [Fact]
    public void ThreePhaseBossKeepsEntityAndResolvesHpBombTimeoutAndRewardsInOrder()
    {
        var definitions = CreateDefinitions();
        var bossDefinition = definitions.GetBoss("boss");
        var world = new World();
        var boss = new EnemyFactory().Create(
            world,
            definitions.GetEnemy("enemy"),
            new Vector2(100, 100),
            boss: bossDefinition);
        var player = world.CreateEntity()
            .Add(new PlayerComponent("player", 1))
            .Add(new TransformComponent(new Vector2(100, 120)))
            .Add(new BombComponent(2, 20));
        var projectiles = new ProjectileStore();
        QueueEnemyProjectile(projectiles, boss.Id, ProjectileCancelResistance.Soft);
        QueueEnemyProjectile(projectiles, boss.Id, ProjectileCancelResistance.Hard);
        projectiles.CommitSpawns();
        var telemetry = new SimulationTelemetry();
        var events = new GameEventBuffer();
        var system = new BossPhaseSystem();
        events.BeginTick(0);

        system.BeginAndAdvance(world, definitions, 0, projectiles, telemetry, events);

        var state = boss.Get<BossComponent>();
        Assert.Equal("phase-1", state.PhaseId);
        Assert.Equal("opening", state.CheckpointId);
        Assert.Equal(10, boss.Get<HealthComponent>().Maximum);
        Assert.True(boss.Has<MotionTimelineComponent>());
        Assert.True(boss.Has<AttackTimelineComponent>());
        Assert.IsType<BossPhaseStartedEvent>(events.Events.Single());
        var render = Assert.Single(new RenderSystem().Capture(world).Where(item => item.EntityId == boss.Id));
        Assert.Equal("Test Boss", render.BossName);
        Assert.Equal("Opening", render.BossPhaseName);

        events.BeginTick(1);
        system.BeginAndAdvance(world, definitions, 0.6f, projectiles, telemetry, events);
        render = Assert.Single(new RenderSystem().Capture(world).Where(item => item.EntityId == boss.Id));
        Assert.True(render.BossWarning);
        Assert.InRange(render.BossRemainingTime!.Value, 0.399f, 0.401f);

        events.BeginTick(2);
        system.BeginAndAdvance(world, definitions, 0.4f, projectiles, telemetry, events);
        new DamageSystem().Update(new[] { new DamageEvent(boss, 10) }, telemetry, events);
        system.Resolve(world, definitions, projectiles, new SeededRandomSource(1), telemetry, events);

        Assert.Equal("phase-2", state.PhaseId);
        Assert.Equal(20, boss.Get<HealthComponent>().Maximum);
        Assert.False(events.Events.OfType<BossPhaseEndedEvent>().Single().TimedOut);
        var firstBonus = events.Events.OfType<BossPhaseBonusEvent>().Single();
        Assert.Equal(100, firstBonus.BaseBonus);
        Assert.Equal(0, firstBonus.TimeBonus);
        Assert.Equal(30, firstBonus.NoMissBonus);
        Assert.Equal(40, firstBonus.NoBombBonus);
        Assert.True(projectiles.GetSnapshot(0).PendingRemoval);
        Assert.False(projectiles.GetSnapshot(1).PendingRemoval);
        Assert.Single(world.Query<ItemComponent>());
        Assert.Equal(boss.Id, state.PhaseIndex >= 0 ? boss.Id : 0);

        boss.Get<InvincibilityComponent>().Remaining = 0;
        events.BeginTick(3);
        var bombDamage = new BombSystem().Update(
            world,
            projectiles,
            new BombInputState(),
            effectRadius: 200,
            telemetry,
            events);
        new DamageSystem().Update(bombDamage, telemetry, events);
        system.Resolve(world, definitions, projectiles, new SeededRandomSource(2), telemetry, events);

        Assert.Equal("phase-3", state.PhaseId);
        Assert.Equal(0, events.Events.OfType<BossPhaseBonusEvent>().Single().NoBombBonus);
        Assert.True(projectiles.GetSnapshot(1).PendingRemoval);

        events.BeginTick(4);
        system.BeginAndAdvance(world, definitions, 3, projectiles, telemetry, events);
        system.Resolve(world, definitions, projectiles, new SeededRandomSource(3), telemetry, events);

        Assert.True(state.IsComplete);
        Assert.True(boss.Has<PendingDestroyComponent>());
        Assert.True(events.Events.OfType<BossPhaseEndedEvent>().Single().TimedOut);
        Assert.Single(events.Events.OfType<BossCompletedEvent>());
        Assert.Equal(1, telemetry.BossesKilled);
        Assert.Equal(1, telemetry.EnemiesKilled);
        Assert.Same(state, boss.Get<BossComponent>());
        _ = player;
    }

    [Fact]
    public void HpDepletionWinsWhenHpAndTimeoutBecomeTrueOnSameTick()
    {
        var definitions = CreateDefinitions();
        var world = new World();
        var boss = new EnemyFactory().Create(
            world,
            definitions.GetEnemy("enemy"),
            Vector2.Zero,
            boss: definitions.GetBoss("boss"));
        var projectiles = new ProjectileStore();
        var telemetry = new SimulationTelemetry();
        var events = new GameEventBuffer();
        var system = new BossPhaseSystem();
        events.BeginTick(0);
        system.BeginAndAdvance(world, definitions, 0, projectiles, telemetry, events);

        events.BeginTick(1);
        system.BeginAndAdvance(world, definitions, 1, projectiles, telemetry, events);
        new DamageSystem().Update(new[] { new DamageEvent(boss, 10) }, telemetry, events);
        system.Resolve(world, definitions, projectiles, new SeededRandomSource(0), telemetry, events);

        Assert.False(events.Events.OfType<BossPhaseEndedEvent>().Single().TimedOut);
        Assert.False(boss.Has<PendingDestroyComponent>());
    }

    [Fact]
    public void StageObjectiveWaitsForBossCompletionAndSurvivesEntityCleanup()
    {
        var definitions = CreateDefinitions();
        var world = new World();
        var stage = new StageSystem(definitions.GetStage("stage"), new EnemyFactory());
        var telemetry = new SimulationTelemetry();
        stage.Tick(world, definitions, telemetry);
        var boss = Assert.Single(world.Query<BossComponent>());
        var bossSystem = new BossPhaseSystem();
        var projectiles = new ProjectileStore();
        var events = new GameEventBuffer();
        events.BeginTick(0);
        bossSystem.BeginAndAdvance(world, definitions, 0, projectiles, telemetry, events);
        Assert.False(stage.IsCleared(world, telemetry));

        for (var phase = 0; phase < 3; phase++)
        {
            events.BeginTick(phase + 1);
            boss.Get<HealthComponent>().Current = 0;
            bossSystem.Resolve(world, definitions, projectiles, new SeededRandomSource(phase), telemetry, events);
            stage.ObserveEvents(events.Events);
        }

        new CleanupSystem().Update(world);
        Assert.True(stage.IsCleared(world, telemetry));
    }

    [Fact]
    public void SimulationAdvancesToNextStageAndRetryCreatesFreshBossState()
    {
        var definitions = CreateDefinitions(nextStage: true, singlePhase: true);
        var simulation = new ShootingSimulation(
            new MemoryDefinitionRepository(definitions),
            new MutableInputState());

        for (var tick = 0; tick < 20 && simulation.CurrentStage.Id == "stage"; tick++) simulation.Tick(default);

        Assert.Equal("stage-2", simulation.CurrentStage.Id);
        Assert.Empty(simulation.World.Query<BossComponent>());
        simulation.Tick(default);
        Assert.Equal(SimulationStatus.StageClear, simulation.Status);
        simulation.Restart();
        simulation.Tick(default);
        var restarted = Assert.Single(simulation.World.Query<BossComponent>());
        Assert.Equal("phase-1", restarted.Get<BossComponent>().PhaseId);
        Assert.Equal(10, restarted.Get<HealthComponent>().Current);
    }

    [Fact]
    public void DefinitionValidationRejectsDuplicateCheckpointAndUnknownObjectiveBoss()
    {
        var valid = CreateDefinitions();
        var boss = valid.GetBoss("boss");
        var duplicate = boss with
        {
            Phases = boss.Phases.Select(phase => phase with { CheckpointId = "duplicate" }).ToArray()
        };
        var duplicateException = Assert.Throws<DefinitionValidationException>(() => Rebuild(valid, new[] { duplicate }, valid.Stages.Values));
        Assert.Contains("duplicate checkpoint", duplicateException.Message);

        var badStage = valid.GetStage("stage") with
        {
            Objectives = new[] { new StageObjectiveDefinition { Type = "complete-boss", BossId = "missing" } }
        };
        var referenceException = Assert.Throws<DefinitionValidationException>(() => Rebuild(valid, valid.Bosses.Values, new[] { badStage }));
        Assert.Contains("Unknown boss", referenceException.Message);
    }

    private static DefinitionCatalog CreateDefinitions(bool nextStage = false, bool singlePhase = false)
    {
        var baseline = TestDefinitions.Create(spawnTime: 0, enemySpeed: 0);
        var phases = new[]
        {
            Phase("phase-1", "Opening", 10, 1, "opening", invulnerability: 0, endCancel: "soft", drop: true,
                baseBonus: 100, timeBonus: 10, noMiss: 30, noBomb: 40),
            Phase("phase-2", "Pressure", 20, 2, "pressure", invulnerability: 0.5f, startCancel: "none", endCancel: "none",
                noBomb: 50),
            Phase("phase-3", "Finale", 30, 3, "finale", startCancel: "all", endCancel: "all")
        };
        if (singlePhase) phases = new[] { Phase("phase-1", "Opening", 10, 0.05f, "opening") };
        var boss = new BossDefinition
        {
            Id = "boss",
            DisplayName = "Test Boss",
            EnemyId = "enemy",
            WarningSeconds = 0.5f,
            Phases = phases
        };
        var firstStage = baseline.GetStage("stage") with
        {
            NextStageId = nextStage ? "stage-2" : null,
            Events = new[]
            {
                new StageEventDefinition
                {
                    Time = 0, Type = "spawn-enemy", EnemyId = "enemy", BossId = "boss", X = 100, Y = 100
                }
            },
            Objectives = new[] { new StageObjectiveDefinition { Type = "complete-boss", BossId = "boss" } }
        };
        var stages = nextStage
            ? new[]
            {
                firstStage,
                new StageDefinition
                {
                    Id = "stage-2", Events = Array.Empty<StageEventDefinition>(),
                    Objectives = new[] { new StageObjectiveDefinition { Type = "defeat-all-enemies" } }
                }
            }
            : new[] { firstStage };
        var game = baseline.Game with { StageId = "stage" };
        return new DefinitionCatalog(
            game,
            baseline.Players.Values,
            baseline.Enemies.Values,
            baseline.Bullets.Values,
            baseline.Weapons.Values,
            stages,
            items: new[] { new ItemDefinition { Id = "phase-item", Kind = "power", Value = 1, VisualId = "item" } },
            patterns: new[]
            {
                new PatternDefinition { Id = "motion", Kind = "motion" },
                new PatternDefinition { Id = "attack", Kind = "attack" }
            },
            bosses: new[] { boss },
            visuals: new[] { new VisualDefinition { Id = "item", AssetId = "item.png" } });
    }

    private static BossPhaseDefinition Phase(
        string id,
        string name,
        int hp,
        float time,
        string checkpoint,
        float invulnerability = 0,
        string startCancel = "none",
        string endCancel = "none",
        bool drop = false,
        long baseBonus = 0,
        long timeBonus = 0,
        long noMiss = 0,
        long noBomb = 0) => new()
        {
            Id = id,
            DisplayName = name,
            Hp = hp,
            TimeLimit = time,
            MotionPatternId = "motion",
            AttackPatternIds = new[] { "attack" },
            CheckpointId = checkpoint,
            InvulnerabilitySeconds = invulnerability,
            StartProjectileCancel = startCancel,
            EndProjectileCancel = endCancel,
            BaseBonus = baseBonus,
            TimeBonusPerSecond = timeBonus,
            NoMissBonus = noMiss,
            NoBombBonus = noBomb,
            DropTable = drop ? new[] { new DropEntryDefinition { ItemId = "phase-item" } } : Array.Empty<DropEntryDefinition>()
        };

    private static DefinitionCatalog Rebuild(
        DefinitionCatalog baseline,
        IEnumerable<BossDefinition> bosses,
        IEnumerable<StageDefinition> stages) => new(
            baseline.Game,
            baseline.Players.Values,
            baseline.Enemies.Values,
            baseline.Bullets.Values,
            baseline.Weapons.Values,
            stages,
            items: baseline.Items.Values,
            patterns: baseline.Patterns.Values,
            bosses: bosses,
            visuals: baseline.Visuals.Values);

    private static void QueueEnemyProjectile(
        ProjectileStore projectiles,
        int ownerId,
        ProjectileCancelResistance resistance) => projectiles.QueueSpawn(new ProjectileSpawnCommand(
            ownerId,
            ProjectileTeam.Enemy,
            "bullet",
            Vector2.Zero,
            Vector2.UnitY,
            1,
            1,
            5,
            "bullet",
            CancelResistance: resistance));

    private sealed class BombInputState : IInputState
    {
        public float MoveX => 0;
        public float MoveY => 0;
        public bool Fire => false;
        public bool Bomb => true;
        public bool Retry => false;
        public bool Pause => false;
    }
}
