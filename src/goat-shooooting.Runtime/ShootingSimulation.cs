using GoatShooooting.Core;
using GoatShooooting.Definitions;

namespace GoatShooooting.Runtime;

public enum SimulationStatus
{
    Running,
    StageClear,
    GameOver
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
    private readonly PlayerInputSystem _playerInputSystem = new();
    private readonly WeaponSystem _weaponSystem = new(new BulletFactory());
    private readonly HomingMovementSystem _homingMovementSystem = new();
    private readonly MovementSystem _movementSystem = new();
    private readonly MovementPatternSystem _movementPatternSystem = new();
    private readonly PlayerBoundsSystem _playerBoundsSystem = new();
    private readonly OutOfBoundsSystem _outOfBoundsSystem = new();
    private readonly CollisionSystem _collisionSystem = new();
    private readonly BulletHitSystem _bulletHitSystem = new();
    private readonly BombSystem _bombSystem = new();
    private readonly DamageSystem _damageSystem = new();
    private readonly InvincibilitySystem _invincibilitySystem = new();
    private readonly FeedbackSystem _feedbackSystem = new();
    private readonly LifetimeSystem _lifetimeSystem = new();
    private readonly CleanupSystem _cleanupSystem = new();
    private StageSystem _stageSystem = null!;
    private bool _pauseWasPressed;
    private double _phaseElapsed;
    private long _phaseTicks;
    private double _legacyAccumulator;
    private int _stageStartScore;
    private int _restartGeneration;

    public ShootingSimulation(IDefinitionRepository definitionRepository, IInputState input)
        : this(definitionRepository, input, new RunConfiguration("legacy", seed: 0))
    {
    }

    public ShootingSimulation(
        IDefinitionRepository definitionRepository,
        IInputState input,
        RunConfiguration configuration,
        IRandomSource? randomSource = null)
    {
        ArgumentNullException.ThrowIfNull(definitionRepository);
        ArgumentNullException.ThrowIfNull(configuration);
        _definitionRepository = definitionRepository;
        _legacyInput = input ?? throw new ArgumentNullException(nameof(input));
        Configuration = configuration;
        _randomSource = randomSource ?? new SeededRandomSource(configuration.Seed);
        Definitions = _definitionRepository.Load();
        World = null!;
        Player = null!;
        Telemetry = null!;
        RunState = new RunState();
        Events = new GameEventBuffer();
        Restart();
    }

    public World World { get; private set; }
    public DefinitionCatalog Definitions { get; private set; }
    public Entity Player { get; private set; }
    public SimulationTelemetry Telemetry { get; private set; }
    public RunConfiguration Configuration { get; }
    public RunState RunState { get; }
    public GameEventBuffer Events { get; }
    public double Elapsed => _stageSystem.Elapsed;
    public StageDefinition CurrentStage { get; private set; } = null!;
    public int StageNumber { get; private set; }
    public StagePhase Phase { get; private set; }
    public double PhaseElapsed => _phaseElapsed;
    public int StageScore => Telemetry.Score - _stageStartScore;
    public int LastStageScore { get; private set; }
    public SimulationStatus Status { get; private set; }
    public bool IsPaused { get; private set; }
    public SimulationFeedback Feedback { get; private set; }
    public int DefinitionReloadCount { get; private set; }
    public string? DefinitionReloadError { get; private set; }
    internal ulong RandomState => _randomSource.State;

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

        if (Status != SimulationStatus.Running)
        {
            if (inputFrame.IsPressed(InputButtons.Retry))
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
                AdvanceStageOrFinish();
            }

            CompleteStep(advanceFrame);
            return;
        }

        if (fixedTick)
        {
            _stageSystem.Tick(World, Definitions, Telemetry);
        }
        else
        {
            _stageSystem.Update(World, Definitions, deltaTime, Telemetry);
        }
        _playerInputSystem.Update(World, _tickInput);
        _weaponSystem.Update(World, Definitions, _tickInput, deltaTime, Telemetry);
        _homingMovementSystem.Update(World, deltaTime);
        _movementSystem.Update(World, deltaTime, Telemetry);
        _movementPatternSystem.Update(World, deltaTime);
        _playerBoundsSystem.Update(World, Definitions.Game.Width, Definitions.Game.Height);
        _outOfBoundsSystem.Update(World, Definitions.Game.Width, Definitions.Game.Height);
        _invincibilitySystem.Update(World, deltaTime);
        var bombDamage = _bombSystem.Update(
            World,
            _tickInput,
            Math.Min(Definitions.Game.Width, Definitions.Game.Height) * 0.4f,
            Telemetry,
            Events);
        _damageSystem.Update(bombDamage, Telemetry, Events);
        var collisions = _collisionSystem.Detect(World);
        var damageEvents = _bulletHitSystem.Update(collisions, Telemetry);
        _damageSystem.Update(damageEvents, Telemetry, Events);
        _lifetimeSystem.Update(World, deltaTime);
        _feedbackSystem.Update(World, deltaTime);
        _cleanupSystem.Update(World);

        if (!World.Query<PlayerComponent>().Any())
        {
            Status = SimulationStatus.GameOver;
        }
        else if (_stageSystem.IsCleared(World, Telemetry))
        {
            BeginResults();
        }

        CompleteStep(advanceFrame);
    }

    private void CompleteStep(bool advanceFrame)
    {
        RunState.Score = Telemetry.Score;
        Feedback = SimulationFeedback.FromEvents(Events.Events);
        if (advanceFrame)
        {
            RunState.Frame++;
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

        Definitions = result.Catalog!;
        DefinitionReloadError = null;
        DefinitionReloadCount++;
        Restart(inputFrame);
        return true;
    }

    private void Restart(InputFrame inputFrame)
    {
        World = new World();
        Player = new PlayerFactory().Create(World, Definitions.GetPlayer(Definitions.Game.PlayerId));
        Telemetry = new SimulationTelemetry();
        RunState.Reset();
        _randomSource.Reset(Configuration.Seed);
        Events.BeginTick(0);
        Status = SimulationStatus.Running;
        IsPaused = false;
        _pauseWasPressed = inputFrame.IsPressed(InputButtons.Pause);
        _bombSystem.Reset(inputFrame.IsPressed(InputButtons.Bomb));
        Feedback = default;
        StageNumber = 0;
        LastStageScore = 0;
        _legacyAccumulator = 0;
        _restartGeneration++;
        StartStage(Definitions.Game.StageId);
    }

    private void BeginResults()
    {
        LastStageScore = StageScore;
        Phase = StagePhase.Results;
        _phaseElapsed = 0;
        ClearStageEntities(keepEffects: true);
        if (CurrentStage.ResultsDuration <= 0)
        {
            AdvanceStageOrFinish();
        }
    }

    private void AdvanceStageOrFinish()
    {
        if (string.IsNullOrWhiteSpace(CurrentStage.NextStageId))
        {
            Status = SimulationStatus.StageClear;
            return;
        }

        StartStage(CurrentStage.NextStageId);
    }

    private void StartStage(string stageId)
    {
        ClearStageEntities(keepEffects: false);
        CurrentStage = Definitions.GetStage(stageId);
        StageNumber++;
        _stageStartScore = Telemetry.Score;
        _stageSystem = new StageSystem(CurrentStage, new EnemyFactory(), Telemetry.BossesKilled);
        Phase = CurrentStage.OpeningDuration > 0 ? StagePhase.Opening : StagePhase.Playing;
        _phaseElapsed = 0;
        _phaseTicks = 0;

        var playerDefinition = Definitions.GetPlayer(Definitions.Game.PlayerId);
        Player.Get<TransformComponent>().Position = new System.Numerics.Vector2(playerDefinition.X, playerDefinition.Y);
        Player.Get<VelocityComponent>().Value = System.Numerics.Vector2.Zero;
        Player.Remove<HitFlashComponent>();
        Player.Remove<PendingDestroyComponent>();
        if (Player.TryGet<InvincibilityComponent>(out var invincibility))
        {
            invincibility.Remaining = 0;
        }
    }

    private void ClearStageEntities(bool keepEffects)
    {
        foreach (var entity in World.Entities.ToArray())
        {
            if (entity.Has<PlayerComponent>() || (keepEffects && entity.Has<ExplosionComponent>()))
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
        public bool Bomb => _frame.IsPressed(InputButtons.Bomb);
        public bool Retry => _frame.IsPressed(InputButtons.Retry);
        public bool Pause => _frame.IsPressed(InputButtons.Pause);

        public void Apply(InputFrame frame) => _frame = frame;
    }
}
