using System.Numerics;
using GoatShooooting.Core;
using GoatShooooting.Definitions;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class PlayerLifeCycleAndItemSystemTests
{
    [Fact]
    public void AutoBombRescuesHitBeforeLifeLossAndDistinguishesUsage()
    {
        var (world, player) = CreatePlayer(initialLives: 2, initialBombs: 2, initialPower: 20);
        var projectiles = new ProjectileStore();
        projectiles.QueueSpawn(EnemyProjectile(player.Get<TransformComponent>().Position));
        projectiles.CommitSpawns();
        var telemetry = new SimulationTelemetry();
        var events = StartedEvents();

        var damage = new PlayerLifeCycleSystem().ResolveHits(
            world,
            new[] { new DamageEvent(player, 1, projectiles.GetSnapshot(0).Id) },
            projectiles,
            Rules() with { AutoBombCost = 2, BombInvincibilitySeconds = 0.5f },
            autoBombEnabled: true,
            bombEffectRadius: 100,
            new BombSystem(),
            new RunState(),
            telemetry,
            events);

        Assert.Empty(damage);
        Assert.Equal(PlayerLifeCycleState.BombRescue, player.Get<PlayerLifeCycleComponent>().State);
        Assert.Equal(2, player.Get<LivesComponent>().Remaining);
        Assert.Equal(0, player.Get<BombComponent>().Remaining);
        Assert.Equal(1, telemetry.AutoBombsUsed);
        Assert.Empty(events.Events.OfType<PlayerHitEvent>());
        Assert.Equal(BombUsageKind.Auto, Assert.Single(events.Events.OfType<BombUsedEvent>()).Kind);
        Assert.True(projectiles.GetSnapshot(0).PendingRemoval);
    }

    [Fact]
    public void DeathRunsDyingRespawningInvincibleActiveAndAppliesResources()
    {
        var (world, player) = CreatePlayer(initialLives: 2, initialBombs: 1, initialPower: 20);
        player.Get<TransformComponent>().Position = new Vector2(300, 300);
        var runState = new RunState();
        var telemetry = new SimulationTelemetry();
        var events = StartedEvents();
        var system = new PlayerLifeCycleSystem();
        var projectiles = new ProjectileStore();
        projectiles.QueueSpawn(EnemyProjectile(new Vector2(20, 20)) with
        {
            CanBeCancelled = false,
            CancelResistance = ProjectileCancelResistance.Uncancelable
        });
        projectiles.CommitSpawns();

        system.ResolveHits(
            world,
            new[] { new DamageEvent(player, 1, 42) },
            projectiles,
            Rules(),
            autoBombEnabled: false,
            bombEffectRadius: 100,
            new BombSystem(),
            runState,
            telemetry,
            events);

        Assert.Equal(PlayerLifeCycleState.Dying, player.Get<PlayerLifeCycleComponent>().State);
        Assert.Equal(1, player.Get<LivesComponent>().Remaining);
        Assert.Equal(15, player.Get<ShipComponent>().Power);
        Assert.Equal(15, runState.Power);
        Assert.Single(events.Events.OfType<PlayerHitEvent>());
        Assert.Single(events.Events.OfType<PlayerDiedEvent>());
        Assert.True(projectiles.GetSnapshot(0).PendingRemoval);
        Assert.Single(world.Query<ExplosionComponent>());
        Assert.Empty(new RenderSystem().Capture(world).Where(item => item.Kind == RenderKind.Player));

        system.Advance(world, 0.1f, telemetry, events);
        Assert.Equal(PlayerLifeCycleState.Respawning, player.Get<PlayerLifeCycleComponent>().State);
        Assert.False(player.Get<PlayerLifeCycleComponent>().CanAct);
        system.Advance(world, 0.2f, telemetry, events);
        Assert.Equal(PlayerLifeCycleState.Invincible, player.Get<PlayerLifeCycleComponent>().State);
        Assert.Equal(new Vector2(100, 200), player.Get<TransformComponent>().Position);
        Assert.Equal(2, player.Get<BombComponent>().Remaining);
        Assert.Single(events.Events.OfType<PlayerRespawnedEvent>());
        Assert.Single(new RenderSystem().Capture(world).Where(item => item.Kind == RenderKind.Player));
        system.Advance(world, 0.3f, telemetry, events);
        Assert.Equal(PlayerLifeCycleState.Active, player.Get<PlayerLifeCycleComponent>().State);
    }

    [Fact]
    public void FinalLifeTransitionsToGameOverPendingWithoutDestroyingPlayerState()
    {
        var (world, player) = CreatePlayer(initialLives: 1);
        var system = new PlayerLifeCycleSystem();

        system.ResolveHits(
            world,
            new[] { new DamageEvent(player, 1, 1) },
            new ProjectileStore(),
            Rules(),
            false,
            100,
            new BombSystem(),
            new RunState(),
            new SimulationTelemetry(),
            StartedEvents());
        system.Advance(world, 0.1f, new SimulationTelemetry(), StartedEvents());

        Assert.Equal(PlayerLifeCycleState.GameOverPending, player.Get<PlayerLifeCycleComponent>().State);
        Assert.Contains(player, world.Entities);
    }

    [Fact]
    public void ItemsApplyAllKindsCapsAndMaximumPowerConversion()
    {
        var (world, player) = CreatePlayer(initialLives: 2, initialBombs: 1, initialPower: 95);
        var rules = Rules() with { MaximumPowerItemScoreValue = 500, MaximumGauge = 100 };
        var runState = new RunState();
        var telemetry = new SimulationTelemetry();
        var events = StartedEvents();
        var factory = new ItemFactory();
        foreach (var item in new[]
        {
            Item("power", "power", 10),
            Item("power-at-cap", "power", 1),
            Item("score", "score", 250),
            Item("bomb", "bomb", 20),
            Item("life", "life", 1),
            Item("gauge", "gauge", 150)
        })
        {
            factory.Create(world, item, player.Get<TransformComponent>().Position, Vector2.Zero, telemetry, events);
        }

        new ItemSystem().Update(world, rules, 0, 600, runState, telemetry, events);
        ScoreRulePipeline.Create(rules, RuntimeCapabilityRegistry.CreateBuiltIn())
            .Apply(events.Events, runState, events);

        Assert.Equal(100, player.Get<ShipComponent>().Power);
        Assert.Equal(100, runState.Power);
        Assert.Equal(750, runState.Score);
        Assert.Equal(9, player.Get<BombComponent>().Remaining);
        Assert.Equal(3, player.Get<LivesComponent>().Remaining);
        Assert.Equal(100, runState.Gauge);
        Assert.Equal(6, telemetry.ItemsCollected);
        Assert.Equal(6, events.Events.OfType<ItemCollectedEvent>().Count());
        Assert.Single(events.Events.OfType<PowerChangedEvent>());
        Assert.Single(events.Events.OfType<ExtendAwardedEvent>());
    }

    [Fact]
    public void DropScatterIsDeterministicAndCollectionLineMagnetizesItems()
    {
        var (world, player) = CreatePlayer();
        player.Get<TransformComponent>().Position = new Vector2(100, 50);
        var enemy = world.CreateEntity()
            .Add(new TransformComponent(new Vector2(100, 200)))
            .Add(new EnemyComponent("dropper"));
        var definitions = CreateItemCatalog();
        var telemetry = new SimulationTelemetry();
        var events = StartedEvents();
        events.Publish((frame, sequence) => new EnemyDestroyedEvent(frame, sequence, enemy.Id, "dropper"));

        new ItemDropSystem().SpawnDrops(
            world, definitions, events.Events, new SeededRandomSource(123), telemetry, events);

        var items = world.Query<ItemComponent, ItemMotionComponent, TransformComponent>().ToArray();
        Assert.Equal(2, items.Length);
        Assert.Equal(2, events.Events.OfType<ItemSpawnedEvent>().Count());
        var firstVelocity = items[0].Get<ItemMotionComponent>().Velocity;
        Assert.NotEqual(Vector2.Zero, firstVelocity);
        Assert.Equal(2, new RenderSystem().Capture(world).Count(item => item.Kind == RenderKind.Item));

        new ItemSystem().Update(world, Rules(), 0.1f, 600, new RunState(), telemetry, events);
        Assert.All(items, item => Assert.Equal(
            ItemMotionState.Magnetized,
            item.Get<ItemMotionComponent>().State));
        Assert.All(items, item => Assert.True(item.Get<TransformComponent>().Position.Y < 200));

        var focusedItem = new ItemFactory().Create(
            world,
            Item("focus-power", "power", 1),
            new Vector2(100, 350),
            Vector2.Zero,
            telemetry,
            events);
        player.Get<TransformComponent>().Position = new Vector2(100, 450);
        player.Get<ShipComponent>().IsFocused = true;
        new ItemSystem().Update(world, Rules(), 0.1f, 600, new RunState(), telemetry, events);
        Assert.Equal(ItemMotionState.Magnetized, focusedItem.Get<ItemMotionComponent>().State);
    }

    [Fact]
    public void ScoreExtendThresholdIsClaimedOnce()
    {
        var (world, player) = CreatePlayer(initialLives: 2);
        var runState = new RunState();
        var telemetry = new SimulationTelemetry();
        var events = StartedEvents();
        var item = new ItemFactory().Create(
            world, Item("score", "score", 1000), player.Get<TransformComponent>().Position,
            Vector2.Zero, telemetry, events);
        var rules = Rules() with { ExtendScoreThresholds = new long[] { 1000 } };
        new ItemSystem().Update(world, rules, 0, 600, runState, telemetry, events);
        ScoreRulePipeline.Create(rules, RuntimeCapabilityRegistry.CreateBuiltIn())
            .Apply(events.Events, runState, events);
        var system = new ExtendSystem();

        system.Update(player, rules, runState, telemetry, events);
        system.Update(player, rules, runState, telemetry, events);

        Assert.True(item.Has<PendingDestroyComponent>());
        Assert.Equal(3, player.Get<LivesComponent>().Remaining);
        Assert.Single(events.Events.OfType<ExtendAwardedEvent>()
            .Where(award => award.Source == "score"));
        Assert.Single(runState.ClaimedExtendThresholds);
    }

    [Fact]
    public void ManualBombWinsSameTickRaceWithProjectileHit()
    {
        var simulation = CreateSimulation(autoBomb: false, credits: 0);
        simulation.Projectiles.QueueSpawn(EnemyProjectile(simulation.Player.Get<TransformComponent>().Position));
        simulation.Projectiles.CommitSpawns();
        var lives = simulation.Player.Get<LivesComponent>().Remaining;

        simulation.Tick(new InputFrame(0, 0, InputButtons.Bomb));

        Assert.Equal(lives, simulation.Player.Get<LivesComponent>().Remaining);
        Assert.Equal(BombUsageKind.Manual, Assert.Single(simulation.Events.Events.OfType<BombUsedEvent>()).Kind);
        Assert.Empty(simulation.Events.Events.OfType<PlayerHitEvent>());
    }

    [Fact]
    public void DifficultyAutoBombIsAppliedByProductionSimulation()
    {
        var simulation = CreateSimulation(autoBomb: true, credits: 0);
        simulation.Projectiles.QueueSpawn(EnemyProjectile(simulation.Player.Get<TransformComponent>().Position));
        simulation.Projectiles.CommitSpawns();

        simulation.Tick(default);

        Assert.Equal(1, simulation.Player.Get<LivesComponent>().Remaining);
        Assert.Equal(0, simulation.Player.Get<BombComponent>().Remaining);
        Assert.Equal(PlayerLifeCycleState.BombRescue, simulation.Player.Get<PlayerLifeCycleComponent>().State);
        Assert.Equal(BombUsageKind.Auto, Assert.Single(simulation.Events.Events.OfType<BombUsedEvent>()).Kind);
        Assert.Empty(simulation.Events.Events.OfType<PlayerHitEvent>());
    }

    [Fact]
    public void ContinueConsumesCreditAndMarksRunResult()
    {
        var simulation = CreateSimulation(autoBomb: false, credits: 1);
        simulation.Projectiles.QueueSpawn(EnemyProjectile(simulation.Player.Get<TransformComponent>().Position));
        simulation.Projectiles.CommitSpawns();
        simulation.Tick(default);
        for (var frame = 0; frame < 30 && simulation.Status == SimulationStatus.Running; frame++)
        {
            simulation.Tick(default);
        }

        Assert.Equal(SimulationStatus.GameOver, simulation.Status);
        Assert.False(simulation.Result!.Continued);
        simulation.Tick(new InputFrame(0, 0, InputButtons.Continue));

        Assert.Equal(SimulationStatus.Running, simulation.Status);
        Assert.True(simulation.RunState.Continued);
        Assert.Equal(1, simulation.RunState.ContinuesUsed);
        Assert.Equal(0, simulation.RunState.CreditsRemaining);
        Assert.Single(simulation.Events.Events.OfType<ContinueUsedEvent>());
        Assert.False(simulation.TryContinue());
    }

    [Fact]
    public void DefinitionsRejectUnknownDropAndUnorderedExtendThresholds()
    {
        var unknownDrop = Assert.Throws<DefinitionValidationException>(() =>
            CreateCatalog(autoBomb: false, credits: 0, dropItemId: "missing"));
        var unorderedThresholds = Assert.Throws<DefinitionValidationException>(() =>
            CreateCatalog(autoBomb: false, credits: 0, thresholds: new long[] { 2000, 1000 }));

        Assert.Contains("Unknown item", unknownDrop.Message, StringComparison.Ordinal);
        Assert.Contains("extend thresholds", unorderedThresholds.Message, StringComparison.Ordinal);
    }

    private static (World World, Entity Player) CreatePlayer(
        int initialLives = 2,
        int initialBombs = 1,
        int initialPower = 0)
    {
        var world = new World();
        var ship = new ShipDefinition
        {
            Id = "ship",
            HitRadius = 3,
            GrazeRadius = 20,
            NormalSpeed = 200,
            FocusSpeed = 100,
            InitialLives = initialLives,
            InitialBombs = initialBombs,
            InitialPower = initialPower,
            MaximumPower = 100,
            MaximumLives = 9,
            MaximumBombs = 9,
            DeathAnimationSeconds = 0.1f,
            RespawnDelaySeconds = 0.2f,
            RespawnInvincibilitySeconds = 0.3f,
            PowerLossOnDeath = 5,
            BombsAfterRespawn = 2,
            NormalWeaponIds = new[] { "weapon" },
            FocusWeaponIds = new[] { "weapon" }
        };
        return (world, new PlayerFactory().Create(world, ship, new Vector2(100, 200)));
    }

    private static RuleSetDefinition Rules() => new()
    {
        Id = "rules",
        StageRouteId = "main",
        StageIds = new[] { "stage" },
        AllowContinue = true,
        ManualBombCost = 1,
        AutoBombCost = 1,
        BombInvincibilitySeconds = 0.5f,
        CollectionLineY = 120,
        ItemFallSpeed = 90,
        ItemMagnetSpeed = 500,
        FocusMagnetRadius = 120,
        ItemCollectionRadius = 18
    };

    private static ItemDefinition Item(string id, string kind, int value) => new()
    {
        Id = id,
        Kind = kind,
        Value = value,
        VisualId = "item"
    };

    private static GameEventBuffer StartedEvents()
    {
        var events = new GameEventBuffer();
        events.BeginTick(0);
        return events;
    }

    private static ProjectileSpawnCommand EnemyProjectile(Vector2 position) => new(
        99,
        ProjectileTeam.Enemy,
        "enemy-shot",
        position,
        Vector2.Zero,
        3,
        1,
        5,
        "shot");

    private static DefinitionCatalog CreateItemCatalog() => CreateCatalog(autoBomb: false, credits: 0);

    private static ShootingSimulation CreateSimulation(bool autoBomb, int credits)
    {
        var definitions = CreateCatalog(autoBomb, credits);
        return new ShootingSimulation(
            new MemoryDefinitionRepository(definitions),
            new MutableInputState(),
            new RunConfiguration("test", 123, "rules", "difficulty", "ship"));
    }

    private static DefinitionCatalog CreateCatalog(
        bool autoBomb,
        int credits,
        string dropItemId = "power",
        IReadOnlyList<long>? thresholds = null)
    {
        var rules = Rules() with
        {
            InitialCredits = credits,
            ExtendScoreThresholds = thresholds ?? Array.Empty<long>()
        };
        var items = new[] { Item("power", "power", 1) };
        return new DefinitionCatalog(
            new GameDefinition { Id = "test", PlayerId = "legacy", StageId = "stage", Width = 800, Height = 600 },
            new[]
            {
                new PlayerDefinition
                {
                    Id = "legacy", Lives = 1, Bombs = 1, Speed = 200, WeaponId = "legacy-weapon",
                    X = 400, Y = 550, Radius = 3
                }
            },
            new[]
            {
                new EnemyDefinition
                {
                    Id = "dropper", Hp = 1, Speed = 0, Radius = 5,
                    DropTable = new[] { new DropEntryDefinition { ItemId = dropItemId, Count = 2, ScatterSpeed = 80 } }
                }
            },
            new[] { new BulletDefinition { Id = "legacy-bullet", Speed = 100, Damage = 1, Radius = 2, Lifetime = 5 } },
            new[] { new WeaponDefinition { Id = "legacy-weapon", BulletId = "legacy-bullet", Cooldown = 1 } },
            new[]
            {
                new StageDefinition
                {
                    Id = "stage",
                    Events = new[]
                    {
                        new StageEventDefinition
                        {
                            Time = 100, Type = "spawn-enemy", EnemyId = "dropper", X = 400, Y = 100
                        }
                    }
                }
            },
            new[]
            {
                new ShipDefinition
                {
                    Id = "ship", HitRadius = 3, GrazeRadius = 20, NormalSpeed = 200, FocusSpeed = 100,
                    InitialLives = 1, InitialBombs = 1, MaximumPower = 100, MaximumLives = 9, MaximumBombs = 9,
                    DeathAnimationSeconds = 0.1f, RespawnDelaySeconds = 0.1f,
                    RespawnInvincibilitySeconds = 0.1f, BombsAfterRespawn = 1,
                    NormalWeaponIds = new[] { "legacy-weapon" }, FocusWeaponIds = new[] { "legacy-weapon" }
                }
            },
            items: items,
            ruleSets: new[] { rules },
            difficulties: new[] { new DifficultyDefinition { Id = "difficulty", AutoBomb = autoBomb } },
            visuals: new[] { new VisualDefinition { Id = "item", AssetId = "item" } });
    }
}
