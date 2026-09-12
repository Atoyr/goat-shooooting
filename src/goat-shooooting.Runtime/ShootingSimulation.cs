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
    private readonly IInputState _input;
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
    private int _stageStartScore;

    public ShootingSimulation(IDefinitionRepository definitionRepository, IInputState input)
    {
        ArgumentNullException.ThrowIfNull(definitionRepository);
        _definitionRepository = definitionRepository;
        _input = input ?? throw new ArgumentNullException(nameof(input));
        Definitions = _definitionRepository.Load();
        World = null!;
        Player = null!;
        Telemetry = null!;
        Restart();
    }

    public World World { get; private set; }
    public DefinitionCatalog Definitions { get; private set; }
    public Entity Player { get; private set; }
    public SimulationTelemetry Telemetry { get; private set; }
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

    public void Update(float deltaTime)
    {
        if (deltaTime < 0 || !float.IsFinite(deltaTime))
        {
            throw new ArgumentOutOfRangeException(nameof(deltaTime), "Delta time must be finite and non-negative.");
        }

        Feedback = default;
        if (TryReloadDefinitions())
        {
            return;
        }

        if (Status != SimulationStatus.Running)
        {
            if (_input.Retry)
            {
                Restart();
            }
            else
            {
                _feedbackSystem.Update(World, deltaTime);
                _cleanupSystem.Update(World);
            }

            return;
        }

        if (_input.Pause && !_pauseWasPressed)
        {
            IsPaused = !IsPaused;
        }

        _pauseWasPressed = _input.Pause;
        if (IsPaused)
        {
            return;
        }

        if (Phase == StagePhase.Opening)
        {
            _phaseElapsed += deltaTime;
            if (_phaseElapsed >= CurrentStage.OpeningDuration)
            {
                Phase = StagePhase.Playing;
                _phaseElapsed = 0;
            }

            return;
        }

        if (Phase == StagePhase.Results)
        {
            _feedbackSystem.Update(World, deltaTime);
            _cleanupSystem.Update(World);
            _phaseElapsed += deltaTime;
            if (_phaseElapsed >= CurrentStage.ResultsDuration)
            {
                AdvanceStageOrFinish();
            }

            return;
        }

        var damageBefore = Telemetry.DamageEventsApplied;
        var enemiesKilledBefore = Telemetry.EnemiesKilled;
        var playerDamageBefore = Telemetry.PlayerDamageEventsApplied;
        var bombsUsedBefore = Telemetry.BombsUsed;

        _stageSystem.Update(World, Definitions, deltaTime, Telemetry);
        _playerInputSystem.Update(World, _input);
        _weaponSystem.Update(World, Definitions, _input, deltaTime, Telemetry);
        _homingMovementSystem.Update(World, deltaTime);
        _movementSystem.Update(World, deltaTime, Telemetry);
        _movementPatternSystem.Update(World, deltaTime);
        _playerBoundsSystem.Update(World, Definitions.Game.Width, Definitions.Game.Height);
        _outOfBoundsSystem.Update(World, Definitions.Game.Width, Definitions.Game.Height);
        _invincibilitySystem.Update(World, deltaTime);
        var bombDamage = _bombSystem.Update(
            World,
            _input,
            Math.Min(Definitions.Game.Width, Definitions.Game.Height) * 0.4f,
            Telemetry);
        _damageSystem.Update(bombDamage, Telemetry);
        var collisions = _collisionSystem.Detect(World);
        var damageEvents = _bulletHitSystem.Update(collisions, Telemetry);
        _damageSystem.Update(damageEvents, Telemetry);
        _lifetimeSystem.Update(World, deltaTime);
        _feedbackSystem.Update(World, deltaTime);
        _cleanupSystem.Update(World);
        Feedback = new SimulationFeedback(
            Telemetry.DamageEventsApplied - damageBefore,
            Telemetry.EnemiesKilled - enemiesKilledBefore,
            Telemetry.PlayerDamageEventsApplied - playerDamageBefore,
            Telemetry.BombsUsed - bombsUsedBefore);

        if (!World.Query<PlayerComponent>().Any())
        {
            Status = SimulationStatus.GameOver;
        }
        else if (_stageSystem.IsCleared(World, Telemetry))
        {
            BeginResults();
        }
    }

    private bool TryReloadDefinitions()
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
        Restart();
        return true;
    }

    public void Restart()
    {
        World = new World();
        Player = new PlayerFactory().Create(World, Definitions.GetPlayer(Definitions.Game.PlayerId));
        Telemetry = new SimulationTelemetry();
        Status = SimulationStatus.Running;
        IsPaused = false;
        _pauseWasPressed = _input.Pause;
        _bombSystem.Reset(_input.Bomb);
        Feedback = default;
        StageNumber = 0;
        LastStageScore = 0;
        StartStage(Definitions.Game.StageId);
    }

    public void SetPaused(bool isPaused)
    {
        if (Status == SimulationStatus.Running)
        {
            IsPaused = isPaused;
            _pauseWasPressed = _input.Pause;
        }
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
}
