using System.Numerics;
using GoatShooooting.Core;
using GoatShooooting.Definitions;

namespace GoatShooooting.Runtime;

public sealed class PlayerFactory
{
    public Entity Create(World world, PlayerDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(definition);

        var entity = world.CreateEntity()
            .Add(new TransformComponent(new Vector2(definition.X, definition.Y)))
            .Add(new VelocityComponent(Vector2.Zero))
            .Add(new LivesComponent(definition.Lives))
            .Add(new BombComponent(definition.Bombs, definition.BombDamage))
            .Add(new ColliderComponent(definition.Radius, CollisionLayer.Player))
            .Add(new PlayerComponent(definition.Id, definition.Speed))
            .Add(new WeaponHolderComponent(definition.WeaponId));

        if (definition.InvincibilitySeconds > 0)
        {
            entity.Add(new InvincibilityComponent(definition.InvincibilitySeconds));
        }

        return entity;
    }
}

public sealed class EnemyFactory
{
    public Entity Create(World world, EnemyDefinition definition, Vector2 position)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(definition);

        var entity = world.CreateEntity()
            .Add(new TransformComponent(position))
            .Add(new VelocityComponent(new Vector2(0, definition.Speed)))
            .Add(new HealthComponent(definition.Hp))
            .Add(new ColliderComponent(definition.Radius, CollisionLayer.Enemy))
            .Add(new EnemyComponent(definition.Id))
            .Add(new ScoreValueComponent(definition.Score));

        if (!string.IsNullOrWhiteSpace(definition.WeaponId))
        {
            entity.Add(new WeaponHolderComponent(definition.WeaponId));
        }

        if (string.Equals(definition.MovementPattern, "sine", StringComparison.Ordinal))
        {
            entity.Add(new SineMovementComponent(
                position.X,
                definition.MovementAmplitude,
                definition.MovementFrequency));
        }
        else if (string.Equals(definition.MovementPattern, "zigzag", StringComparison.Ordinal))
        {
            entity.Add(new ZigzagMovementComponent(
                position.X,
                definition.MovementAmplitude,
                definition.MovementFrequency));
        }

        return entity;
    }
}

public sealed class BulletFactory
{
    public Entity Create(
        World world,
        BulletDefinition definition,
        Vector2 position,
        Vector2 direction,
        CollisionLayer ownerLayer)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(definition);
        if (direction == Vector2.Zero)
        {
            throw new ArgumentException("Bullet direction cannot be zero.", nameof(direction));
        }

        var targetLayer = ownerLayer switch
        {
            CollisionLayer.Player => CollisionLayer.Enemy,
            CollisionLayer.Enemy => CollisionLayer.Player,
            _ => throw new ArgumentOutOfRangeException(nameof(ownerLayer), ownerLayer, "Only player or enemy entities may own bullets.")
        };
        var bulletLayer = ownerLayer == CollisionLayer.Player
            ? CollisionLayer.PlayerBullet
            : CollisionLayer.EnemyBullet;

        var entity = world.CreateEntity()
            .Add(new TransformComponent(position))
            .Add(new VelocityComponent(Vector2.Normalize(direction) * definition.Speed))
            .Add(new DamageComponent(definition.Damage))
            .Add(new ColliderComponent(definition.Radius, bulletLayer))
            .Add(new BulletComponent(definition.Id, targetLayer))
            .Add(new LifetimeComponent(definition.Lifetime));

        if (string.Equals(definition.MovementPattern, "homing", StringComparison.Ordinal))
        {
            entity.Add(new HomingMovementComponent(
                definition.HomingTurnDegreesPerSecond * (MathF.PI / 180)));
        }

        return entity;
    }
}
