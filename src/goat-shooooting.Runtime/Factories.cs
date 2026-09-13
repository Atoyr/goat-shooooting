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
            .Add(new GrazeRadiusComponent(definition.Radius + 20))
            .Add(new PlayerComponent(definition.Id, definition.Speed))
            .Add(new WeaponHolderComponent(definition.WeaponId));

        if (definition.InvincibilitySeconds > 0)
        {
            entity.Add(new InvincibilityComponent(definition.InvincibilitySeconds));
        }

        return entity;
    }
}

public sealed class EnemyFactory(RuntimeCapabilityRegistry? capabilities = null)
{
    private readonly RuntimeCapabilityRegistry _capabilities = capabilities ?? RuntimeCapabilityRegistry.CreateBuiltIn();

    public Entity Create(World world, EnemyDefinition definition, Vector2 position, bool isBoss = false)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(definition);
        definition = DefinitionMigrator.Migrate(definition);

        var entity = world.CreateEntity()
            .Add(new TransformComponent(position))
            .Add(new VelocityComponent(new Vector2(0, definition.Speed)))
            .Add(new HealthComponent(definition.Hp))
            .Add(new ColliderComponent(definition.Radius, CollisionLayer.Enemy))
            .Add(new EnemyComponent(definition.Id))
            .Add(new ScoreValueComponent(definition.Score));

        if (isBoss)
        {
            entity.Add(new BossComponent());
        }

        if (!string.IsNullOrWhiteSpace(definition.WeaponId))
        {
            entity.Add(new WeaponHolderComponent(definition.WeaponId));
        }

        var motion = definition.Motion!;
        var factory = _capabilities.ActorMotions.Resolve(motion.Type, $"enemy '{definition.Id}' motion");
        factory.Validate(motion, $"enemy '{definition.Id}' motion");
        factory.Apply(entity, position, motion);

        return entity;
    }
}

public sealed class BulletFactory(RuntimeCapabilityRegistry? capabilities = null)
{
    private readonly RuntimeCapabilityRegistry _capabilities = capabilities ?? RuntimeCapabilityRegistry.CreateBuiltIn();

    public int Create(
        ProjectileStore projectiles,
        BulletDefinition definition,
        Vector2 position,
        Vector2 direction,
        CollisionLayer ownerLayer,
        int ownerEntityId)
    {
        ArgumentNullException.ThrowIfNull(projectiles);
        ArgumentNullException.ThrowIfNull(definition);
        definition = DefinitionMigrator.Migrate(definition);
        if (direction == Vector2.Zero)
        {
            throw new ArgumentException("Bullet direction cannot be zero.", nameof(direction));
        }

        var team = ownerLayer switch
        {
            CollisionLayer.Player => ProjectileTeam.Player,
            CollisionLayer.Enemy => ProjectileTeam.Enemy,
            _ => throw new ArgumentOutOfRangeException(
                nameof(ownerLayer),
                ownerLayer,
                "Only player or enemy entities may own bullets.")
        };
        var behaviorDefinition = definition.Behavior!;
        var behaviorFactory = _capabilities.ProjectileBehaviors.Resolve(
            behaviorDefinition.Type,
            $"bullet '{definition.Id}' behavior");
        behaviorFactory.Validate(behaviorDefinition, $"bullet '{definition.Id}' behavior");
        var behavior = behaviorFactory.Create(behaviorDefinition);
        return projectiles.QueueSpawn(new ProjectileSpawnCommand(
            ownerEntityId,
            team,
            definition.Id,
            position,
            Vector2.Normalize(direction) * definition.Speed,
            definition.Radius,
            definition.Damage,
            definition.Lifetime,
            definition.Id,
            behavior.Behavior,
            behavior.HomingTurnRadiansPerSecond));
    }

    public Entity Create(
        World world,
        BulletDefinition definition,
        Vector2 position,
        Vector2 direction,
        CollisionLayer ownerLayer)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(definition);
        definition = DefinitionMigrator.Migrate(definition);
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

        var behaviorDefinition = definition.Behavior!;
        var behaviorFactory = _capabilities.ProjectileBehaviors.Resolve(
            behaviorDefinition.Type,
            $"bullet '{definition.Id}' behavior");
        behaviorFactory.Validate(behaviorDefinition, $"bullet '{definition.Id}' behavior");
        var behavior = behaviorFactory.Create(behaviorDefinition);
        if (behavior.Behavior == ProjectileBehavior.Homing)
        {
            entity.Add(new HomingMovementComponent(behavior.HomingTurnRadiansPerSecond));
        }

        return entity;
    }
}
