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

        var ship = DefinitionMigrator.FromPlayer(DefinitionMigrator.Migrate(definition));
        return Create(world, ship, new Vector2(definition.X, definition.Y), definition.BombDamage, definition.InvincibilitySeconds);
    }

    public Entity Create(
        World world,
        ShipDefinition definition,
        Vector2 position,
        int bombDamage = 50,
        float? invincibilitySeconds = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(definition);

        var entity = world.CreateEntity()
            .Add(new TransformComponent(position))
            .Add(new VelocityComponent(Vector2.Zero))
            .Add(new LivesComponent(definition.InitialLives))
            .Add(new BombComponent(definition.InitialBombs, bombDamage))
            .Add(new ColliderComponent(definition.HitRadius, CollisionLayer.Player))
            .Add(new GrazeRadiusComponent(definition.GrazeRadius))
            .Add(new PlayerComponent(definition.Id, definition.NormalSpeed))
            .Add(new ShipComponent(
                definition.Id,
                definition.NormalSpeed,
                definition.FocusSpeed,
                definition.HitRadius,
                definition.GrazeRadius,
                definition.InitialPower,
                definition.NormalWeaponIds,
                definition.FocusWeaponIds,
                definition.MaximumPower,
                definition.MaximumLives,
                definition.MaximumBombs,
                definition.BombWeaponId,
                definition.SpecialWeaponId,
                definition.VisualId))
            .Add(new PlayerLifeCycleComponent(
                new Vector2(definition.RespawnX ?? position.X, definition.RespawnY ?? position.Y),
                definition.DeathAnimationSeconds,
                definition.RespawnDelaySeconds,
                definition.RespawnInvincibilitySeconds,
                definition.PowerLossOnDeath,
                definition.BombsAfterRespawn))
            .Add(new WeaponRuntimeComponent());

        if (definition.NormalWeaponIds.Count > 0)
        {
            entity.Add(new WeaponHolderComponent(definition.NormalWeaponIds[0]));
        }

        var invincibilityDuration = invincibilitySeconds ?? definition.RespawnInvincibilitySeconds;
        entity.Add(new InvincibilityComponent(Math.Max(0.0001f, invincibilityDuration)));

        foreach (var option in definition.Options)
        {
            world.CreateEntity()
                .Add(new TransformComponent(position + new Vector2(option.OffsetX, option.OffsetY)))
                .Add(new OptionUnitComponent(
                    entity.Id,
                    option.Id,
                    new Vector2(option.OffsetX, option.OffsetY),
                    option.FollowSpeed,
                    option.Radius,
                    option.NormalWeaponIds,
                    option.FocusWeaponIds,
                    option.VisualId))
                .Add(new WeaponRuntimeComponent());
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

        return Create(
            projectiles,
            DefinitionMigrator.FromBullet(definition),
            position,
            direction,
            ownerLayer,
            ownerEntityId);
    }

    public int Create(
        ProjectileStore projectiles,
        ProjectileDefinition definition,
        Vector2 position,
        Vector2 direction,
        CollisionLayer ownerLayer,
        int ownerEntityId,
        float speedMultiplier = 1,
        float damageMultiplier = 1)
    {
        ArgumentNullException.ThrowIfNull(projectiles);
        ArgumentNullException.ThrowIfNull(definition);
        if (direction == Vector2.Zero) throw new ArgumentException("Projectile direction cannot be zero.", nameof(direction));
        if (!float.IsFinite(speedMultiplier) || speedMultiplier <= 0) throw new ArgumentOutOfRangeException(nameof(speedMultiplier));
        if (!float.IsFinite(damageMultiplier) || damageMultiplier <= 0) throw new ArgumentOutOfRangeException(nameof(damageMultiplier));

        var team = ownerLayer switch
        {
            CollisionLayer.Player => ProjectileTeam.Player,
            CollisionLayer.Enemy => ProjectileTeam.Enemy,
            _ => throw new ArgumentOutOfRangeException(
                nameof(ownerLayer),
                ownerLayer,
                "Only player or enemy entities may own projectiles.")
        };
        var behaviorDefinition = definition.Behavior;
        var behaviorFactory = _capabilities.ProjectileBehaviors.Resolve(
            behaviorDefinition.Type,
            $"projectile '{definition.Id}' behavior");
        behaviorFactory.Validate(behaviorDefinition, $"projectile '{definition.Id}' behavior");
        var behavior = behaviorFactory.Create(behaviorDefinition);
        return projectiles.QueueSpawn(new ProjectileSpawnCommand(
            ownerEntityId,
            team,
            definition.Id,
            position,
            Vector2.Normalize(direction) * definition.Speed * speedMultiplier,
            definition.HitRadius,
            Math.Max(1, (int)MathF.Round(definition.Damage * damageMultiplier)),
            definition.Lifetime,
            definition.VisualId,
            behavior.Behavior,
            behavior.HomingTurnRadiansPerSecond,
            definition.CanDamage,
            definition.CanBeCancelled,
            ParseCancelResistance(definition.CancelResistance),
            definition.PierceCount,
            ProjectileDamageType.Normal,
            ProjectileClearBehavior.Remove));
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

    private static ProjectileCancelResistance ParseCancelResistance(string value) => value switch
    {
        "soft" => ProjectileCancelResistance.Soft,
        "hard" => ProjectileCancelResistance.Hard,
        "uncancelable" => ProjectileCancelResistance.Uncancelable,
        _ => throw new ArgumentException($"Unknown projectile cancel resistance '{value}'.", nameof(value))
    };
}
