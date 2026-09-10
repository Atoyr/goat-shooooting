using GoatShooooting.Core;
using GoatShooooting.Definitions;

namespace GoatShooooting.Runtime;

/// <summary>Headless-capable composition root for the production game simulation.</summary>
public sealed class ShootingSimulation
{
    private readonly IInputState _input;
    private readonly PlayerInputSystem _playerInputSystem = new();
    private readonly WeaponSystem _weaponSystem = new(new BulletFactory());
    private readonly MovementSystem _movementSystem = new();
    private readonly CollisionSystem _collisionSystem = new();
    private readonly BulletHitSystem _bulletHitSystem = new();
    private readonly DamageSystem _damageSystem = new();
    private readonly LifetimeSystem _lifetimeSystem = new();
    private readonly CleanupSystem _cleanupSystem = new();
    private readonly StageSystem _stageSystem;

    public ShootingSimulation(IDefinitionRepository definitionRepository, IInputState input)
    {
        ArgumentNullException.ThrowIfNull(definitionRepository);
        _input = input ?? throw new ArgumentNullException(nameof(input));
        Definitions = definitionRepository.Load();
        World = new World();
        Player = new PlayerFactory().Create(World, Definitions.GetPlayer(Definitions.Game.PlayerId));
        _stageSystem = new StageSystem(Definitions.GetStage(Definitions.Game.StageId), new EnemyFactory());
    }

    public World World { get; }
    public DefinitionCatalog Definitions { get; }
    public Entity Player { get; }
    public SimulationTelemetry Telemetry { get; } = new();
    public double Elapsed => _stageSystem.Elapsed;

    public void Update(float deltaTime)
    {
        if (deltaTime < 0 || !float.IsFinite(deltaTime))
        {
            throw new ArgumentOutOfRangeException(nameof(deltaTime), "Delta time must be finite and non-negative.");
        }

        _stageSystem.Update(World, Definitions, deltaTime, Telemetry);
        _playerInputSystem.Update(World, _input);
        _weaponSystem.Update(World, Definitions, _input, deltaTime, Telemetry);
        _movementSystem.Update(World, deltaTime, Telemetry);
        var collisions = _collisionSystem.Detect(World);
        var damageEvents = _bulletHitSystem.Update(collisions, Telemetry);
        _damageSystem.Update(damageEvents, Telemetry);
        _lifetimeSystem.Update(World, deltaTime);
        _cleanupSystem.Update(World);
    }
}
