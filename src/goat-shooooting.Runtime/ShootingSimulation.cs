using GoatShooooting.Core;
using GoatShooooting.Definitions;

namespace GoatShooooting.Runtime;

public enum SimulationStatus
{
    Running,
    StageClear,
    GameOver,
    TimeExpired,
    ContentError
}

public enum StagePhase
{
    Opening,
    Playing,
    Results
}

/// <summary>Headless-capable composition root for the production game simulation.</summary>
public sealed partial class ShootingSimulation
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
    private readonly ProjectileProgramSystem _projectileProgramSystem = new();
    private readonly ProjectileLifetimeSystem _projectileLifetimeSystem = new();
    private readonly ActorSpatialGrid _actorSpatialGrid = new();
    private readonly ProjectileCollisionSystem _projectileCollisionSystem = new();
    private readonly OptionFollowSystem _optionFollowSystem = new();
    private readonly ActorTransformSystem _actorTransformSystem = new();
    private readonly ActorAnimationStateSystem _actorAnimationStateSystem = new();
    private readonly LaserSystem _laserSystem = new();
    private readonly InteractionSystem _interactionSystem = new();
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
    private readonly FixedWorldClock _worldClock = new();
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
    private EventRuleReducer _eventRuleReducer = null!;
    private IReadOnlyList<StageHandle> _stageRoute = Array.Empty<StageHandle>();
    private StageHandle _currentStageHandle;
    private ItemHandle? _specialCancelItemHandle;
    private IReadOnlyList<IGameplayEvent> _previousTickFacts = Array.Empty<IGameplayEvent>();
    private bool _needsPreviousTickFacts;
    private readonly List<string> _pendingExternalSignals = new();

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
        CompiledDefinitions = new DefinitionCompiler().Compile(_definitionRepository.Load(), _capabilities);
        Definitions = CompiledDefinitions.Source;
        World = null!;
        Player = null!;
        Telemetry = null!;
        RunState = new RunState();
        Events = new GameEventBuffer();
        _weaponSystem.Advanced.Events = Events;
        Restart();
    }

    public World World { get; private set; }
    public CompiledCatalog CompiledDefinitions { get; private set; }
    /// <summary>Compatibility view used by v1/v2 integrations and presentation adapters.</summary>
    public DefinitionCatalog Definitions { get; private set; }
    public string CompiledContentHash => CompiledDefinitions.ContentHash;
    public Entity Player { get; private set; }
    public ShipDefinition CurrentShip { get; private set; } = null!;
    public RuleSetDefinition CurrentRuleSet { get; private set; } = null!;
    public DifficultyDefinition? CurrentDifficulty { get; private set; }
    public VariantDefinition? CurrentVariant { get; private set; }
    public VariantHandle? CurrentVariantHandle { get; private set; }
    public ProjectileStore Projectiles { get; private set; } = null!;
    public SimulationTelemetry Telemetry { get; private set; }
    public RunConfiguration Configuration { get; }
    public RunState RunState { get; }
    public GameEventBuffer Events { get; }
    public ScopedResourceStore Resources { get; private set; } = null!;
    public StateMachineSystem StateMachines { get; private set; } = null!;
    public double Elapsed => _stageSystem.Elapsed;
    public StageDefinition CurrentStage { get; private set; } = null!;
    public int StageNumber { get; private set; }
    public StagePhase Phase { get; private set; }
    public double PhaseElapsed => _phaseElapsed;
    public long StageScore => RunState.Score - _stageStartScore;
    public long LastStageScore { get; private set; }
    public FixedWorldClock WorldClock => _worldClock;
    public StagePresentationState StagePresentation => _stageSystem.Presentation;
    public SimulationStatus Status { get; private set; }
    public bool IsPaused { get; private set; }
    public SimulationFeedback Feedback { get; private set; }
    public int DefinitionReloadCount { get; private set; }
    public string? DefinitionReloadError { get; private set; }
    public string? DefinitionContentError { get; private set; }
    public RunResult? Result { get; private set; }
    public RunDebugSnapshot DebugSnapshot => new(
        RunState.Frame,
        RunState.Rank,
        RunState.Gauge,
        RunState.SpecialPhase,
        RunState.SpecialLevel,
        RunState.SpecialTimeRemaining,
        RunState.SpecialCooldownRemaining)
    {
        ModifierProvenance = _modifierState.Provenance
    };
    public RunMetadataSnapshot ReplayMetadata => new(
        Configuration,
        RunState.Frame,
        RunState.Rank,
        RunState.Gauge,
        RunState.SpecialPhase,
        RunState.SpecialLevel);
    internal ulong RandomState => _randomSource.State;
    internal IReadOnlyCollection<string> CompletedBossIds => _stageSystem.CompletedBossIds;
    internal bool HasStageProgram => CompiledDefinitions.Get(_currentStageHandle).Program is not null;
    public StageProgramRuntimeSnapshot? StageProgramSnapshot =>
        _stageSystem.CaptureProgramSnapshot();

    /// <summary>Advances exactly one 60Hz simulation tick using an immutable input sample.</summary>
    public void Tick(InputFrame inputFrame) =>
        ProcessStep(inputFrame, SimulationTiming.TickDurationSeconds, advanceFrame: true, fixedTick: true);

    /// <summary>Queues a deterministic semantic signal for the next production tick.</summary>
    public void InjectSignal(string signalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalId);
        _pendingExternalSignals.Add(signalId);
    }

    public void SetWorldTimeScale(float scale) => _worldClock.SetScale(scale);

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

    public FrameSnapshot CaptureFrame(RenderSystem renderSystem)
    {
        ArgumentNullException.ThrowIfNull(renderSystem);
        var items = renderSystem.Capture(World, Projectiles);
        var bossItem = items.FirstOrDefault(static item => item.BossName is not null);
        var boss = bossItem.BossName is null
            ? null
            : new BossHudSnapshot(
                bossItem.EntityId,
                bossItem.BossDefinitionId ?? string.Empty,
                bossItem.BossName,
                bossItem.BossPhaseName ?? string.Empty,
                bossItem.HealthFraction,
                bossItem.BossRemainingTime ?? 0,
                bossItem.BossWarning);
        var ship = Player.Get<ShipComponent>();
        return new FrameSnapshot(
            RunState.Frame,
            items,
            RunState.Score,
            RunState.Chain,
            RunState.Multiplier,
            ship.Power,
            ship.MaximumPower,
            RunState.Gauge,
            CurrentRuleSet.MaximumGauge,
            RunState.Rank,
            StageNumber,
            CurrentStage.Id,
            Player.Get<LivesComponent>().Remaining,
            Player.Get<BombComponent>().Remaining,
            boss);
    }

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
        foreach (var signalId in _pendingExternalSignals)
            Events.Publish((frame, sequence) => new RuleSignalEvent(
                frame, sequence, signalId, "external", PresentationOnly: false));
        _pendingExternalSignals.Clear();
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
        if (_needsPreviousTickFacts)
        {
            Resources.SynchronizeFromLegacy(RunState, Player);
            var preInputCommands = _eventRuleReducer.Reduce(
                RulePhase.PreInput, _previousTickFacts, Resources, Player.Id);
            var deferredPreInput = RuleCommandExecutor.ApplyCore(
                preInputCommands, Resources, RunState, Events, StateMachines, CompiledDefinitions, Player.Id);
            ApplyDeferredRuleCommands(deferredPreInput);
            var stateCommands = StateMachines.Update(
                RunState.Frame, inputFrame, _previousTickFacts, Resources, Player.Id);
            var deferredState = RuleCommandExecutor.ApplyCore(
                stateCommands, Resources, RunState, Events, StateMachines, CompiledDefinitions, Player.Id);
            ApplyDeferredRuleCommands(deferredState);
            Resources.SynchronizeToLegacy(RunState, Player);
            _previousTickFacts = Array.Empty<IGameplayEvent>();
        }

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

        if (fixedTick) _worldClock.Advance();
        var worldDeltaTime = fixedTick
            ? _worldClock.DeltaSeconds
            : deltaTime * (_worldClock.ScaleQ16 / (float)FixedWorldClock.One);
        _playerLifeCycleSystem.Advance(World, worldDeltaTime, Telemetry, Events);
        if (Player.Get<PlayerLifeCycleComponent>().State == PlayerLifeCycleState.GameOverPending)
        {
            Status = SimulationStatus.GameOver;
            CompleteStep(advanceFrame);
            return;
        }

        if (CurrentRuleSet.StateMachineIds.Count == 0)
        {
            _specialGaugeSystem.BeginTick(
                inputFrame,
                deltaTime,
                RunState,
                Player,
                Projectiles,
                Telemetry,
                Events);
        }

        if (fixedTick)
        {
            _stageSystem.Tick(
                World, CompiledDefinitions, Telemetry, _worldClock.Frame, _worldClock, Events);
        }
        else
        {
            _stageSystem.Update(World, CompiledDefinitions, deltaTime, Telemetry);
        }
        _bossPhaseSystem.BeginAndAdvance(
            World,
            CompiledDefinitions,
            deltaTime,
            worldDeltaTime,
            Projectiles,
            Telemetry,
            Events);
        _playerInputSystem.Update(World, _tickInput);
        _motionTimelineSystem.Update(
            World,
            CompiledDefinitions,
            CurrentDifficulty?.Id,
            worldDeltaTime,
            Telemetry,
            CurrentDifficulty?.PatternTags);
        _attackTimelineSystem.Update(
            World,
            CompiledDefinitions,
            CurrentDifficulty?.Id,
            worldDeltaTime,
            Telemetry,
            Projectiles,
            CurrentDifficulty?.PatternTags);
        _weaponSystem.Update(World, CompiledDefinitions, _tickInput, worldDeltaTime, Telemetry, Projectiles);
        Projectiles.CommitSpawns(Events);
        try
        {
            _projectileProgramSystem.Update(Projectiles, World, _worldClock.Frame);
        }
        catch (DefinitionValidationException exception)
        {
            DefinitionContentError = exception.Message;
            Status = SimulationStatus.ContentError;
            return;
        }
        _projectileMovementSystem.Update(
            Projectiles,
            World,
            worldDeltaTime,
            Definitions.Game.Width,
            Definitions.Game.Height,
            Telemetry);
        _movementSystem.Update(World, worldDeltaTime, Telemetry);
        _movementPatternSystem.Update(World, worldDeltaTime);
        _playerBoundsSystem.Update(World, Definitions.Game.Width, Definitions.Game.Height);
        _outOfBoundsSystem.Update(World, Definitions.Game.Width, Definitions.Game.Height);
        _optionFollowSystem.Update(World, worldDeltaTime);
        _actorTransformSystem.Update(World);
        _invincibilitySystem.Update(World, worldDeltaTime);
        _actorSpatialGrid.Rebuild(World);
        var bombDamage = _bombSystem.Update(
            World,
            Projectiles,
            _tickInput,
            Math.Min(Definitions.Game.Width, Definitions.Game.Height) * 0.4f,
            Telemetry,
            Events,
            CurrentRuleSet.ManualBombCost,
            CurrentRuleSet.BombInvincibilitySeconds,
            Resources,
            string.IsNullOrWhiteSpace(CurrentRuleSet.BombResourceId)
                ? null : CompiledDefinitions.ResolveResource(CurrentRuleSet.BombResourceId));
        _damageSystem.Update(bombDamage, Telemetry, Events, World);
        var laserDamage = _laserSystem.Update(World, Projectiles, worldDeltaTime, Telemetry, Events);
        try
        {
            _interactionSystem.Update(World, Projectiles, CompiledDefinitions, Telemetry, Events);
        }
        catch (DefinitionValidationException exception)
        {
            DefinitionContentError = exception.Message;
            Status = SimulationStatus.ContentError;
            return;
        }
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
            Events,
            Resources,
            string.IsNullOrWhiteSpace(CurrentRuleSet.BombResourceId)
                ? null : CompiledDefinitions.ResolveResource(CurrentRuleSet.BombResourceId));
        _damageSystem.Update(allDamage, Telemetry, Events, World);
        _damageSystem.Update(autoBombDamage, Telemetry, Events, World);
        _bossPhaseSystem.Resolve(
            World,
            CompiledDefinitions,
            Projectiles,
            _randomSource,
            Telemetry,
            Events);
        _stageSystem.ObserveEvents(Events.Events);
        _itemDropSystem.SpawnDrops(World, CompiledDefinitions, Events.Events, _randomSource, Telemetry, Events);
        _itemSystem.Update(
            World,
            CurrentRuleSet,
            worldDeltaTime,
            Definitions.Game.Height,
            RunState,
            Telemetry,
            Events);
        if (CurrentRuleSet.StateMachineIds.Count == 0)
        {
            _specialGaugeSystem.Observe(
                Events.Events,
                RunState,
                Player.Id,
                Projectiles,
                Telemetry,
                Events);
        }
        _itemDropSystem.SpawnProjectileCancelDrops(
            World,
            CompiledDefinitions,
            Events.Events,
            _specialCancelItemHandle,
            Telemetry,
            Events);
        _rankSystem.Observe(
            Events.Events,
            worldDeltaTime,
            RunState,
            World,
            CompiledDefinitions,
            Projectiles,
            Telemetry,
            Events);
        Projectiles.CommitSpawns(Events);
        ApplyScores(0);
        _extendSystem.Update(Player, CurrentRuleSet, RunState, Telemetry, Events);
        _projectileLifetimeSystem.Update(Projectiles);
        _feedbackSystem.Update(World, worldDeltaTime);
        _actorAnimationStateSystem.Update(World);
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
        _previousTickFacts = _needsPreviousTickFacts ? Events.Events.ToArray() : Array.Empty<IGameplayEvent>();
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
            var compiled = new DefinitionCompiler().Compile(result.Catalog!, _capabilities);
            CompiledDefinitions = compiled;
        }
        catch (DefinitionValidationException exception)
        {
            DefinitionReloadError = exception.Message;
            return false;
        }

        Definitions = CompiledDefinitions.Source;
        DefinitionReloadError = null;
        DefinitionReloadCount++;
        Restart(inputFrame);
        return true;
    }

    private void Restart(InputFrame inputFrame)
    {
        World = new World();
        _worldClock.Reset();
        DefinitionContentError = null;
        Projectiles = new ProjectileStore();
        var shipId = Configuration.ShipId ??
            (!string.IsNullOrWhiteSpace(Definitions.Game.PlayerId)
                ? Definitions.Game.PlayerId
                : Definitions.Game.ShipIds[0]);
        var shipHandle = CompiledDefinitions.ResolveShip(shipId);
        CurrentShip = CompiledDefinitions.Get(shipHandle);
        CurrentRuleSet = ResolveRuleSet();
        CurrentVariantHandle = ResolveVariant();
        CurrentVariant = CurrentVariantHandle is { } variantHandle
            ? CompiledDefinitions.Get(variantHandle).Definition
            : null;
        Resources = new ScopedResourceStore(CompiledDefinitions.GetResources(CurrentRuleSet));
        _eventRuleReducer = new EventRuleReducer(
            CompiledDefinitions.GetEventRules(CurrentRuleSet, CurrentVariantHandle));
        StateMachines = new StateMachineSystem(CompiledDefinitions.GetStateMachines(CurrentRuleSet));
        _needsPreviousTickFacts = _eventRuleReducer.HasPreInputRules || !StateMachines.IsEmpty;
        _modifierState.SetStateModifierProvider(() => StateMachines.ActiveModifiers(Player?.Id ?? 0));
        _stageRoute = CurrentRuleSet.StageIds.Select(CompiledDefinitions.ResolveStage).ToArray();
        _scorePipeline = ScoreRulePipeline.Create(CurrentRuleSet, _capabilities);
        CurrentDifficulty = ResolveDifficulty();
        Projectiles.ConfigurePrograms(CompiledDefinitions, CurrentVariantHandle, Configuration.Seed, StageNumber);
        _specialGaugeSystem = SpecialGaugeSystem.Create(CurrentRuleSet, _capabilities);
        _specialCancelItemHandle = string.IsNullOrWhiteSpace(_specialGaugeSystem.Rule?.CancelItemId)
            ? null
            : CompiledDefinitions.ResolveItem(_specialGaugeSystem.Rule.CancelItemId);
        _rankSystem = RankSystem.Create(CurrentRuleSet, _capabilities, CompiledDefinitions);
        _runRuleSystem = new RunRuleSystem(CurrentRuleSet);
        var difficultyModifiers = CurrentDifficulty is null
            ? null
            : CompiledDefinitions.GetDifficultyModifiers(CompiledDefinitions.ResolveDifficulty(CurrentDifficulty.Id));
        _modifierState.Configure(
            CurrentDifficulty, difficultyModifiers, _rankSystem.Rule, _specialGaugeSystem.Rule, RunState);
        var playerFactory = new PlayerFactory();
        if (Definitions.Players.TryGetValue(shipId, out var legacyPlayer))
        {
            _playerStartPosition = new System.Numerics.Vector2(legacyPlayer.X, legacyPlayer.Y);
            Player = playerFactory.Create(
                World,
                CurrentShip,
                new System.Numerics.Vector2(legacyPlayer.X, legacyPlayer.Y),
                legacyPlayer.BombDamage,
                legacyPlayer.InvincibilitySeconds,
                CompiledDefinitions);
        }
        else
        {
            _playerStartPosition = new System.Numerics.Vector2(
                Definitions.Game.Width / 2f,
                Definitions.Game.Height - Math.Max(48, CurrentShip.HitRadius * 4));
            Player = playerFactory.Create(
                World,
                CurrentShip,
                _playerStartPosition,
                compiledDefinitions: CompiledDefinitions);
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
        Resources.SynchronizeFromLegacy(RunState, Player);
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
        _previousTickFacts = Array.Empty<IGameplayEvent>();
        _restartGeneration++;
        var stageId = Configuration.StartStageId;
        if (string.IsNullOrWhiteSpace(stageId))
        {
            stageId = CurrentRuleSet.StageIds[0];
        }

        StartStage(CompiledDefinitions.ResolveStage(stageId));
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

    private VariantHandle? ResolveVariant()
    {
        var variantId = Configuration.VariantId ?? Definitions.Game.DefaultVariantId;
        return string.IsNullOrWhiteSpace(variantId) ? null : CompiledDefinitions.ResolveVariant(variantId);
    }

    public ResolvedProgramBinding ResolveProgramBinding(SemanticProgramSlotDefinition slot) =>
        CompiledDefinitions.ResolveProgramBinding(slot, CurrentVariantHandle);

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
        var routeIndex = _stageRoute.ToList().IndexOf(_currentStageHandle);
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
            StartStage(_stageRoute[routeIndex + 1]);
            return;
        }

        if (CurrentRuleSet.ClearCondition == "time-attack")
        {
            StartStage(_stageRoute[0]);
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

    private void StartStage(StageHandle stageHandle)
    {
        ClearStageEntities(keepEffects: false);
        Resources.Reset(ResourceResetPolicy.OnStageStart);
        StateMachines.Reset(ResourceScope.Stage, RunState.Frame);
        _currentStageHandle = stageHandle;
        var compiledStage = CompiledDefinitions.Get(stageHandle);
        CurrentStage = compiledStage.Definition;
        StageNumber++;
        Projectiles.SetProgramStageInstance(StageNumber);
        _stageStartScore = RunState.Score;
        var stageSystemDefinition = CurrentStage;
        if (StageNumber == 1 && !string.IsNullOrWhiteSpace(Configuration.CheckpointId))
        {
            stageSystemDefinition = CurrentStage with { Events = Array.Empty<StageEventDefinition>() };
        }
        var stageForRuntime = ReferenceEquals(stageSystemDefinition, CurrentStage)
            ? compiledStage
            : new CompiledStageDefinition(
                compiledStage.Handle,
                stageSystemDefinition,
                Array.Empty<CompiledStageEventDefinition>());
        _stageSystem = new StageSystem(stageForRuntime, _enemyFactory, Telemetry.BossesKilled);
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
        var compiledStage = CompiledDefinitions.Get(_currentStageHandle);
        var programBossEvents = compiledStage.Program?.Tracks
            .SelectMany(static track => track.Events)
            .Where(static stageEvent => stageEvent.BossHandle is not null)
            .ToArray() ?? Array.Empty<CompiledStageProgramEvent>();
        var stageBossIds = CurrentStage.Events
            .Where(static stageEvent => !string.IsNullOrWhiteSpace(stageEvent.BossId))
            .Select(static stageEvent => stageEvent.BossId!)
            .Concat(CurrentStage.Objectives
                .Where(static objective => !string.IsNullOrWhiteSpace(objective.BossId))
                .Select(static objective => objective.BossId!))
            .Concat(programBossEvents.Select(stageEvent =>
                CompiledDefinitions.Get(stageEvent.BossHandle!.Value).Id))
            .ToHashSet(StringComparer.Ordinal);
        var bossDefinition = Definitions.Bosses.Values.FirstOrDefault(candidate =>
            stageBossIds.Contains(candidate.Id) &&
            candidate.Phases.Any(phase => phase.CheckpointId == checkpointId));
        if (bossDefinition is null)
            throw new DefinitionValidationException(
                $"Stage '{CurrentStage.Id}' has no boss checkpoint '{checkpointId}'.");
        var stageEvent = CurrentStage.Events.FirstOrDefault(candidate => candidate.BossId == bossDefinition.Id);
        var bossHandle = CompiledDefinitions.ResolveBoss(bossDefinition.Id);
        var programBossEvent = programBossEvents.FirstOrDefault(candidate => candidate.BossHandle == bossHandle);
        var bossEntity = _enemyFactory.Create(
            World,
            CompiledDefinitions,
            CompiledDefinitions.ResolveEnemy(stageEvent?.EnemyId ?? bossDefinition.EnemyId),
            stageEvent is not null
                ? new System.Numerics.Vector2(stageEvent.X, stageEvent.Y)
                : programBossEvent is not null
                    ? new System.Numerics.Vector2(programBossEvent.X, programBossEvent.Y)
                    : new System.Numerics.Vector2(Definitions.Game.Width / 2f, 80),
            isBoss: true,
            bossHandle);
        Telemetry.EnemiesSpawned++;
        _bossPhaseSystem.BeginAtCheckpoint(
            World,
            bossEntity,
            CompiledDefinitions,
            bossHandle,
            checkpointId,
            Projectiles,
            Telemetry,
            Events);
    }

    private void ApplyScores(int eventStartIndex)
    {
        var inputs = Events.Events.Skip(eventStartIndex).ToArray();
        if (inputs.Any(static value => value is BossPhaseStartedEvent))
        {
            Resources.Reset(ResourceResetPolicy.OnBossPhase);
            StateMachines.Reset(ResourceScope.BossPhase, RunState.Frame);
        }
        if (!_eventRuleReducer.IsEmpty)
        {
            Resources.SynchronizeFromLegacy(RunState, Player);
            var commands = _eventRuleReducer.Reduce(RulePhase.PostInteraction, inputs, Resources, Player.Id);
            var deferred = RuleCommandExecutor.ApplyCore(
                commands, Resources, RunState, Events, StateMachines, CompiledDefinitions, Player.Id,
                _modifierState.ScoreMultiplier);
            ApplyDeferredRuleCommands(deferred);
            Resources.SynchronizeToLegacy(RunState, Player);
        }
        else
        {
            _scorePipeline.Apply(inputs, RunState, Events);
            Resources.SynchronizeFromLegacy(RunState, Player);
        }
        Telemetry.Score = RunState.Score;
    }

    private void ApplyDeferredRuleCommands(IReadOnlyList<RuleCommand> commands)
    {
        foreach (var command in commands)
        {
            var playerPosition = Player.Get<TransformComponent>().Position;
            var position = new System.Numerics.Vector2(
                command.Value == 0 && !command.Arguments.ContainsKey("x") ? playerPosition.X : checked((float)command.Value),
                command.Minimum == double.MinValue ? playerPosition.Y : checked((float)command.Minimum));
            switch (command.Kind)
            {
                case RuleCommandKind.SpawnItem:
                    _ = new ItemFactory().Create(
                        World,
                        CompiledDefinitions.Get(CompiledDefinitions.ResolveItem(command.TargetId!)),
                        position,
                        System.Numerics.Vector2.Zero,
                        Telemetry,
                        Events);
                    break;
                case RuleCommandKind.SpawnActor:
                    _ = _enemyFactory.CreateActor(
                        World,
                        CompiledDefinitions,
                        CompiledDefinitions.ResolveActor(command.TargetId!),
                        position);
                    Telemetry.EnemiesSpawned++;
                    break;
                case RuleCommandKind.SpawnProjectile:
                    var angle = command.Maximum == double.MaxValue ? -90 : command.Maximum;
                    var radians = checked((float)(angle * (Math.PI / 180)));
                    var direction = new System.Numerics.Vector2(MathF.Cos(radians), MathF.Sin(radians));
                    var team = ReadString(command, "team") == "enemy"
                        ? CollisionLayer.Enemy : CollisionLayer.Player;
                    _ = new BulletFactory(_capabilities).Create(
                        Projectiles,
                        CompiledDefinitions.Get(CompiledDefinitions.ResolveProjectile(command.TargetId!)),
                        position,
                        direction,
                        team,
                        Player.Id);
                    break;
                case RuleCommandKind.CancelProjectiles:
                    ApplyProjectileQuery(command, convert: false);
                    break;
                case RuleCommandKind.ConvertProjectiles:
                    ApplyProjectileQuery(command, convert: true);
                    break;
            }
        }
        Projectiles.CommitSpawns(Events);
    }

    private void ApplyProjectileQuery(RuleCommand command, bool convert)
    {
        var team = ReadString(command, "team");
        var requiredTags = ReadStrings(command, "requiredTags");
        var requiredMask = CompiledDefinitions.Tags.Mask(requiredTags);
        var replacement = convert ? CompiledDefinitions.ResolveProjectile(command.TargetId!) : default;
        for (var index = 0; index < Projectiles.ActiveCount; index++)
        {
            if (Projectiles.IsPendingRemovalAt(index) ||
                team == "player" && Projectiles.TeamAt(index) != ProjectileTeam.Player ||
                team == "enemy" && Projectiles.TeamAt(index) != ProjectileTeam.Enemy ||
                (Projectiles.TagMaskAt(index) & requiredMask) != requiredMask) continue;
            if (convert)
            {
                Projectiles.TransformAt(index, replacement);
            }
            else
            {
                var projectileId = Projectiles.IdAt(index);
                Projectiles.QueueRemoveAt(index);
                if (Projectiles.TeamAt(index) == ProjectileTeam.Enemy) Telemetry.EnemyBulletsCleared++;
                Events.Publish((frame, sequence) => new ProjectileCancelledEvent(
                    frame, sequence, projectileId, Source: command.SourceDefinitionId));
            }
        }
    }

    private static string? ReadString(RuleCommand command, string name) =>
        command.Arguments.TryGetValue(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString() : null;

    private static IReadOnlyList<string> ReadStrings(RuleCommand command, string name) =>
        command.Arguments.TryGetValue(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.Array
            ? value.EnumerateArray().Select(static item => item.GetString()!).ToArray()
            : Array.Empty<string>();

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
