using System.Numerics;

namespace GoatShooooting.Core;

public sealed class TransformComponent(Vector2 position)
{
    public Vector2 PreviousPosition { get; set; } = position;
    public Vector2 Position { get; set; } = position;

    public void Snap(Vector2 value)
    {
        PreviousPosition = value;
        Position = value;
    }
}

public sealed class VelocityComponent(Vector2 value)
{
    public Vector2 Value { get; set; } = value;
}

public sealed class HealthComponent(int maximum)
{
    public int Maximum { get; } = maximum > 0 ? maximum : throw new ArgumentOutOfRangeException(nameof(maximum));
    public int Current { get; set; } = maximum;
}

public sealed class LivesComponent(int initialLives)
{
    public int Initial { get; } = initialLives > 0
        ? initialLives
        : throw new ArgumentOutOfRangeException(nameof(initialLives));
    public int Remaining { get; set; } = initialLives;
}

public sealed class BombComponent(int initialBombs, int damage)
{
    public int Initial { get; } = initialBombs >= 0
        ? initialBombs
        : throw new ArgumentOutOfRangeException(nameof(initialBombs));
    public int Remaining { get; set; } = initialBombs;
    public int Damage { get; } = damage > 0 ? damage : throw new ArgumentOutOfRangeException(nameof(damage));
}

public sealed class DamageComponent(int value)
{
    public int Value { get; } = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
}

public enum CollisionLayer
{
    Player,
    Enemy,
    PlayerBullet,
    EnemyBullet
}

public sealed class ColliderComponent(float radius, CollisionLayer layer)
{
    public float Radius { get; } = radius > 0 ? radius : throw new ArgumentOutOfRangeException(nameof(radius));
    public CollisionLayer Layer { get; } = layer;
}

public sealed class RotationComponent(float degrees = 0)
{
    public float Degrees { get; set; } = degrees;
}

public enum ActorPartHealthPolicy { Shared, Independent, Indestructible }
public enum HurtboxShape { Circle, Capsule, Aabb, Obb }

public readonly record struct HurtboxShapeData(
    string Id,
    HurtboxShape Shape,
    Vector2 Offset,
    float Radius,
    float Width,
    float Height,
    float Length,
    float RotationDegrees,
    float BoundingRadius);

public sealed class HurtboxSetComponent(IReadOnlyList<HurtboxShapeData> shapes)
{
    public IReadOnlyList<HurtboxShapeData> Shapes { get; } = shapes.ToArray();
    public float MaximumBoundingRadius { get; } = shapes.Count == 0 ? 0 : shapes.Max(static shape =>
        shape.Offset.Length() + shape.BoundingRadius);
}

public readonly record struct HardpointData(
    string Id,
    Vector2 Offset,
    float RotationDegrees,
    string? WeaponId,
    int WeaponHandle);

public sealed class HardpointSetComponent(IReadOnlyList<HardpointData> hardpoints)
{
    public IReadOnlyList<HardpointData> Hardpoints { get; } = hardpoints.ToArray();
}

public sealed class ActorRootComponent(string definitionId, int definitionHandle, ulong tagMask)
{
    public string DefinitionId { get; } = definitionId;
    public int DefinitionHandle { get; } = definitionHandle;
    public ulong TagMask { get; } = tagMask;
}

public sealed class ActorPresentationComponent(
    string? visualId,
    string? animationId,
    string? animationStateId,
    int renderLayer)
{
    public string? VisualId { get; } = visualId;
    public string? AnimationId { get; } = animationId;
    public string? AnimationStateId { get; } = animationStateId;
    public int RenderLayer { get; } = renderLayer;
    public string SemanticState { get; set; } = "idle";
}

public sealed class ParentTransformComponent(
    int rootEntityId,
    int parentEntityId,
    Vector2 localOffset,
    float localRotationDegrees)
{
    public int RootEntityId { get; } = rootEntityId;
    public int ParentEntityId { get; } = parentEntityId;
    public Vector2 LocalOffset { get; } = localOffset;
    public float LocalRotationDegrees { get; } = localRotationDegrees;
}

public sealed class ActorPartComponent(
    int rootEntityId,
    int parentEntityId,
    int partHandle,
    string partId,
    Vector2 localOffset,
    float localRotationDegrees,
    ActorPartHealthPolicy healthPolicy,
    float damageForwardingRatio,
    bool targetable,
    int lockCapacity,
    bool enabled,
    ulong tagMask,
    string interactionClass,
    string? destroySignal,
    string? detachSignal)
{
    public int RootEntityId { get; } = rootEntityId;
    public int ParentEntityId { get; set; } = parentEntityId;
    public int PartHandle { get; } = partHandle;
    public string PartId { get; } = partId;
    public Vector2 LocalOffset { get; } = localOffset;
    public float LocalRotationDegrees { get; } = localRotationDegrees;
    public ActorPartHealthPolicy HealthPolicy { get; } = healthPolicy;
    public float DamageForwardingRatio { get; } = damageForwardingRatio;
    public bool Targetable { get; } = targetable;
    public int LockCapacity { get; } = lockCapacity;
    public bool Enabled { get; set; } = enabled;
    public bool Detached { get; set; }
    public ulong TagMask { get; } = tagMask;
    public string InteractionClass { get; } = interactionClass;
    public string? DestroySignal { get; } = destroySignal;
    public string? DetachSignal { get; } = detachSignal;
}

public sealed class LockTargetComponent(bool targetable = true, int capacity = 1)
{
    public bool Targetable { get; set; } = targetable;
    public int Capacity { get; set; } = Math.Max(0, capacity);
}

public sealed class GrazeRadiusComponent(float radius)
{
    public float Radius { get; } = radius > 0 ? radius : throw new ArgumentOutOfRangeException(nameof(radius));
}

public sealed class PlayerComponent(string definitionId, float speed)
{
    public string DefinitionId { get; } = definitionId;
    public float Speed { get; } = speed;
}

public sealed class ShipComponent(
    string definitionId,
    float normalSpeed,
    float focusSpeed,
    float hitRadius,
    float grazeRadius,
    int initialPower,
    IReadOnlyList<string> normalWeaponIds,
    IReadOnlyList<string> focusWeaponIds,
    int maximumPower = 100,
    int maximumLives = 9,
    int maximumBombs = 9,
    string? bombWeaponId = null,
    string? specialWeaponId = null,
    string? visualId = null,
    int definitionHandle = -1,
    IReadOnlyList<int>? normalWeaponHandles = null,
    IReadOnlyList<int>? focusWeaponHandles = null,
    int bombWeaponHandle = -1,
    int specialWeaponHandle = -1)
{
    public string DefinitionId { get; } = string.IsNullOrWhiteSpace(definitionId)
        ? throw new ArgumentException("Ship definition id must not be empty.", nameof(definitionId))
        : definitionId;
    public float NormalSpeed { get; } = normalSpeed > 0 ? normalSpeed : throw new ArgumentOutOfRangeException(nameof(normalSpeed));
    public float FocusSpeed { get; } = focusSpeed > 0 ? focusSpeed : throw new ArgumentOutOfRangeException(nameof(focusSpeed));
    public float HitRadius { get; } = hitRadius > 0 ? hitRadius : throw new ArgumentOutOfRangeException(nameof(hitRadius));
    public float GrazeRadius { get; } = grazeRadius >= hitRadius ? grazeRadius : throw new ArgumentOutOfRangeException(nameof(grazeRadius));
    public int Power { get; set; } = initialPower >= 0 ? initialPower : throw new ArgumentOutOfRangeException(nameof(initialPower));
    public int MaximumPower { get; } = maximumPower >= initialPower
        ? maximumPower
        : throw new ArgumentOutOfRangeException(nameof(maximumPower));
    public int MaximumLives { get; } = maximumLives > 0
        ? maximumLives
        : throw new ArgumentOutOfRangeException(nameof(maximumLives));
    public int MaximumBombs { get; } = maximumBombs >= 0
        ? maximumBombs
        : throw new ArgumentOutOfRangeException(nameof(maximumBombs));
    public IReadOnlyList<string> NormalWeaponIds { get; } = normalWeaponIds.ToArray();
    public IReadOnlyList<string> FocusWeaponIds { get; } = focusWeaponIds.ToArray();
    public string? BombWeaponId { get; } = bombWeaponId;
    public string? SpecialWeaponId { get; } = specialWeaponId;
    public string? VisualId { get; } = visualId;
    public int DefinitionHandle { get; } = definitionHandle;
    public IReadOnlyList<int> NormalWeaponHandles { get; } = normalWeaponHandles?.ToArray() ?? Array.Empty<int>();
    public IReadOnlyList<int> FocusWeaponHandles { get; } = focusWeaponHandles?.ToArray() ?? Array.Empty<int>();
    public int BombWeaponHandle { get; } = bombWeaponHandle;
    public int SpecialWeaponHandle { get; } = specialWeaponHandle;
    public bool IsFocused { get; set; }
}

public enum PlayerLifeCycleState
{
    Active,
    HitPending,
    BombRescue,
    Dying,
    Respawning,
    Invincible,
    GameOverPending
}

public sealed class PlayerLifeCycleComponent(
    Vector2 respawnPosition,
    float deathAnimationSeconds,
    float respawnDelaySeconds,
    float respawnInvincibilitySeconds,
    int powerLossOnDeath,
    int bombsAfterRespawn)
{
    public PlayerLifeCycleState State { get; set; } = PlayerLifeCycleState.Active;
    public float Timer { get; set; }
    public Vector2 RespawnPosition { get; } = respawnPosition;
    public float DeathAnimationSeconds { get; } = deathAnimationSeconds >= 0
        ? deathAnimationSeconds
        : throw new ArgumentOutOfRangeException(nameof(deathAnimationSeconds));
    public float RespawnDelaySeconds { get; } = respawnDelaySeconds >= 0
        ? respawnDelaySeconds
        : throw new ArgumentOutOfRangeException(nameof(respawnDelaySeconds));
    public float RespawnInvincibilitySeconds { get; } = respawnInvincibilitySeconds >= 0
        ? respawnInvincibilitySeconds
        : throw new ArgumentOutOfRangeException(nameof(respawnInvincibilitySeconds));
    public int PowerLossOnDeath { get; } = powerLossOnDeath >= 0
        ? powerLossOnDeath
        : throw new ArgumentOutOfRangeException(nameof(powerLossOnDeath));
    public int BombsAfterRespawn { get; } = bombsAfterRespawn >= 0
        ? bombsAfterRespawn
        : throw new ArgumentOutOfRangeException(nameof(bombsAfterRespawn));
    public int? HitSourceEntityId { get; set; }
    public bool CanAct => State is PlayerLifeCycleState.Active or PlayerLifeCycleState.Invincible;
    public bool CanBeHit => State is PlayerLifeCycleState.Active or PlayerLifeCycleState.Invincible;
}

public enum ItemMotionState
{
    Scatter,
    Falling,
    Magnetized
}

public sealed class ItemComponent(string definitionId, string kind, int value, string visualId)
{
    public string DefinitionId { get; } = string.IsNullOrWhiteSpace(definitionId)
        ? throw new ArgumentException("Item definition id must not be empty.", nameof(definitionId))
        : definitionId;
    public string Kind { get; } = string.IsNullOrWhiteSpace(kind)
        ? throw new ArgumentException("Item kind must not be empty.", nameof(kind))
        : kind;
    public int Value { get; } = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    public string VisualId { get; } = string.IsNullOrWhiteSpace(visualId)
        ? throw new ArgumentException("Item visual id must not be empty.", nameof(visualId))
        : visualId;
}

public sealed class ItemMotionComponent(Vector2 scatterVelocity)
{
    public ItemMotionState State { get; set; } = ItemMotionState.Scatter;
    public Vector2 Velocity { get; set; } = scatterVelocity;
}

public sealed class OptionUnitComponent(
    int ownerEntityId,
    string definitionId,
    Vector2 offset,
    float followSpeed,
    float radius,
    IReadOnlyList<string> normalWeaponIds,
    IReadOnlyList<string> focusWeaponIds,
    string? visualId,
    IReadOnlyList<int>? normalWeaponHandles = null,
    IReadOnlyList<int>? focusWeaponHandles = null)
{
    public int OwnerEntityId { get; } = ownerEntityId > 0
        ? ownerEntityId
        : throw new ArgumentOutOfRangeException(nameof(ownerEntityId));
    public string DefinitionId { get; } = definitionId;
    public Vector2 Offset { get; } = offset;
    public float FollowSpeed { get; } = followSpeed > 0
        ? followSpeed
        : throw new ArgumentOutOfRangeException(nameof(followSpeed));
    public float Radius { get; } = radius > 0 ? radius : throw new ArgumentOutOfRangeException(nameof(radius));
    public IReadOnlyList<string> NormalWeaponIds { get; } = normalWeaponIds.ToArray();
    public IReadOnlyList<string> FocusWeaponIds { get; } = focusWeaponIds.ToArray();
    public string? VisualId { get; } = visualId;
    public IReadOnlyList<int> NormalWeaponHandles { get; } = normalWeaponHandles?.ToArray() ?? Array.Empty<int>();
    public IReadOnlyList<int> FocusWeaponHandles { get; } = focusWeaponHandles?.ToArray() ?? Array.Empty<int>();
}

public sealed class WeaponRuntimeComponent
{
    private readonly Dictionary<string, WeaponActionState> _states = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, WeaponActionState> States => _states;

    public WeaponActionState GetOrCreate(string key)
    {
        if (!_states.TryGetValue(key, out var state))
        {
            state = new WeaponActionState();
            _states.Add(key, state);
        }

        return state;
    }
}

public sealed class WeaponActionState
{
    public float CooldownRemaining { get; set; }
    public int BurstShotsRemaining { get; set; }
    public float BurstCooldownRemaining { get; set; }
    public float PatternAngleDegrees { get; set; }
    public int PatternDirection { get; set; } = 1;
    public int ShotsSinceDirectionChange { get; set; }
    public bool WasHeld { get; set; }
    public float HeldSeconds { get; set; }
    public int ActiveLaserEntityId { get; set; }
    public List<int> LockedTargetEntityIds { get; } = new();
}

public sealed class LaserComponent(
    int ownerEntityId,
    CollisionLayer ownerLayer,
    Vector2 direction,
    float length,
    float width,
    int damage,
    float damageInterval,
    string visualId,
    string projectileInteraction,
    ulong tagMask = 0,
    int interactionPower = 1,
    int interactionResistance = 0)
{
    public int OwnerEntityId { get; } = ownerEntityId;
    public CollisionLayer OwnerLayer { get; set; } = ownerLayer;
    public Vector2 Direction { get; set; } = direction;
    public float Length { get; } = length;
    public float Width { get; } = width;
    public int Damage { get; } = damage;
    public float DamageInterval { get; } = damageInterval;
    public string VisualId { get; } = visualId;
    public string ProjectileInteraction { get; } = projectileInteraction;
    public ulong TagMask { get; } = tagMask;
    public int InteractionPower { get; } = interactionPower;
    public int InteractionResistance { get; } = interactionResistance;
    public float DamageCooldownRemaining { get; set; }
}

public sealed class EnemyComponent(string definitionId, int definitionHandle = -1)
{
    public string DefinitionId { get; } = definitionId;
    public int DefinitionHandle { get; } = definitionHandle;
}

/// <summary>Renderer-neutral state for a legacy boss marker or a managed multi-phase boss.</summary>
public sealed class BossComponent(string? definitionId = null, string? displayName = null, int definitionHandle = -1)
{
    public string? DefinitionId { get; } = definitionId;
    public int DefinitionHandle { get; } = definitionHandle;
    public string DisplayName { get; } = displayName ?? definitionId ?? string.Empty;
    public int PhaseIndex { get; set; } = -1;
    public string PhaseId { get; set; } = string.Empty;
    public string PhaseDisplayName { get; set; } = string.Empty;
    public float PhaseElapsed { get; set; }
    public float PhaseTimeLimit { get; set; }
    public float WarningSeconds { get; set; }
    public string? CheckpointId { get; set; }
    public int PlayerDeathsAtPhaseStart { get; set; }
    public int BombsUsedAtPhaseStart { get; set; }
    public bool IsInitialized { get; set; }
    public bool IsComplete { get; set; }
    public bool IsManaged => !string.IsNullOrWhiteSpace(DefinitionId);
    public float RemainingTime => Math.Max(0, PhaseTimeLimit - PhaseElapsed);
    public bool IsWarning => IsInitialized && !IsComplete && RemainingTime <= WarningSeconds;
}

public sealed class ScoreValueComponent(int value)
{
    public int Value { get; } = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
}

public sealed class SineMovementComponent(float originX, float amplitude, float frequency)
{
    public float OriginX { get; } = originX;
    public float Amplitude { get; } = amplitude > 0 ? amplitude : throw new ArgumentOutOfRangeException(nameof(amplitude));
    public float Frequency { get; } = frequency > 0 ? frequency : throw new ArgumentOutOfRangeException(nameof(frequency));
    public float Elapsed { get; set; }
}

public sealed class ZigzagMovementComponent(float originX, float amplitude, float frequency)
{
    public float OriginX { get; } = originX;
    public float Amplitude { get; } = amplitude > 0 ? amplitude : throw new ArgumentOutOfRangeException(nameof(amplitude));
    public float Frequency { get; } = frequency > 0 ? frequency : throw new ArgumentOutOfRangeException(nameof(frequency));
    public float Elapsed { get; set; }
}

public sealed class BulletComponent(string definitionId, CollisionLayer targetLayer)
{
    public string DefinitionId { get; } = definitionId;
    public CollisionLayer TargetLayer { get; } = targetLayer;
}

public sealed class HomingMovementComponent(float turnRadiansPerSecond)
{
    public float TurnRadiansPerSecond { get; } = turnRadiansPerSecond > 0
        ? turnRadiansPerSecond
        : throw new ArgumentOutOfRangeException(nameof(turnRadiansPerSecond));
}

public sealed class WeaponHolderComponent(string weaponId, int weaponHandle = -1)
{
    public string WeaponId { get; } = weaponId;
    public int WeaponHandle { get; } = weaponHandle;
    public float CooldownRemaining { get; set; }
    public float PatternAngleDegrees { get; set; }
    public int PatternDirection { get; set; } = 1;
    public int ShotsSinceDirectionChange { get; set; }
}

public sealed class LifetimeComponent(float seconds)
{
    public float Remaining { get; set; } = seconds > 0 ? seconds : throw new ArgumentOutOfRangeException(nameof(seconds));
}

public sealed class InvincibilityComponent(float duration)
{
    public float Duration { get; } = duration > 0 ? duration : throw new ArgumentOutOfRangeException(nameof(duration));
    public float Remaining { get; set; }
}

public sealed class HitFlashComponent(float seconds)
{
    public float Remaining { get; set; } = seconds > 0 ? seconds : throw new ArgumentOutOfRangeException(nameof(seconds));
}

public sealed class ExplosionComponent(float maxRadius, float duration)
{
    public float MaxRadius { get; } = maxRadius > 0 ? maxRadius : throw new ArgumentOutOfRangeException(nameof(maxRadius));
    public float Duration { get; } = duration > 0 ? duration : throw new ArgumentOutOfRangeException(nameof(duration));
    public float Remaining { get; set; } = duration;
}

public sealed class PendingDestroyComponent;
