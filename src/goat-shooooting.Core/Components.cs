using System.Numerics;

namespace GoatShooooting.Core;

public sealed class TransformComponent(Vector2 position)
{
    public Vector2 Position { get; set; } = position;
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

public sealed class PlayerComponent(string definitionId, float speed)
{
    public string DefinitionId { get; } = definitionId;
    public float Speed { get; } = speed;
}

public sealed class EnemyComponent(string definitionId)
{
    public string DefinitionId { get; } = definitionId;
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

public sealed class BulletComponent(string definitionId, CollisionLayer targetLayer)
{
    public string DefinitionId { get; } = definitionId;
    public CollisionLayer TargetLayer { get; } = targetLayer;
}

public sealed class WeaponHolderComponent(string weaponId)
{
    public string WeaponId { get; } = weaponId;
    public float CooldownRemaining { get; set; }
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
