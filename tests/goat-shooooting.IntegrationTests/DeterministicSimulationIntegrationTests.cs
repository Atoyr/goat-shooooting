using GoatShooooting.Core;
using GoatShooooting.Definitions;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.IntegrationTests;

public sealed class DeterministicSimulationIntegrationTests
{
    [Fact]
    public void RecordedRunPlaysBackToMatchingHashScoreBreakdownAndClear()
    {
        var definitions = CreateDefinitionsWithEmptyStage(resultsDuration: 0);
        var configuration = new RunConfiguration("test", 123);
        var original = new ShootingSimulation(
            new MemoryDefinitionRepository(definitions),
            new MutableInputState(),
            configuration);
        var contentHash = DefinitionContentHasher.Compute(definitions);
        var recorder = new ReplayRecorder(configuration, contentHash, DateTimeOffset.UnixEpoch, hashInterval: 1);
        var input = new InputFrame(12, -3, InputButtons.Fire);

        original.Tick(input);
        recorder.Record(input, original);
        var replay = recorder.Complete(original);
        ReplayValidator.Validate(replay, contentHash);

        var playback = new ShootingSimulation(
            new MemoryDefinitionRepository(definitions),
            new MutableInputState(),
            replay.Header.Configuration);
        var session = new ReplayPlaybackSession(replay);
        session.Step(playback);

        Assert.True(session.IsComplete);
        Assert.Equal(replay.Result.FinalStateHash, playback.ComputeCanonicalStateHash());
        Assert.Equal(replay.Result.Score, playback.RunState.Score);
        Assert.Equal(replay.Result.ScoreBreakdown, playback.RunState.ScoreBreakdown);
        Assert.True(replay.Result.Cleared);
    }

    [Fact]
    public void PlaybackReportsTheFirstDesyncFrame()
    {
        var definitions = CreateDefinitionsWithEmptyStage(resultsDuration: 0);
        var configuration = new RunConfiguration("test", 5);
        var simulation = new ShootingSimulation(
            new MemoryDefinitionRepository(definitions), new MutableInputState(), configuration);
        var recorder = new ReplayRecorder(
            configuration, DefinitionContentHasher.Compute(definitions), DateTimeOffset.UnixEpoch, 1);
        simulation.Tick(default);
        recorder.Record(default, simulation);
        var replay = recorder.Complete(simulation);
        replay = replay with
        {
            Checkpoints = replay.Checkpoints.Select(checkpoint => checkpoint with { StateHash = 1 }).ToArray(),
            Result = replay.Result with { FinalStateHash = 1 }
        };
        var playback = new ShootingSimulation(
            new MemoryDefinitionRepository(definitions), new MutableInputState(), configuration);

        var exception = Assert.Throws<ReplayException>(() => new ReplayPlaybackSession(replay).Step(playback));

        Assert.Equal(ReplayErrorCode.Desync, exception.Code);
        Assert.Contains("frame 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SameConfigurationAndInputsProduceSameCanonicalHash()
    {
        var first = CreateSimulation(seed: 31415);
        var second = CreateSimulation(seed: 31415);
        var inputs = CreateInputSequence(180);

        foreach (var input in inputs)
        {
            first.Tick(input);
            second.Tick(input);
            Assert.Equal(first.ComputeCanonicalStateHash(), second.ComputeCanonicalStateHash());
        }

        Assert.Equal(180, first.RunState.Frame);
        Assert.Equal(first.Telemetry.Score, first.RunState.Score);
    }

    [Fact]
    public void SeedAndInputAreBothRepresentedInCanonicalState()
    {
        var baseline = CreateSimulation(seed: 1);
        var changedSeed = CreateSimulation(seed: 2);
        var changedInput = CreateSimulation(seed: 1);

        for (var frame = 0; frame < 30; frame++)
        {
            var input = new InputFrame(64, 0, InputButtons.Fire);
            baseline.Tick(input);
            changedSeed.Tick(input);
            changedInput.Tick(new InputFrame(-64, 0, InputButtons.Fire));
        }

        Assert.NotEqual(baseline.ComputeCanonicalStateHash(), changedSeed.ComputeCanonicalStateHash());
        Assert.NotEqual(baseline.ComputeCanonicalStateHash(), changedInput.ComputeCanonicalStateHash());
    }

    [Fact]
    public void TypedEventsAreOrderedAndFeedbackIsDerivedFromThem()
    {
        var simulation = new ShootingSimulation(
            new MemoryDefinitionRepository(CreateDefinitions(spawnTime: 0, enemyHp: 10)),
            new MutableInputState(),
            new RunConfiguration("test", 0));

        simulation.Tick(new InputFrame(0, 0, InputButtons.Bomb));

        Assert.Collection(
            simulation.Events.Events,
            gameplayEvent => Assert.IsType<BombUsedEvent>(gameplayEvent),
            gameplayEvent => Assert.IsType<EnemyDamagedEvent>(gameplayEvent),
            gameplayEvent => Assert.IsType<EnemyDestroyedEvent>(gameplayEvent),
            gameplayEvent => Assert.IsType<ScoreAwardedEvent>(gameplayEvent),
            gameplayEvent => Assert.IsType<StageClearedEvent>(gameplayEvent),
            gameplayEvent => Assert.IsType<AllClearedEvent>(gameplayEvent));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, simulation.Events.Events.Select(static item => item.Sequence));
        Assert.All(simulation.Events.Events, static gameplayEvent => Assert.Equal(0, gameplayEvent.Frame));
        Assert.Equal(new SimulationFeedback(Hits: 1, EnemiesDestroyed: 1, PlayerHits: 0, BombsUsed: 1),
            simulation.Feedback);
        Assert.Equal(simulation.Telemetry.Score, simulation.RunState.Score);
    }

    [Fact]
    public void PauseOpeningResultsAndRetryHaveExplicitFrameRules()
    {
        var definitions = CreateDefinitions(spawnTime: 10, enemyHp: 10, openingDuration: 1);
        var simulation = new ShootingSimulation(
            new MemoryDefinitionRepository(definitions),
            new MutableInputState(),
            new RunConfiguration("test", 0));

        simulation.Tick(new InputFrame(0, 0, InputButtons.Pause));
        Assert.True(simulation.IsPaused);
        Assert.Equal(0, simulation.RunState.Frame);
        Assert.Empty(simulation.Events.Events);

        simulation.Tick(default);
        simulation.Tick(new InputFrame(0, 0, InputButtons.Pause));
        Assert.False(simulation.IsPaused);
        Assert.Equal(1, simulation.RunState.Frame);

        for (var frame = 1; frame < 60; frame++)
        {
            simulation.Tick(default);
        }

        Assert.Equal(StagePhase.Playing, simulation.Phase);
        Assert.Equal(60, simulation.RunState.Frame);

        var retryRandom = new SeededRandomSource(77);
        var terminal = new ShootingSimulation(
            new MemoryDefinitionRepository(CreateDefinitionsWithEmptyStage(resultsDuration: 1)),
            new MutableInputState(),
            new RunConfiguration("test", 77),
            retryRandom);
        terminal.Tick(default);
        Assert.Equal(StagePhase.Results, terminal.Phase);
        for (var frame = 0; frame < 60; frame++)
        {
            terminal.Tick(default);
        }

        Assert.Equal(SimulationStatus.StageClear, terminal.Status);
        Assert.Equal(61, terminal.RunState.Frame);
        terminal.Tick(default);
        Assert.Equal(61, terminal.RunState.Frame);

        retryRandom.NextUInt32();
        terminal.Tick(new InputFrame(0, 0, InputButtons.Retry));
        Assert.Equal(SimulationStatus.Running, terminal.Status);
        Assert.Equal(0, terminal.RunState.Frame);
        Assert.Equal(unchecked((ulong)77), retryRandom.State);
        Assert.Empty(terminal.Events.Events);
    }

    [Fact]
    public void SuccessfulHotReloadResetsRunWhileFailureKeepsLastGoodAndContinues()
    {
        var repository = new StubReloadableRepository(CreateDefinitions(spawnTime: 10, enemyHp: 10));
        var random = new SeededRandomSource(99);
        var simulation = new ShootingSimulation(
            repository,
            new MutableInputState(),
            new RunConfiguration("test", 99),
            random);
        simulation.Tick(new InputFrame(32, 0));
        Assert.Equal(1, simulation.RunState.Frame);
        random.NextUInt32();

        repository.Next = new DefinitionReloadResult(
            CreateDefinitions(spawnTime: 10, enemyHp: 20),
            null);
        simulation.Tick(default);

        Assert.Equal(1, simulation.DefinitionReloadCount);
        Assert.Equal(0, simulation.RunState.Frame);
        Assert.Empty(simulation.Events.Events);
        Assert.Equal(20, simulation.Definitions.GetEnemy("enemy").Hp);
        Assert.Equal(unchecked((ulong)99), random.State);

        var lastGoodWorld = simulation.World;
        repository.Next = new DefinitionReloadResult(null, "invalid content");
        simulation.Tick(default);

        Assert.Same(lastGoodWorld, simulation.World);
        Assert.Equal(1, simulation.RunState.Frame);
        Assert.Equal("invalid content", simulation.DefinitionReloadError);
    }

    [Fact]
    public void LegacyUpdateAccumulatesPartialTimeIntoFixedTicks()
    {
        var simulation = CreateSimulation(seed: 0);

        simulation.Update(1f / 120f);
        Assert.Equal(0, simulation.RunState.Frame);

        simulation.Update(1f / 120f);
        Assert.Equal(1, simulation.RunState.Frame);

        simulation.Update(59f / 60f);
        Assert.Equal(60, simulation.RunState.Frame);
        Assert.Equal(1, simulation.Elapsed);
    }

    private static ShootingSimulation CreateSimulation(long seed) => new(
        new MemoryDefinitionRepository(CreateDefinitions(spawnTime: 100, enemyHp: 20)),
        new MutableInputState(),
        new RunConfiguration("test", seed));

    private static IReadOnlyList<InputFrame> CreateInputSequence(int count) => Enumerable
        .Range(0, count)
        .Select(frame => new InputFrame(
            (sbyte)(frame % 120 < 60 ? 64 : -64),
            (sbyte)(frame % 90 < 45 ? -32 : 32),
            InputButtons.Fire))
        .ToArray();

    private static DefinitionCatalog CreateDefinitions(
        float spawnTime,
        int enemyHp,
        float openingDuration = 0)
    {
        var catalog = CreateDefinitionsWithEmptyStage(resultsDuration: 0);
        return new DefinitionCatalog(
            catalog.Game,
            catalog.Players.Values,
            catalog.Enemies.Values.Select(enemy => enemy with { Hp = enemyHp }),
            catalog.Bullets.Values,
            catalog.Weapons.Values,
            new[]
            {
                new StageDefinition
                {
                    Id = "stage",
                    OpeningDuration = openingDuration,
                    Events = new[]
                    {
                        new StageEventDefinition
                        {
                            Time = spawnTime,
                            Type = "spawn-enemy",
                            EnemyId = "enemy",
                            X = 400,
                            Y = 300
                        }
                    }
                }
            });
    }

    private static DefinitionCatalog CreateDefinitionsWithEmptyStage(float resultsDuration) => new(
        new GameDefinition { PlayerId = "player", StageId = "stage", Width = 800, Height = 600 },
        new[]
        {
            new PlayerDefinition
            {
                Id = "player", Lives = 3, Bombs = 2, BombDamage = 50,
                Speed = 200, WeaponId = "weapon", X = 400, Y = 550, Radius = 10
            }
        },
        new[]
        {
            new EnemyDefinition { Id = "enemy", Hp = 10, Speed = 0, Radius = 10, Score = 100 }
        },
        new[]
        {
            new BulletDefinition
            {
                Id = "bullet", Speed = 100, Damage = 10, Radius = 3, Lifetime = 5
            }
        },
        new[]
        {
            new WeaponDefinition { Id = "weapon", BulletId = "bullet", Cooldown = 0.1f }
        },
        new[]
        {
            new StageDefinition
            {
                Id = "stage",
                ResultsDuration = resultsDuration,
                Events = Array.Empty<StageEventDefinition>()
            }
        });

    private sealed class StubReloadableRepository(DefinitionCatalog initial) : IReloadableDefinitionRepository
    {
        public DefinitionReloadResult? Next { get; set; }

        public DefinitionCatalog Load() => initial;

        public DefinitionReloadResult? PollChanges()
        {
            var result = Next;
            Next = null;
            return result;
        }
    }
}
