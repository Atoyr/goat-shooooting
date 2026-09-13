using GoatShooooting.Core;
using GoatShooooting.Definitions;

namespace GoatShooooting.Runtime;

public enum SimulationStatus
{
    Running,
    StageClear,
    GameOver,
    TimeExpired
}

public enum StagePhase
{
    Opening,
    Playing,
    Results
}

/// <summary>Headless-capable composition root for the production game simulation.</summary>
public sealed class ShootingSimulation
{
    private readonly IDefinitionRepository _definitionRepository;
    private readonly IInputState _legacyInput;
    private readonly TickInputState _tickInput = new();
    private readonly IRandomSource _randomSource;
    private readonly RuntimeCapabilityRegistry _capabilities;
    private readonly RunModifierState _modifierState = new();
    private readonly PlayerInputSystem _playerInputSystem = new();
    private readonly WeaponSystem _weaponSystem;
    private readonly EnemyFactory _enemyFactory;
    private readonly ProjectileMovementSystem _projectileMovementSystem = new();
    private readonly ProjectileLifetimeSystem _projectileLifetimeSystem = new();
    private readonly ActorSpatialGrid _actorSpatialGrid = new();
    private readonly ProjectileCollisionSystem _projectileCollisionSystem = new();
    private readonly OptionFollowSystem _optionFollowSystem = new();
    private readonly LaserSystem _laserSystem = new();
    private readonly PlayerLifeCycleSystem _playerLifeCycleSystem = new();
    private readonly ItemDropSystem _itemDropSystem = new();
    private readonly BossPhaseSystem _bossPhaseSystem;
    private readonly ItemSystem _itemSystem = new();
    private readonly ExtendSystem _extendSystem = new();
    private readonly MovementSystem _movementSystem = new();
    private readonly TransformHistorySystem _transformHistorySystem = new();
    private readonly MotionTimelineSystem _motionTimelineSystem = new();
    private readonly AttackTimelineSystem _attackTimelineSystem;
    private readonly MovementPatternSystem _movementPatternSystem = new();
    private readonly PlayerBoundsSystem _playerBoundsSystem = new();
    private readonly OutOfBoundsSystem _outOfBoundsSystem = new();
    private readonly BombSystem _bombSystem = new();
    private readonly DamageSystem _damageSystem = new();
    private readonly InvincibilitySystem _invincibilitySystem = new();
    private readonly FeedbackSystem _feedbackSystem = new();
    private readonly CleanupSystem _cleanupSystem = new();
    private StageSystem _stageSystem = null!;
    private bool _pauseWasPressed;
    private bool _retryWasPressed;
    private double _phaseElapsed;
    private long _phaseTicks;
    private double _legacyAccumulator;
    private long _stageStartScore;
    private int _restartGeneration;
    private System.Numerics.Vector2 _playerStartPosition;
    private ScoreRulePipeline _scorePipeline = null!;
    private SpecialGaugeSystem _specialGaugeSystem = null!;
    private RankSystem _rankSystem = null!;
    private RunRuleSystem _runRuleSystem = null!;

    public ShootingSimulation(IDefinitionRepository definitionRepository, IInputState input)
        : this(definitionRepository, input, new RunConfiguration("legacy", seed: 0))
    {
    }

    public ShootingSimulation(
        IDefinitionRepository definitionRepository,
        IInputState input,
        RunConfiguration configuration,
        IRandomSource? randomSource = null,
        RuntimeCapabilityRegistry? capabilities = null)
    {
        ArgumentNullException.ThrowIfNull(definitionRepository);
        ArgumentNullException.ThrowIfNull(configuration);
        _definitionRepository = definitionRepository;
        _legacyInput = input ?? throw new ArgumentNullException(nameof(input));
        Configuration = configuration;
        _randomSource = randomSource ?? new SeededRandomSource(configuration.Seed);
        _capabilities = capabilities ?? RuntimeCapabilityRegistry.CreateBuiltIn();
        _enemyFactory = new EnemyFactory(_capabilities, _modifierState);
        _weaponSystem = new WeaponSystem(
            new BulletFactory(_capabilities),
            _capabilities,
            _randomSource,
            configuration.DifficultyId,
            _modifierState);
        _attackTimelineSystem = new AttackTimelineSystem(_weaponSystem.Advanced);
        _bossPhaseSystem = new BossPhaseSystem(_itemDropSystem, _modifierState);
        Definitions = _definitionRepository.Load();
        new CapabilityValidator().Validate(Definitions, _capabilities);
        World = null!;
        Player = null!;
        Telemetry = null!;
        RunState = new RunState();
        Events = new GameEventBuffer();
        _weaponSystem.Advanced.Events = Events;
        Restart();
    }

    public World World { get; private set; }
    public DefinitionCatalog Definitions { get; private set; }
    public Entity Player { get; private set; }
    public ShipDefinition CurrentShip { get; private set; } = null!;
    public RuleSetDefinition CurrentRuleSet { get; private set; } = null!;
    public DifficultyDefinition? CurrentDifficulty { get; private set; }
    public ProjectileStore Projectiles { get; private set; } = null!;
    public SimulationTelemetry Telemetry { get; private set; }
    public RunConfiguration Configuration { get; }
    public RunState RunState { get; }
    public GameEventBuffer Events { get; }
    public double Elapsed => _stageSystem.Elapsed;
    public StageDefinition CurrentStage { get; private set; } = null!;
    public int StageNumber { get; private set; }
    public StagePhase Phase { get; private set; }
    public double PhaseElapsed => _phaseElapsed;
    public long StageScore => RunState.Score - _stageStartScore;
    public long LastStageScore { get; private set; }
    public SimulationStatus Status { get; private set; }
    public bool IsPaused { get; private set; }
    public SimulationFeedback Feedback { get; private set; }
    public int DefinitionReloadCount { get; private set; }
    public string? DefinitionReloadError { get; private set; }
    public RunResult? Result { get; private set; }
    public RunDebugSnapshot DebugSnapshot => new(
        RunState.Frame,
        RunState.Rank,
        RunState.Gauge,
        RunState.SpecialPhase,
        RunState.SpecialLevel,
        RunState.SpecialTimeRemaining,
        RunState.SpecialCooldownRemaining);
    public RunMetadataSnapshot ReplayMetadata => new(
        Configuration,
        RunState.Frame,
        RunState.Rank,
        RunState.Gauge,
        RunState.SpecialPhase,
        RunState.SpecialLevel);
    internal ulong RandomState => _randomSource.State;
    internal IReadOnlyCollection<string> CompletedBossIds => _stageSystem.CompletedBossIds;

    /// <summary>Advances exactly one 60Hz simulation tick using an immutable input sample.</summary>
    public void Tick(InputFrame inputFrame) =>
        ProcessStep(inputFrame, SimulationTiming.TickDurationSeconds, advanceFrame: true, fixedTick: true);

    /// <summary>
    /// Compatibility adapter for existing callers. Positive elapsed time is accumulated into fixed ticks;
    /// a zero value performs the historical zero-time control/content pump without advancing the run frame.
    /// </summary>
    public void Update(float deltaTime)
    {
        if (deltaTime < 0 || !float.IsFinite(deltaTime))
        {
            throw new ArgumentOutOfRangeException(nameof(deltaTime), "Delta time must be finite and non-negative.");
        }

        Feedback = default;
        if (deltaTime == 0)
        {
            ProcessStep(InputFrame.Capture(_legacyInput), 0, advanceFrame: false, fixedTick: false);
            return;
        }

        _legacyAccumulator += deltaTime;
        var aggregateFeedback = default(SimulationFeedback);
        while (_legacyAccumulator + 1e-12 >= SimulationTiming.ExactTickDurationSeconds)
        {
            var generation = _restartGeneration;
            Tick(InputFrame.Capture(_legacyInput));
            aggregateFeedback += Feedback;
            if (_restartGeneration != generation)
            {
                Feedback = default;
                return;
            }

            _legacyAccumulator -= SimulationTiming.ExactTickDurationSeconds;
        }

        if (_legacyAccumulator < 1e-12)
        {
            _legacyAccumulator = 0;
        }

        Feedback = aggregateFeedback;
    }

    public ulong ComputeCanonicalStateHash() => SimulationStateHasher.Compute(this);

    public void Restart() => Restart(InputFrame.Capture(_legacyInput));

    public void SetPaused(bool isPaused)
    {
        if (Status == SimulationStatus.Running)
        {
            IsPaused = isPaused;
            _pauseWasPressed = _legacyInput.Pause;
        }
    }

    private void ProcessStep(InputFrame inputFrame, float deltaTime, bool advanceFrame, bool fixedTick)
    {
        _tickInput.Apply(inputFrame);
        Events.BeginTick(RunState.Frame);
        Feedback = default;
        if (TryReloadDefinitions(inputFrame))
        {
            return;
        }

        var retryPressed = inputFrame.IsPressed(InputButtons.Retry);
        if (Configuration.IsPractice && retryPressed && !_retryWasPressed)
        {
            Restart(inputFrame);
            return;
        }
        _retryWasPressed = retryPressed;

        if (Status != SimulationStatus.Running)
        {
            if (inputFrame.IsPressed(InputButtons.Continue) && TryContinue())
            {
                CompleteStep(advanceFrame);
            }
            else if (inputFrame.IsPressed(InputButtons.Retry))
            {
                Restart(inputFrame);
            }
            else
            {
                _feedbackSystem.Update(World, deltaTime);
                _cleanupSystem.Update(World);
            }

            return;
        }

        var pausePressed = inputFrame.IsPressed(InputButtons.Pause);
        if (pausePressed && !_pauseWasPressed)
        {
            IsPaused = !IsPaused;
        }

        _pauseWasPressed = pausePressed;
        if (IsPaused)
        {
            return;
        }

        _transformHistorySystem.BeginTick(World);

        if (_runRuleSystem.IsTimeExpired(RunState.Frame))
        {
            Events.Publish((frame, sequence) => new TimeAttackEndedEvent(
                frame,
                sequence,
                _runRuleSystem.TimeLimitFrames!.Value));
            Status = SimulationStatus.TimeExpired;
            CompleteStep(advanceFrame);
            return;
        }

        if (Phase == StagePhase.Opening)
        {
            AdvancePhaseTime(deltaTime, fixedTick);
            if (_phaseElapsed >= CurrentStage.OpeningDuration)
            {
                Phase = StagePhase.Playing;
                _phaseElapsed = 0;
                _phaseTicks = 0;
            }

            CompleteStep(advanceFrame);
            return;
        }

        if (Phase == StagePhase.Results)
        {
            _feedbackSystem.Update(World, deltaTime);
            _cleanupSystem.Update(World);
            AdvancePhaseTime(deltaTime, fixedTick);
            if (_phaseElapsed >= CurrentStage.ResultsDuration)
            {
                var scoreEventStart = Events.Events.Count;
                AdvanceStageOrFinish();
                ApplyScores(scoreEventStart);
                _extendSystem.Update(Player, CurrentRuleSet, RunState, Telemetry, Events);
            }

            CompleteStep(advanceFrame);
            return;
        }

        _playerLifeCycleSystem.Advance(World, deltaTime, Telemetry, Events);
        if (Player.Get<PlayerLifeCycleComponent>().State == PlayerLifeCycleState.GameOverPending)
        {
            Status = SimulationStatus.GameOver;
            CompleteStep(advanceFrame);
            return;
        }

        _specialGaugeSystem.BeginTick(
            inputFrame,
            deltaTime,
            RunState,
            Player,
            Projectiles,
            Telemetry,
            Events);

        if (fixedTick)
        {
            _stageSystem.Tick(World, Definitions, Telemetry);
        }
        else
        {
            _stageSystem.Update(World, Definitions, deltaTime, Telemetry);
        }
        _bossPhaseSystem.BeginAndAdvance(
            World,
            Definitions,
            deltaTime,
            Projectiles,
            Telemetry,
            Events);
        _playerInputSystem.Update(World, _tickInput);
        _motionTimelineSystem.Update(
            World,
            Definitions,
            CurrentDifficulty?.Id,
            deltaTime,
            Telemetry,
            CurrentDifficulty?.PatternTags);
        _attackTimelineSystem.Update(
            World,
            Definitions,
            CurrentDifficulty?.Id,
            deltaTime,
            Telemetry,
            Projectiles,
            CurrentDifficulty?.PatternTags);
        _weaponSystem.Update(World, Definitions, _tickInput, deltaTime, Telemetry, Projectiles);
        Projectiles.CommitSpawns(Events);
        _projectileMovementSystem.Update(
            Projectiles,
            World,
            deltaTime,
            Definitions.Game.Width,
            Definitions.Game.Height,
            Telemetry);
        _movementSystem.Update(World, deltaTime, Telemetry);
        _movementPatternSystem.Update(World, deltaTime);
        _playerBoundsSystem.Update(World, Definitions.Game.Width, Definitions.Game.Height);
        _outOfBoundsSystem.Update(World, Definitions.Game.Width, Definitions.Game.Height);
        _optionFollowSystem.Update(World, deltaTime);
        _invincibilitySystem.Update(World, deltaTime);
        _actorSpatialGrid.Rebuild(World);
        var bombDamage = _bombSystem.Update(
            World,
            Projectiles,
            _tickInput,
            Math.Min(Definitions.Game.Width, Definitions.Game.Height) * 0.4f,
            Telemetry,
            Events,
            CurrentRuleSet.ManualBombCost,
            CurrentRuleSet.BombInvincibilitySeconds);
        _damageSystem.Update(bombDamage, Telemetry, Events, World);
        var laserDamage = _laserSystem.Update(World, Projectiles, deltaTime, Telemetry, Events);
        var damageEvents = _projectileCollisionSystem.Detect(
            Projectiles,
            _actorSpatialGrid,
            Telemetry,
            Events);
        var allDamage = laserDamage.Concat(damageEvents).ToArray();
        var autoBombDamage = _playerLifeCycleSystem.ResolveHits(
            World,
            allDamage,
            Projectiles,
            CurrentRuleSet,
            CurrentDifficulty?.AutoBomb == true,
            Math.Min(Definitions.Game.Width, Definitions.Game.Height) * 0.4f,
            _bombSystem,
            RunState,
            Telemetry,
            Events);
        _damageSystem.Update(allDamage, Telemetry, Events, World);
        _damageSystem.Update(autoBombDamage, Telemetry, Events, World);
        _bossPhaseSystem.Resolve(
            World,
            Definitions,
            Projectiles,
            _randomSource,
            Telemetry,
            Events);
        _stageSystem.ObserveEvents(Events.Events);
        _itemDropSystem.SpawnDrops(World, Definitions, Events.Events, _randomSource, Telemetry, Events);
        _itemSystem.Update(
            World,
            CurrentRuleSet,
            deltaTime,
            Definitions.Game.Height,
            RunState,
            Telemetry,
            Events);
        _specialGaugeSystem.Observe(
            Events.Events,
            RunState,
            Player.Id,
            Projectiles,
            Telemetry,
            Events);
        _rankSystem.Observe(
            Events.Events,
            deltaTime,
            RunState,
            World,
            Definitions,
            Projectiles,
            Telemetry,
            Events);
        Projectiles.CommitSpawns(Events);
        ApplyScores(0);
        _extendSystem.Update(Player, CurrentRuleSet, RunState, Telemetry, Events);
        _projectileLifetimeSystem.Update(Projectiles);
        _feedbackSystem.Update(World, deltaTime);
        _cleanupSystem.Update(World);
        Projectiles.CommitRemovals();

        if (Player.Get<PlayerLifeCycleComponent>().State == PlayerLifeCycleState.GameOverPending)
        {
            Status = SimulationStatus.GameOver;
        }
        else if (_stageSystem.IsCleared(World, Telemetry) &&
            !World.Query<ItemComponent>().Any() &&
            Player.Get<PlayerLifeCycleComponent>().State is
                PlayerLifeCycleState.Active or PlayerLifeCycleState.Invincible)
        {
            var scoreEventStart = Events.Events.Count;
            BeginResults();
            ApplyScores(scoreEventStart);
            _extendSystem.Update(Player, CurrentRuleSet, RunState, Telemetry, Events);
            LastStageScore = StageScore;
            if (CurrentStage.ResultsDuration <= 0)
            {
                scoreEventStart = Events.Events.Count;
                AdvanceStageOrFinish();
                ApplyScores(scoreEventStart);
                _extendSystem.Update(Player, CurrentRuleSet, RunState, Telemetry, Events);
            }
        }

        CompleteStep(advanceFrame);
    }

    private void CompleteStep(bool advanceFrame)
    {
        Telemetry.Score = RunState.Score;
        Feedback = SimulationFeedback.FromEvents(Events.Events);
        if (advanceFrame)
        {
            RunState.Frame++;
        }

        if (Status != SimulationStatus.Running)
        {
            Result = new RunResult(
                Status,
                RunState.Score,
                RunState.Frame,
                RunState.Continued,
                RunState.ContinuesUsed,
                RunState.CreditsRemaining);
        }
    }

    private void AdvancePhaseTime(float deltaTime, bool fixedTick)
    {
        if (fixedTick)
        {
            _phaseTicks++;
            _phaseElapsed = _phaseTicks / (double)SimulationTiming.TicksPerSecond;
        }
        else
        {
            _phaseElapsed += deltaTime;
        }
    }

    private bool TryReloadDefinitions(InputFrame inputFrame)
    {
        if (_definitionRepository is not IReloadableDefinitionRepository reloadable)
        {
            return false;
        }

        var result = reloadable.PollChanges();
        if (result is null)
        {
            return false;
        }

        if (!result.Success)
        {
            DefinitionReloadError = result.Error;
            return false;
        }

        try
        {
            new CapabilityValidator().Validate(result.Catalog!, _capabilities);
        }
        catch (DefinitionValidationException exception)
        {
            DefinitionReloadError = exception.Message;
            return false;
        }

        Definitions = result.Catalog!;
        DefinitionReloadError = null;
        DefinitionReloadCount++;
        Restart(inputFrame);
        return true;
    }

    private void Restart(InputFrame inputFrame)
    {
        World = new World();
        Projectiles = new ProjectileStore();
        var shipId = Configuration.ShipId ??
            (!string.IsNullOrWhiteSpace(Definitions.Game.PlayerId)
                ? Definitions.Game.PlayerId
                : Definitions.Game.ShipIds[0]);
        CurrentShip = Definitions.GetShip(shipId);
        CurrentRuleSet = ResolveRuleSet();
        _scorePipeline = ScoreRulePipeline.Create(CurrentRuleSet, _capabilities);
        CurrentDifficulty = ResolveDifficulty();
        _specialGaugeSystem = SpecialGaugeSystem.Create(CurrentRuleSet, _capabilities);
        _rankSystem = RankSystem.Create(CurrentRuleSet, _capabilities);
        _runRuleSystem = new RunRuleSystem(CurrentRuleSet);
        _modifierState.Configure(CurrentDifficulty, _rankSystem.Rule, _specialGaugeSystem.Rule, RunState);
        var playerFactory = new PlayerFactory();
        if (Definitions.Players.TryGetValue(shipId, out var legacyPlayer))
        {
            _playerStartPosition = new System.Numerics.Vector2(legacyPlayer.X, legacyPlayer.Y);
            Player = playerFactory.Create(World, legacyPlayer);
        }
        else
        {
            _playerStartPosition = new System.Numerics.Vector2(
                Definitions.Game.Width / 2f,
                Definitions.Game.Height - Math.Max(48, CurrentShip.HitRadius * 4));
            Player = playerFactory.Create(World, CurrentShip, _playerStartPosition);
        }
        Telemetry = new SimulationTelemetry();
        var initialPower = Math.Min(
            CurrentShip.MaximumPower,
            Configuration.InitialPower ?? CurrentRuleSet.InitialPower ?? CurrentShip.InitialPower);
        RunState.Reset(initialPower, CurrentRuleSet.InitialCredits);
        if ((Configuration.InitialLives ?? CurrentRuleSet.InitialLives) is { } initialLives)
        {
            Player.Remove<LivesComponent>();
            Player.Add(new LivesComponent(Math.Min(CurrentShip.MaximumLives, initialLives)));
        }
        if ((Configuration.InitialBombs ?? CurrentRuleSet.InitialBombs) is { } initialBombs)
        {
            var bombDamage = Player.Get<BombComponent>().Damage;
            Player.Remove<BombComponent>();
            Player.Add(new BombComponent(Math.Min(CurrentShip.MaximumBombs, initialBombs), bombDamage));
        }
        var ship = Player.Get<ShipComponent>();
        ship.Power = initialPower;
        _specialGaugeSystem.Reset(
            RunState,
            Configuration.InitialGauge ?? CurrentRuleSet.InitialGauge,
            inputFrame.IsPressed(InputButtons.Special));
        _rankSystem.Reset(RunState);
        if (Configuration.InitialRank is { } initialRank) _rankSystem.SetInitial(RunState, initialRank);
        _randomSource.Reset(Configuration.Seed);
        Events.BeginTick(0);
        Status = SimulationStatus.Running;
        IsPaused = false;
        _pauseWasPressed = inputFrame.IsPressed(InputButtons.Pause);
        _retryWasPressed = inputFrame.IsPressed(InputButtons.Retry);
        _bombSystem.Reset(inputFrame.IsPressed(InputButtons.Bomb));
        Feedback = default;
        Result = null;
        StageNumber = 0;
        LastStageScore = 0;
        _legacyAccumulator = 0;
        _restartGeneration++;
        var stageId = Configuration.StartStageId;
        if (string.IsNullOrWhiteSpace(stageId))
        {
            stageId = CurrentRuleSet.StageIds[0];
        }

        StartStage(stageId);
        if (Configuration.InitialInvincibilitySeconds is { } initialInvincibility)
        {
            Player.Get<InvincibilityComponent>().Remaining = initialInvincibility;
        }
    }

    public bool TryContinue()
    {
        if (Status != SimulationStatus.GameOver ||
            !CurrentRuleSet.AllowContinue ||
            RunState.CreditsRemaining < CurrentRuleSet.ContinueCreditCost) return false;

        RunState.CreditsRemaining -= CurrentRuleSet.ContinueCreditCost;
        RunState.Continued = true;
        RunState.ContinuesUsed++;
        Telemetry.ContinuesUsed++;
        var lives = Player.Get<LivesComponent>();
        lives.Remaining = lives.Initial;
        var bombs = Player.Get<BombComponent>();
        bombs.Remaining = Math.Min(CurrentShip.MaximumBombs, CurrentShip.BombsAfterRespawn);
        var ship = Player.Get<ShipComponent>();
        var previousPower = ship.Power;
        ship.Power = Math.Min(
            CurrentShip.MaximumPower,
            CurrentRuleSet.InitialPower ?? CurrentShip.InitialPower);
        RunState.Power = ship.Power;
        if (previousPower != ship.Power)
        {
            Events.Publish((frame, sequence) => new PowerChangedEvent(
                frame, sequence, Player.Id, previousPower, ship.Power, "continue"));
        }

        var lifeCycle = Player.Get<PlayerLifeCycleComponent>();
        lifeCycle.State = PlayerLifeCycleState.Respawning;
        lifeCycle.Timer = 0;
        Projectiles.QueueRemoveAll();
        Projectiles.CommitRemovals();
        foreach (var item in World.Query<ItemComponent>().ToArray()) World.DestroyEntity(item);
        Events.Publish((frame, sequence) => new ContinueUsedEvent(
            frame, sequence, Player.Id, RunState.CreditsRemaining));
        Status = SimulationStatus.Running;
        Result = null;
        return true;
    }

    private RuleSetDefinition ResolveRuleSet()
    {
        var ruleSetId = Configuration.RuleSetId;
        if (string.IsNullOrWhiteSpace(ruleSetId)) ruleSetId = Definitions.Game.DefaultRuleSetId;
        return string.IsNullOrWhiteSpace(ruleSetId)
            ? new RuleSetDefinition
            {
                Id = "legacy",
                StageRouteId = "legacy",
                StageIds = GetLegacyStageRoute(),
                AllowContinue = false
            }
            : Definitions.GetRuleSet(ruleSetId);
    }

    private DifficultyDefinition? ResolveDifficulty()
    {
        var difficultyId = Configuration.DifficultyId;
        if (string.IsNullOrWhiteSpace(difficultyId))
        {
            difficultyId = Definitions.Game.DifficultyIds.FirstOrDefault();
        }

        return string.IsNullOrWhiteSpace(difficultyId) ? null : Definitions.GetDifficulty(difficultyId);
    }

    private void BeginResults()
    {
        Events.Publish((frame, sequence) => new StageClearedEvent(
            frame, sequence, CurrentStage.Id, StageNumber));
        Phase = StagePhase.Results;
        _phaseElapsed = 0;
        ClearStageEntities(keepEffects: true);
    }

    private void AdvanceStageOrFinish()
    {
        var routeIndex = CurrentRuleSet.StageIds.ToList().IndexOf(CurrentStage.Id);
        if (Configuration.IsPractice)
        {
            Events.Publish((frame, sequence) => new AllClearedEvent(
                frame,
                sequence,
                CurrentStage.Id,
                Player.Get<LivesComponent>().Remaining,
                Player.Get<BombComponent>().Remaining));
            Status = SimulationStatus.StageClear;
            return;
        }
        if (routeIndex + 1 < CurrentRuleSet.StageIds.Count)
        {
            StartStage(CurrentRuleSet.StageIds[routeIndex + 1]);
            return;
        }

        if (CurrentRuleSet.ClearCondition == "time-attack")
        {
            StartStage(CurrentRuleSet.StageIds[0]);
            return;
        }

        if (routeIndex >= 0)
        {
            Events.Publish((frame, sequence) => new AllClearedEvent(
                frame,
                sequence,
                CurrentStage.Id,
                Player.Get<LivesComponent>().Remaining,
                Player.Get<BombComponent>().Remaining));
            Status = SimulationStatus.StageClear;
            return;
        }

        throw new InvalidOperationException($"Stage '{CurrentStage.Id}' is not in rule set route '{CurrentRuleSet.Id}'.");
    }

    private IReadOnlyList<string> GetLegacyStageRoute()
    {
        var route = new List<string>();
        var stage = Definitions.GetStage(Definitions.Game.StageId);
        while (true)
        {
            route.Add(stage.Id);
            if (string.IsNullOrWhiteSpace(stage.NextStageId)) return route;
            stage = Definitions.GetStage(stage.NextStageId);
        }
    }

    private void StartStage(string stageId)
    {
        ClearStageEntities(keepEffects: false);
        CurrentStage = Definitions.GetStage(stageId);
        StageNumber++;
        _stageStartScore = RunState.Score;
        var stageSystemDefinition = CurrentStage;
        if (StageNumber == 1 && !string.IsNullOrWhiteSpace(Configuration.CheckpointId))
        {
            stageSystemDefinition = CurrentStage with { Events = Array.Empty<StageEventDefinition>() };
        }
        _stageSystem = new StageSystem(stageSystemDefinition, _enemyFactory, Telemetry.BossesKilled, _capabilities);
        Phase = CurrentStage.OpeningDuration > 0 ? StagePhase.Opening : StagePhase.Playing;
        _phaseElapsed = 0;
        _phaseTicks = 0;

        Player.Get<TransformComponent>().Snap(_playerStartPosition);
        Player.Get<VelocityComponent>().Value = System.Numerics.Vector2.Zero;
        Player.Remove<HitFlashComponent>();
        Player.Remove<PendingDestroyComponent>();
        if (Player.TryGet<InvincibilityComponent>(out var invincibility))
        {
            invincibility.Remaining = 0;
        }

        if (StageNumber == 1 && !string.IsNullOrWhiteSpace(Configuration.CheckpointId))
        {
            StartBossCheckpoint(Configuration.CheckpointId);
        }
    }

    private void StartBossCheckpoint(string checkpointId)
    {
        var stageBossIds = CurrentStage.Events
            .Where(static stageEvent => !string.IsNullOrWhiteSpace(stageEvent.BossId))
            .Select(static stageEvent => stageEvent.BossId!)
            .Concat(CurrentStage.Objectives
                .Where(static objective => !string.IsNullOrWhiteSpace(objective.BossId))
                .Select(static objective => objective.BossId!))
            .ToHashSet(StringComparer.Ordinal);
        var bossDefinition = Definitions.Bosses.Values.FirstOrDefault(candidate =>
            stageBossIds.Contains(candidate.Id) &&
            candidate.Phases.Any(phase => phase.CheckpointId == checkpointId));
        if (bossDefinition is null)
            throw new DefinitionValidationException(
                $"Stage '{CurrentStage.Id}' has no boss checkpoint '{checkpointId}'.");
        var stageEvent = CurrentStage.Events.FirstOrDefault(candidate => candidate.BossId == bossDefinition.Id);
        var bossEntity = _enemyFactory.Create(
            World,
            Definitions.GetEnemy(stageEvent?.EnemyId ?? bossDefinition.EnemyId),
            stageEvent is null
                ? new System.Numerics.Vector2(Definitions.Game.Width / 2f, 80)
                : new System.Numerics.Vector2(stageEvent.X, stageEvent.Y),
            isBoss: true,
            bossDefinition);
        Telemetry.EnemiesSpawned++;
        _bossPhaseSystem.BeginAtCheckpoint(
            bossEntity,
            bossDefinition,
            checkpointId,
            Projectiles,
            Telemetry,
            Events);
    }

    private void ApplyScores(int eventStartIndex)
    {
        var inputs = Events.Events.Skip(eventStartIndex).ToArray();
        _scorePipeline.Apply(inputs, RunState, Events);
        Telemetry.Score = RunState.Score;
    }

    private void ClearStageEntities(bool keepEffects)
    {
        Projectiles.QueueRemoveAll();
        Projectiles.CommitRemovals();
        foreach (var entity in World.Entities.ToArray())
        {
            if (entity.Has<PlayerComponent>() || entity.Has<OptionUnitComponent>() ||
                (keepEffects && entity.Has<ExplosionComponent>()))
            {
                continue;
            }

            World.DestroyEntity(entity);
        }
    }

    private sealed class TickInputState : IInputState
    {
        private InputFrame _frame;

        public float MoveX => _frame.NormalizedMoveX;
        public float MoveY => _frame.NormalizedMoveY;
        public bool Fire => _frame.IsPressed(InputButtons.Fire);
        public bool Focus => _frame.IsPressed(InputButtons.Focus);
        public bool Special => _frame.IsPressed(InputButtons.Special);
        public bool Bomb => _frame.IsPressed(InputButtons.Bomb);
        public bool Retry => _frame.IsPressed(InputButtons.Retry);
        public bool Pause => _frame.IsPressed(InputButtons.Pause);

        public void Apply(InputFrame frame) => _frame = frame;
    }
}
