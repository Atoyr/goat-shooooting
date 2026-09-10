using GoatShooooting.Core;
using GoatShooooting.Definitions;

namespace GoatShooooting.Runtime;

public enum SimulationStatus
{
    Running,
    StageClear,
    GameOver
}

/// <summary>Headless-capable composition root for the production game simulation.</summary>
public sealed class ShootingSimulation
{
    private readonly IInputState _input;
    private readonly PlayerInputSystem _playerInputSystem = new();
    private readonly WeaponSystem _weaponSystem = new(new BulletFactory());
    private readonly MovementSystem _movementSystem = new();
    private readonly PlayerBoundsSystem _playerBoundsSystem = new();
    private readonly OutOfBoundsSystem _outOfBoundsSystem = new();
    private readonly CollisionSystem _collisionSystem = new();
    private readonly BulletHitSystem _bulletHitSystem = new();
    private readonly DamageSystem _damageSystem = new();
    private readonly LifetimeSystem _lifetimeSystem = new();
    private readonly CleanupSystem _cleanupSystem = new();
    private StageSystem _stageSystem = null!;

    public ShootingSimulation(IDefinitionRepository definitionRepository, IInputState input)
    {
        ArgumentNullException.ThrowIfNull(definitionRepository);
        _input = input ?? throw new ArgumentNullException(nameof(input));
        Definitions = definitionRepository.Load();
        World = null!;
        Player = null!;
        Telemetry = null!;
        Restart();
    }

    public World World { get; private set; }
    public DefinitionCatalog Definitions { get; }
    public Entity Player { get; private set; }
    public SimulationTelemetry Telemetry { get; private set; }
    public double Elapsed => _stageSystem.Elapsed;
    public SimulationStatus Status { get; private set; }

    public void Update(float deltaTime)
    {
        if (deltaTime < 0 || !float.IsFinite(deltaTime))
        {
            throw new ArgumentOutOfRangeException(nameof(deltaTime), "Delta time must be finite and non-negative.");
        }

        if (Status != SimulationStatus.Running)
        {
            if (_input.Retry)
            {
                Restart();
            }

            return;
        }

        _stageSystem.Update(World, Definitions, deltaTime, Telemetry);
        _playerInputSystem.Update(World, _input);
        _weaponSystem.Update(World, Definitions, _input, deltaTime, Telemetry);
        _movementSystem.Update(World, deltaTime, Telemetry);
        _playerBoundsSystem.Update(World, Definitions.Game.Width, Definitions.Game.Height);
        _outOfBoundsSystem.Update(World, Definitions.Game.Width, Definitions.Game.Height);
        var collisions = _collisionSystem.Detect(World);
        var damageEvents = _bulletHitSystem.Update(collisions, Telemetry);
        _damageSystem.Update(damageEvents, Telemetry);
        _lifetimeSystem.Update(World, deltaTime);
        _cleanupSystem.Update(World);

        if (!World.Query<PlayerComponent>().Any())
        {
            Status = SimulationStatus.GameOver;
        }
        else if (_stageSystem.IsComplete && !World.Query<EnemyComponent>().Any())
        {
            Status = SimulationStatus.StageClear;
        }
    }

    private void Restart()
    {
        World = new World();
        Player = new PlayerFactory().Create(World, Definitions.GetPlayer(Definitions.Game.PlayerId));
        Telemetry = new SimulationTelemetry();
        _stageSystem = new StageSystem(Definitions.GetStage(Definitions.Game.StageId), new EnemyFactory());
        Status = SimulationStatus.Running;
    }
}
