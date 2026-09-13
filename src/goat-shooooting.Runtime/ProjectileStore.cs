using System.Numerics;

namespace GoatShooooting.Runtime;

public enum ProjectileTeam
{
    Player,
    Enemy
}

public enum ProjectileBehavior
{
    Straight,
    Homing
}

public enum ProjectileCancelResistance
{
    Soft,
    Hard,
    Uncancelable
}

public enum ProjectileDamageType
{
    Normal
}

public enum ProjectileClearBehavior
{
    Remove
}

public readonly record struct ProjectileSpawnCommand(
    int OwnerEntityId,
    ProjectileTeam Team,
    string DefinitionId,
    Vector2 Position,
    Vector2 Velocity,
    float HitRadius,
    int Damage,
    float Lifetime,
    string VisualId,
    ProjectileBehavior Behavior = ProjectileBehavior.Straight,
    float HomingTurnRadiansPerSecond = 0,
    bool CanDamage = true,
    bool CanBeCancelled = true,
    ProjectileCancelResistance CancelResistance = ProjectileCancelResistance.Soft,
    int PierceCount = 0,
    ProjectileDamageType DamageType = ProjectileDamageType.Normal,
    ProjectileClearBehavior ClearBehavior = ProjectileClearBehavior.Remove);

public readonly record struct ProjectileSnapshot(
    int Id,
    int OwnerEntityId,
    ProjectileTeam Team,
    string DefinitionId,
    Vector2 PreviousPosition,
    Vector2 Position,
    Vector2 Velocity,
    float HitRadius,
    int Damage,
    float Age,
    float Lifetime,
    string VisualId,
    ProjectileBehavior Behavior,
    bool CanDamage,
    bool CanBeCancelled,
    ProjectileCancelResistance CancelResistance,
    int PierceCount,
    ProjectileDamageType DamageType,
    ProjectileClearBehavior ClearBehavior,
    int GrazedPlayerEntityId,
    bool PendingRemoval);

/// <summary>Dense, reusable structure-of-arrays storage for active projectiles.</summary>
public sealed class ProjectileStore
{
    private const int DefaultCapacity = 256;
    private readonly List<PendingSpawn> _pendingSpawns = new(DefaultCapacity);
    private int[] _ids;
    private int[] _ownerEntityIds;
    private ProjectileTeam[] _teams;
    private string[] _definitionIds;
    private Vector2[] _previousPositions;
    private Vector2[] _positions;
    private Vector2[] _velocities;
    private float[] _hitRadii;
    private int[] _damage;
    private float[] _ages;
    private float[] _lifetimes;
    private string[] _visualIds;
    private ProjectileBehavior[] _behaviors;
    private float[] _homingTurnRates;
    private bool[] _canDamage;
    private bool[] _canBeCancelled;
    private ProjectileCancelResistance[] _cancelResistances;
    private int[] _pierceCounts;
    private ProjectileDamageType[] _damageTypes;
    private ProjectileClearBehavior[] _clearBehaviors;
    private int[] _grazedPlayerEntityIds;
    private bool[] _pendingRemoval;
    private int _nextId = 1;

    public ProjectileStore(int initialCapacity = DefaultCapacity)
    {
        if (initialCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        }

        var capacity = Math.Max(1, initialCapacity);
        _ids = new int[capacity];
        _ownerEntityIds = new int[capacity];
        _teams = new ProjectileTeam[capacity];
        _definitionIds = new string[capacity];
        _previousPositions = new Vector2[capacity];
        _positions = new Vector2[capacity];
        _velocities = new Vector2[capacity];
        _hitRadii = new float[capacity];
        _damage = new int[capacity];
        _ages = new float[capacity];
        _lifetimes = new float[capacity];
        _visualIds = new string[capacity];
        _behaviors = new ProjectileBehavior[capacity];
        _homingTurnRates = new float[capacity];
        _canDamage = new bool[capacity];
        _canBeCancelled = new bool[capacity];
        _cancelResistances = new ProjectileCancelResistance[capacity];
        _pierceCounts = new int[capacity];
        _damageTypes = new ProjectileDamageType[capacity];
        _clearBehaviors = new ProjectileClearBehavior[capacity];
        _grazedPlayerEntityIds = new int[capacity];
        _pendingRemoval = new bool[capacity];
    }

    public int ActiveCount { get; private set; }
    public int PendingSpawnCount => _pendingSpawns.Count;
    public int Capacity => _ids.Length;

    public int QueueSpawn(ProjectileSpawnCommand command)
    {
        Validate(command);
        var id = _nextId++;
        _pendingSpawns.Add(new PendingSpawn(id, command));
        return id;
    }

    public void CommitSpawns(GameEventBuffer? events = null)
    {
        EnsureCapacity(ActiveCount + _pendingSpawns.Count);
        foreach (var pending in _pendingSpawns)
        {
            var index = ActiveCount++;
            var command = pending.Command;
            _ids[index] = pending.Id;
            _ownerEntityIds[index] = command.OwnerEntityId;
            _teams[index] = command.Team;
            _definitionIds[index] = command.DefinitionId;
            _previousPositions[index] = command.Position;
            _positions[index] = command.Position;
            _velocities[index] = command.Velocity;
            _hitRadii[index] = command.HitRadius;
            _damage[index] = command.Damage;
            _ages[index] = 0;
            _lifetimes[index] = command.Lifetime;
            _visualIds[index] = command.VisualId;
            _behaviors[index] = command.Behavior;
            _homingTurnRates[index] = command.HomingTurnRadiansPerSecond;
            _canDamage[index] = command.CanDamage;
            _canBeCancelled[index] = command.CanBeCancelled;
            _cancelResistances[index] = command.CancelResistance;
            _pierceCounts[index] = command.PierceCount;
            _damageTypes[index] = command.DamageType;
            _clearBehaviors[index] = command.ClearBehavior;
            _grazedPlayerEntityIds[index] = 0;
            _pendingRemoval[index] = false;
            events?.Publish((frame, sequence) => new ProjectileSpawnedEvent(
                frame,
                sequence,
                pending.Id,
                command.OwnerEntityId,
                command.Team,
                command.DefinitionId));
        }

        _pendingSpawns.Clear();
    }

    public void QueueRemoveAt(int index)
    {
        ValidateActiveIndex(index);
        _pendingRemoval[index] = true;
    }

    public void QueueRemoveAll()
    {
        for (var index = 0; index < ActiveCount; index++)
        {
            _pendingRemoval[index] = true;
        }

        _pendingSpawns.Clear();
    }

    public void CommitRemovals()
    {
        for (var index = ActiveCount - 1; index >= 0; index--)
        {
            if (_pendingRemoval[index])
            {
                RemoveAt(index);
            }
        }
    }

    public ProjectileSnapshot GetSnapshot(int index)
    {
        ValidateActiveIndex(index);
        return new ProjectileSnapshot(
            _ids[index],
            _ownerEntityIds[index],
            _teams[index],
            _definitionIds[index],
            _previousPositions[index],
            _positions[index],
            _velocities[index],
            _hitRadii[index],
            _damage[index],
            _ages[index],
            _lifetimes[index],
            _visualIds[index],
            _behaviors[index],
            _canDamage[index],
            _canBeCancelled[index],
            _cancelResistances[index],
            _pierceCounts[index],
            _damageTypes[index],
            _clearBehaviors[index],
            _grazedPlayerEntityIds[index],
            _pendingRemoval[index]);
    }

    internal int IdAt(int index) => _ids[index];
    internal int OwnerEntityIdAt(int index) => _ownerEntityIds[index];
    internal ProjectileTeam TeamAt(int index) => _teams[index];
    internal string DefinitionIdAt(int index) => _definitionIds[index];
    internal ref Vector2 PreviousPositionAt(int index) => ref _previousPositions[index];
    internal ref Vector2 PositionAt(int index) => ref _positions[index];
    internal ref Vector2 VelocityAt(int index) => ref _velocities[index];
    internal float HitRadiusAt(int index) => _hitRadii[index];
    internal int DamageAt(int index) => _damage[index];
    internal ref float AgeAt(int index) => ref _ages[index];
    internal float LifetimeAt(int index) => _lifetimes[index];
    internal string VisualIdAt(int index) => _visualIds[index];
    internal ProjectileBehavior BehaviorAt(int index) => _behaviors[index];
    internal float HomingTurnRateAt(int index) => _homingTurnRates[index];
    internal bool CanDamageAt(int index) => _canDamage[index];
    internal bool CanBeCancelledAt(int index) => _canBeCancelled[index];
    internal ProjectileCancelResistance CancelResistanceAt(int index) => _cancelResistances[index];
    internal ref int PierceCountAt(int index) => ref _pierceCounts[index];
    internal ProjectileDamageType DamageTypeAt(int index) => _damageTypes[index];
    internal ProjectileClearBehavior ClearBehaviorAt(int index) => _clearBehaviors[index];
    internal ref int GrazedPlayerEntityIdAt(int index) => ref _grazedPlayerEntityIds[index];
    internal bool IsPendingRemovalAt(int index) => _pendingRemoval[index];

    private static void Validate(ProjectileSpawnCommand command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.DefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.VisualId);
        if (command.OwnerEntityId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "Owner entity id must be positive.");
        }

        if (!float.IsFinite(command.Position.X) || !float.IsFinite(command.Position.Y) ||
            !float.IsFinite(command.Velocity.X) || !float.IsFinite(command.Velocity.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(command), "Position and velocity must be finite.");
        }

        if (!float.IsFinite(command.HitRadius) || command.HitRadius <= 0 ||
            !float.IsFinite(command.Lifetime) || command.Lifetime <= 0 ||
            !float.IsFinite(command.HomingTurnRadiansPerSecond) || command.HomingTurnRadiansPerSecond < 0 ||
            command.Damage <= 0 || command.PierceCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "Projectile numeric values are invalid.");
        }
    }

    private void EnsureCapacity(int required)
    {
        if (required <= Capacity)
        {
            return;
        }

        var capacity = Math.Max(required, checked(Capacity * 2));
        Array.Resize(ref _ids, capacity);
        Array.Resize(ref _ownerEntityIds, capacity);
        Array.Resize(ref _teams, capacity);
        Array.Resize(ref _definitionIds, capacity);
        Array.Resize(ref _previousPositions, capacity);
        Array.Resize(ref _positions, capacity);
        Array.Resize(ref _velocities, capacity);
        Array.Resize(ref _hitRadii, capacity);
        Array.Resize(ref _damage, capacity);
        Array.Resize(ref _ages, capacity);
        Array.Resize(ref _lifetimes, capacity);
        Array.Resize(ref _visualIds, capacity);
        Array.Resize(ref _behaviors, capacity);
        Array.Resize(ref _homingTurnRates, capacity);
        Array.Resize(ref _canDamage, capacity);
        Array.Resize(ref _canBeCancelled, capacity);
        Array.Resize(ref _cancelResistances, capacity);
        Array.Resize(ref _pierceCounts, capacity);
        Array.Resize(ref _damageTypes, capacity);
        Array.Resize(ref _clearBehaviors, capacity);
        Array.Resize(ref _grazedPlayerEntityIds, capacity);
        Array.Resize(ref _pendingRemoval, capacity);
    }

    private void RemoveAt(int index)
    {
        var last = ActiveCount - 1;
        if (index != last)
        {
            _ids[index] = _ids[last];
            _ownerEntityIds[index] = _ownerEntityIds[last];
            _teams[index] = _teams[last];
            _definitionIds[index] = _definitionIds[last];
            _previousPositions[index] = _previousPositions[last];
            _positions[index] = _positions[last];
            _velocities[index] = _velocities[last];
            _hitRadii[index] = _hitRadii[last];
            _damage[index] = _damage[last];
            _ages[index] = _ages[last];
            _lifetimes[index] = _lifetimes[last];
            _visualIds[index] = _visualIds[last];
            _behaviors[index] = _behaviors[last];
            _homingTurnRates[index] = _homingTurnRates[last];
            _canDamage[index] = _canDamage[last];
            _canBeCancelled[index] = _canBeCancelled[last];
            _cancelResistances[index] = _cancelResistances[last];
            _pierceCounts[index] = _pierceCounts[last];
            _damageTypes[index] = _damageTypes[last];
            _clearBehaviors[index] = _clearBehaviors[last];
            _grazedPlayerEntityIds[index] = _grazedPlayerEntityIds[last];
            _pendingRemoval[index] = _pendingRemoval[last];
        }

        _definitionIds[last] = null!;
        _visualIds[last] = null!;
        _pendingRemoval[last] = false;
        ActiveCount--;
    }

    private void ValidateActiveIndex(int index)
    {
        if ((uint)index >= (uint)ActiveCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    private readonly record struct PendingSpawn(int Id, ProjectileSpawnCommand Command);
}
