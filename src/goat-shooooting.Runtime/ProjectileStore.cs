using System.Numerics;
using GoatShooooting.Definitions;

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

public enum ProjectileMotionKernel : byte
{
    Linear,
    ScalarAcceleration,
    VectorAcceleration,
    Polar,
    Homing,
    Curve
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
    ProjectileClearBehavior ClearBehavior = ProjectileClearBehavior.Remove,
    float AccelerationPerSecond = 0,
    int TargetEntityId = 0,
    ProjectileHandle? DefinitionHandle = null,
    ProgramHandle? ProgramHandle = null,
    int SpawnLineageId = 0,
    ulong TagMask = 0,
    int InteractionClass = 0,
    int InteractionPower = 1,
    int InteractionResistance = 0,
    string? SourceNodeId = null,
    string? SourceProgramId = null);

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
    float AccelerationPerSecond,
    int GrazedPlayerEntityId,
    bool PendingRemoval,
    int TargetEntityId,
    ProjectileHandle? DefinitionHandle,
    ProgramHandle? ProgramHandle,
    int ProgramCounter,
    long WakeFrame,
    int LocalSlotOffset,
    ProjectileMotionKernel MotionKernel,
    Vector2 Acceleration,
    float AngularVelocity,
    int SpawnLineageId,
    ulong TagMask,
    int InteractionClass,
    int InteractionPower,
    int InteractionResistance,
    string? SourceNodeId,
    string? SourceProgramId);

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
    private float[] _accelerations;
    private int[] _grazedPlayerEntityIds;
    private bool[] _pendingRemoval;
    private int[] _targetEntityIds;
    private int[] _definitionHandles;
    private int[] _programHandles;
    private int[] _programCounters;
    private long[] _wakeFrames;
    private int[] _localSlotOffsets;
    private ProjectileMotionKernel[] _motionKernels;
    private Vector2[] _vectorAccelerations;
    private float[] _angularVelocities;
    private int[] _spawnLineageIds;
    private ulong[] _tagMasks;
    private int[] _interactionClasses;
    private int[] _interactionPowers;
    private int[] _interactionResistances;
    private string?[] _sourceNodeIds;
    private string?[] _sourceProgramIds;
    private CompiledCatalog? _compiledDefinitions;
    private ResolvedProgramBinding?[] _programBindings = Array.Empty<ResolvedProgramBinding?>();
    private long _runSeed;
    private int _stageInstance;
    private int _nextId = 1;
    private readonly Dictionary<int, int> _indexById = new();
    private readonly PriorityQueue<int, (long Frame, int Id)> _wakeSchedule = new();

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
        _accelerations = new float[capacity];
        _grazedPlayerEntityIds = new int[capacity];
        _pendingRemoval = new bool[capacity];
        _targetEntityIds = new int[capacity];
        _definitionHandles = new int[capacity];
        _programHandles = new int[capacity];
        Array.Fill(_definitionHandles, -1);
        Array.Fill(_programHandles, -1);
        _programCounters = new int[capacity];
        _wakeFrames = new long[capacity];
        _localSlotOffsets = new int[capacity];
        _motionKernels = new ProjectileMotionKernel[capacity];
        _vectorAccelerations = new Vector2[capacity];
        _angularVelocities = new float[capacity];
        _spawnLineageIds = new int[capacity];
        _tagMasks = new ulong[capacity];
        _interactionClasses = new int[capacity];
        _interactionPowers = new int[capacity];
        _interactionResistances = new int[capacity];
        _sourceNodeIds = new string?[capacity];
        _sourceProgramIds = new string?[capacity];
    }

    public int ActiveCount { get; private set; }
    public int PendingSpawnCount => _pendingSpawns.Count;
    public int Capacity => _ids.Length;
    internal bool HasCompiledDefinitions => _compiledDefinitions is not null;

    internal ProjectileStore CloneForCheckpoint()
    {
        var clone = new ProjectileStore(Capacity)
        {
            ActiveCount = ActiveCount,
            _ids = (int[])_ids.Clone(),
            _ownerEntityIds = (int[])_ownerEntityIds.Clone(),
            _teams = (ProjectileTeam[])_teams.Clone(),
            _definitionIds = (string[])_definitionIds.Clone(),
            _previousPositions = (Vector2[])_previousPositions.Clone(),
            _positions = (Vector2[])_positions.Clone(),
            _velocities = (Vector2[])_velocities.Clone(),
            _hitRadii = (float[])_hitRadii.Clone(),
            _damage = (int[])_damage.Clone(),
            _ages = (float[])_ages.Clone(),
            _lifetimes = (float[])_lifetimes.Clone(),
            _visualIds = (string[])_visualIds.Clone(),
            _behaviors = (ProjectileBehavior[])_behaviors.Clone(),
            _homingTurnRates = (float[])_homingTurnRates.Clone(),
            _canDamage = (bool[])_canDamage.Clone(),
            _canBeCancelled = (bool[])_canBeCancelled.Clone(),
            _cancelResistances = (ProjectileCancelResistance[])_cancelResistances.Clone(),
            _pierceCounts = (int[])_pierceCounts.Clone(),
            _damageTypes = (ProjectileDamageType[])_damageTypes.Clone(),
            _clearBehaviors = (ProjectileClearBehavior[])_clearBehaviors.Clone(),
            _accelerations = (float[])_accelerations.Clone(),
            _grazedPlayerEntityIds = (int[])_grazedPlayerEntityIds.Clone(),
            _pendingRemoval = (bool[])_pendingRemoval.Clone(),
            _targetEntityIds = (int[])_targetEntityIds.Clone(),
            _definitionHandles = (int[])_definitionHandles.Clone(),
            _programHandles = (int[])_programHandles.Clone(),
            _programCounters = (int[])_programCounters.Clone(),
            _wakeFrames = (long[])_wakeFrames.Clone(),
            _localSlotOffsets = (int[])_localSlotOffsets.Clone(),
            _motionKernels = (ProjectileMotionKernel[])_motionKernels.Clone(),
            _vectorAccelerations = (Vector2[])_vectorAccelerations.Clone(),
            _angularVelocities = (float[])_angularVelocities.Clone(),
            _spawnLineageIds = (int[])_spawnLineageIds.Clone(),
            _tagMasks = (ulong[])_tagMasks.Clone(),
            _interactionClasses = (int[])_interactionClasses.Clone(),
            _interactionPowers = (int[])_interactionPowers.Clone(),
            _interactionResistances = (int[])_interactionResistances.Clone(),
            _sourceNodeIds = (string?[])_sourceNodeIds.Clone(),
            _sourceProgramIds = (string?[])_sourceProgramIds.Clone(),
            _compiledDefinitions = _compiledDefinitions,
            _programBindings = (ResolvedProgramBinding?[])_programBindings.Clone(),
            _runSeed = _runSeed,
            _stageInstance = _stageInstance,
            _nextId = _nextId
        };
        clone._pendingSpawns.AddRange(_pendingSpawns);
        foreach (var pair in _indexById) clone._indexById.Add(pair.Key, pair.Value);
        foreach (var item in _wakeSchedule.UnorderedItems)
            clone._wakeSchedule.Enqueue(item.Element, item.Priority);
        return clone;
    }

    public void ConfigurePrograms(
        CompiledCatalog definitions,
        VariantHandle? variantHandle,
        long runSeed = 0,
        int stageInstance = 0)
    {
        _compiledDefinitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _runSeed = runSeed;
        _stageInstance = stageInstance;
        _programBindings = new ResolvedProgramBinding?[definitions.Projectiles.Count];
        foreach (var projectile in definitions.Projectiles)
        {
            if (projectile.ProgramSlot is not null)
                _programBindings[projectile.Handle.Value] = definitions.ResolveProgramBinding(projectile.ProgramSlot, variantHandle);
        }
    }

    public void SetProgramStageInstance(int stageInstance)
    {
        if (stageInstance < 0) throw new ArgumentOutOfRangeException(nameof(stageInstance));
        _stageInstance = stageInstance;
    }

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
            _indexById.Add(pending.Id, index);
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
            _accelerations[index] = command.AccelerationPerSecond;
            _grazedPlayerEntityIds[index] = 0;
            _pendingRemoval[index] = false;
            _targetEntityIds[index] = command.TargetEntityId;
            _definitionHandles[index] = command.DefinitionHandle?.Value ?? -1;
            var binding = command.DefinitionHandle is { } definitionHandle &&
                (uint)definitionHandle.Value < (uint)_programBindings.Length
                ? _programBindings[definitionHandle.Value]
                : null;
            _programHandles[index] = command.ProgramHandle?.Value ?? binding?.Program.Handle.Value ?? -1;
            _programCounters[index] = 0;
            _wakeFrames[index] = 0;
            _localSlotOffsets[index] = 0;
            _motionKernels[index] = command.ProgramHandle is not null
                ? ProjectileMotionKernel.Linear
                : command.Behavior == ProjectileBehavior.Homing
                    ? ProjectileMotionKernel.Homing
                    : command.AccelerationPerSecond != 0
                        ? ProjectileMotionKernel.ScalarAcceleration
                        : ProjectileMotionKernel.Linear;
            _vectorAccelerations[index] = Vector2.Zero;
            _angularVelocities[index] = 0;
            _spawnLineageIds[index] = command.SpawnLineageId > 0 ? command.SpawnLineageId : pending.Id;
            var teamTags = _compiledDefinitions is null
                ? 0
                : _compiledDefinitions.Tags.Mask(command.Team == ProjectileTeam.Player
                    ? new[] { "projectile", "shot" }
                    : new[] { "projectile", "bullet" });
            _tagMasks[index] = command.TagMask | teamTags;
            _interactionClasses[index] = command.InteractionClass;
            _interactionPowers[index] = command.InteractionPower;
            _interactionResistances[index] = command.InteractionResistance;
            _sourceNodeIds[index] = command.SourceNodeId;
            _sourceProgramIds[index] = command.SourceProgramId;
            if (_programHandles[index] >= 0) _wakeSchedule.Enqueue(pending.Id, (0, pending.Id));
            events?.Publish((frame, sequence) => new ProjectileSpawnedEvent(
                frame,
                sequence,
                pending.Id,
                command.OwnerEntityId,
                command.Team,
                command.DefinitionId,
                command.SourceProgramId ?? (_programHandles[index] < 0 || _compiledDefinitions is null
                    ? null : _compiledDefinitions.Get(new ProgramHandle(_programHandles[index])).Definition.Id),
                command.SourceNodeId));
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
            _accelerations[index],
            _grazedPlayerEntityIds[index],
            _pendingRemoval[index],
            _targetEntityIds[index],
            _definitionHandles[index] < 0 ? null : new ProjectileHandle(_definitionHandles[index]),
            _programHandles[index] < 0 ? null : new ProgramHandle(_programHandles[index]),
            _programCounters[index],
            _wakeFrames[index],
            _localSlotOffsets[index],
            _motionKernels[index],
            _vectorAccelerations[index],
            _angularVelocities[index],
            _spawnLineageIds[index],
            _tagMasks[index],
            _interactionClasses[index],
            _interactionPowers[index],
            _interactionResistances[index],
            _sourceNodeIds[index],
            _sourceProgramIds[index]);
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
    internal float AccelerationAt(int index) => _accelerations[index];
    internal ref int GrazedPlayerEntityIdAt(int index) => ref _grazedPlayerEntityIds[index];
    internal bool IsPendingRemovalAt(int index) => _pendingRemoval[index];
    internal int TargetEntityIdAt(int index) => _targetEntityIds[index];
    internal int DefinitionHandleAt(int index) => _definitionHandles[index];
    internal int ProgramHandleAt(int index) => _programHandles[index];
    internal ref int ProgramCounterAt(int index) => ref _programCounters[index];
    internal ref long WakeFrameAt(int index) => ref _wakeFrames[index];
    internal ref int LocalSlotOffsetAt(int index) => ref _localSlotOffsets[index];
    internal ref ProjectileMotionKernel MotionKernelAt(int index) => ref _motionKernels[index];
    internal ref Vector2 VectorAccelerationAt(int index) => ref _vectorAccelerations[index];
    internal ref float AngularVelocityAt(int index) => ref _angularVelocities[index];
    internal int SpawnLineageIdAt(int index) => _spawnLineageIds[index];
    internal ref ulong TagMaskAt(int index) => ref _tagMasks[index];
    internal ref int InteractionClassAt(int index) => ref _interactionClasses[index];
    internal int InteractionPowerAt(int index) => _interactionPowers[index];
    internal int InteractionResistanceAt(int index) => _interactionResistances[index];
    internal ResolvedProgramBinding GetProgramBindingAt(int index)
    {
        var definitionHandle = _definitionHandles[index];
        if ((uint)definitionHandle >= (uint)_programBindings.Length || _programBindings[definitionHandle] is not { } binding)
            throw new InvalidOperationException("The projectile has no configured program binding.");
        return binding;
    }

    internal void ClearProgramAt(int index) => _programHandles[index] = -1;

    internal void ScheduleWakeAt(int index, long frame)
    {
        _wakeFrames[index] = frame;
        _wakeSchedule.Enqueue(_ids[index], (frame, _ids[index]));
    }

    internal void CollectDueProgramIndices(long frame, List<int> output)
    {
        output.Clear();
        while (_wakeSchedule.TryPeek(out _, out var priority) && priority.Frame <= frame)
        {
            _wakeSchedule.TryDequeue(out var id, out var scheduled);
            if (!_indexById.TryGetValue(id, out var index) || _programHandles[index] < 0 ||
                _wakeFrames[index] != scheduled.Frame) continue;
            output.Add(index);
        }
    }

    internal void QueueCompiledChild(
        int parentIndex,
        ProjectileHandle handle,
        Vector2 velocity,
        string nodeId,
        int childIndex)
    {
        if (_compiledDefinitions is null) throw new InvalidOperationException("Projectile programs are not configured.");
        var compiled = _compiledDefinitions.Get(handle);
        var definition = compiled.Definition;
        QueueSpawn(new ProjectileSpawnCommand(
            _ownerEntityIds[parentIndex],
            _teams[parentIndex],
            definition.Id,
            _positions[parentIndex],
            velocity,
            definition.HitRadius,
            definition.Damage,
            definition.Lifetime,
            definition.VisualId,
            compiled.Behavior.Behavior,
            compiled.Behavior.HomingTurnRadiansPerSecond,
            definition.CanDamage,
            definition.CanBeCancelled,
            ParseResistance(definition.CancelResistance),
            definition.PierceCount,
            DefinitionHandle: handle,
            SpawnLineageId: DeriveLineage(
                _runSeed,
                _stageInstance,
                _spawnLineageIds[parentIndex],
                _programHandles[parentIndex],
                nodeId,
                _programCounters[parentIndex],
                childIndex),
            TagMask: compiled.TagMask,
            InteractionPower: compiled.InteractionPower,
            InteractionResistance: compiled.InteractionResistance,
            SourceNodeId: nodeId,
            SourceProgramId: _compiledDefinitions.Get(new ProgramHandle(_programHandles[parentIndex])).Definition.Id));
    }

    internal void TransformAt(int index, ProjectileHandle handle)
    {
        if (_compiledDefinitions is null) throw new InvalidOperationException("Projectile programs are not configured.");
        var compiled = _compiledDefinitions.Get(handle);
        var definition = compiled.Definition;
        _definitionHandles[index] = handle.Value;
        _definitionIds[index] = definition.Id;
        _hitRadii[index] = definition.HitRadius;
        _damage[index] = definition.Damage;
        _lifetimes[index] = definition.Lifetime;
        _visualIds[index] = definition.VisualId;
        _canDamage[index] = definition.CanDamage;
        _canBeCancelled[index] = definition.CanBeCancelled;
        _cancelResistances[index] = ParseResistance(definition.CancelResistance);
        _tagMasks[index] = compiled.TagMask | (_compiledDefinitions?.Tags.Mask(
            _teams[index] == ProjectileTeam.Player ? new[] { "shot" } : new[] { "bullet" }) ?? 0);
        _interactionPowers[index] = compiled.InteractionPower;
        _interactionResistances[index] = compiled.InteractionResistance;
        var binding = (uint)handle.Value < (uint)_programBindings.Length ? _programBindings[handle.Value] : null;
        _programHandles[index] = binding?.Program.Handle.Value ?? -1;
        _programCounters[index] = 0;
        _wakeFrames[index] = 0;
    }

    private static int DeriveLineage(
        long runSeed,
        int stageInstance,
        int parent,
        int program,
        string nodeId,
        int invocation,
        int child)
    {
        unchecked
        {
            var hash = 14695981039346656037UL;
            Add((ulong)runSeed);
            Add((uint)stageInstance);
            Add((uint)parent);
            Add((uint)program);
            foreach (var character in nodeId) Add(character);
            Add((uint)invocation);
            Add((uint)child);
            var value = (int)(hash & int.MaxValue);
            return value == 0 ? 1 : value;

            void Add(ulong value)
            {
                hash ^= value;
                hash *= 1099511628211UL;
            }
        }
    }

    private static ProjectileCancelResistance ParseResistance(string value) => value switch
    {
        "soft" => ProjectileCancelResistance.Soft,
        "hard" => ProjectileCancelResistance.Hard,
        "uncancelable" => ProjectileCancelResistance.Uncancelable,
        _ => throw new DefinitionValidationException($"Unknown projectile cancel resistance '{value}'.")
    };

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
            !float.IsFinite(command.AccelerationPerSecond) ||
            command.Damage <= 0 || command.PierceCount < 0 || command.TargetEntityId < 0 ||
            command.InteractionPower < 0 || command.InteractionResistance < 0)
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
        Array.Resize(ref _accelerations, capacity);
        Array.Resize(ref _grazedPlayerEntityIds, capacity);
        Array.Resize(ref _pendingRemoval, capacity);
        Array.Resize(ref _targetEntityIds, capacity);
        ResizeWithSentinel(ref _definitionHandles, capacity, -1);
        ResizeWithSentinel(ref _programHandles, capacity, -1);
        Array.Resize(ref _programCounters, capacity);
        Array.Resize(ref _wakeFrames, capacity);
        Array.Resize(ref _localSlotOffsets, capacity);
        Array.Resize(ref _motionKernels, capacity);
        Array.Resize(ref _vectorAccelerations, capacity);
        Array.Resize(ref _angularVelocities, capacity);
        Array.Resize(ref _spawnLineageIds, capacity);
        Array.Resize(ref _tagMasks, capacity);
        Array.Resize(ref _interactionClasses, capacity);
        Array.Resize(ref _interactionPowers, capacity);
        Array.Resize(ref _interactionResistances, capacity);
        Array.Resize(ref _sourceNodeIds, capacity);
        Array.Resize(ref _sourceProgramIds, capacity);
    }

    private void RemoveAt(int index)
    {
        var last = ActiveCount - 1;
        _indexById.Remove(_ids[index]);
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
            _accelerations[index] = _accelerations[last];
            _grazedPlayerEntityIds[index] = _grazedPlayerEntityIds[last];
            _pendingRemoval[index] = _pendingRemoval[last];
            _targetEntityIds[index] = _targetEntityIds[last];
            _definitionHandles[index] = _definitionHandles[last];
            _programHandles[index] = _programHandles[last];
            _programCounters[index] = _programCounters[last];
            _wakeFrames[index] = _wakeFrames[last];
            _localSlotOffsets[index] = _localSlotOffsets[last];
            _motionKernels[index] = _motionKernels[last];
            _vectorAccelerations[index] = _vectorAccelerations[last];
            _angularVelocities[index] = _angularVelocities[last];
            _spawnLineageIds[index] = _spawnLineageIds[last];
            _tagMasks[index] = _tagMasks[last];
            _interactionClasses[index] = _interactionClasses[last];
            _interactionPowers[index] = _interactionPowers[last];
            _interactionResistances[index] = _interactionResistances[last];
            _sourceNodeIds[index] = _sourceNodeIds[last];
            _sourceProgramIds[index] = _sourceProgramIds[last];
            _indexById[_ids[index]] = index;
        }

        _definitionIds[last] = null!;
        _visualIds[last] = null!;
        _pendingRemoval[last] = false;
        _targetEntityIds[last] = 0;
        _definitionHandles[last] = -1;
        _programHandles[last] = -1;
        _programCounters[last] = 0;
        _wakeFrames[last] = 0;
        _localSlotOffsets[last] = 0;
        _motionKernels[last] = default;
        _vectorAccelerations[last] = default;
        _angularVelocities[last] = 0;
        _spawnLineageIds[last] = 0;
        _tagMasks[last] = 0;
        _interactionClasses[last] = 0;
        _interactionPowers[last] = 0;
        _interactionResistances[last] = 0;
        _sourceNodeIds[last] = null;
        _sourceProgramIds[last] = null;
        ActiveCount--;
    }

    private static void ResizeWithSentinel(ref int[] values, int capacity, int sentinel)
    {
        var previous = values.Length;
        Array.Resize(ref values, capacity);
        Array.Fill(values, sentinel, previous, capacity - previous);
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
