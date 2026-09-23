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
        float? invincibilitySeconds = null,
        CompiledCatalog? compiledDefinitions = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(definition);

        var shipHandle = compiledDefinitions?.ResolveShip(definition.Id).Value ?? -1;
        var normalWeaponHandles = compiledDefinitions is null
            ? Array.Empty<int>()
            : definition.NormalWeaponIds.Select(id => compiledDefinitions.ResolveWeapon(id).Value).ToArray();
        var focusWeaponHandles = compiledDefinitions is null
            ? Array.Empty<int>()
            : definition.FocusWeaponIds.Select(id => compiledDefinitions.ResolveWeapon(id).Value).ToArray();
        var bombWeaponHandle = compiledDefinitions is null || string.IsNullOrWhiteSpace(definition.BombWeaponId)
            ? -1
            : compiledDefinitions.ResolveWeapon(definition.BombWeaponId).Value;
        var specialWeaponHandle = compiledDefinitions is null || string.IsNullOrWhiteSpace(definition.SpecialWeaponId)
            ? -1
            : compiledDefinitions.ResolveWeapon(definition.SpecialWeaponId).Value;
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
                definition.VisualId,
                shipHandle,
                normalWeaponHandles,
                focusWeaponHandles,
                bombWeaponHandle,
                specialWeaponHandle))
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
            entity.Add(new WeaponHolderComponent(
                definition.NormalWeaponIds[0],
                normalWeaponHandles.Length > 0 ? normalWeaponHandles[0] : -1));
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
                    option.VisualId,
                    compiledDefinitions is null
                        ? null
                        : option.NormalWeaponIds.Select(id => compiledDefinitions.ResolveWeapon(id).Value).ToArray(),
                    compiledDefinitions is null
                        ? null
                        : option.FocusWeaponIds.Select(id => compiledDefinitions.ResolveWeapon(id).Value).ToArray()))
                .Add(new WeaponRuntimeComponent());
        }

        return entity;
    }
}

public sealed class EnemyFactory
{
    private readonly RuntimeCapabilityRegistry _capabilities;
    private readonly RunModifierState? _modifiers;

    public EnemyFactory(RuntimeCapabilityRegistry? capabilities = null, RunModifierState? modifiers = null)
    {
        _capabilities = capabilities ?? RuntimeCapabilityRegistry.CreateBuiltIn();
        _modifiers = modifiers;
    }

    public Entity Create(
        World world,
        EnemyDefinition definition,
        Vector2 position,
        bool isBoss = false,
        BossDefinition? boss = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(definition);
        definition = DefinitionMigrator.Migrate(definition);
        return CreateCore(world, definition, position, isBoss, boss, null, null, null, null);
    }

    public Entity Create(
        World world,
        CompiledCatalog definitions,
        EnemyHandle handle,
        Vector2 position,
        bool isBoss = false,
        BossHandle? bossHandle = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var boss = bossHandle is { } resolvedBoss ? definitions.Get(resolvedBoss) : null;
        var actor = definitions.Get(bossHandle is { } selectedBoss
            ? definitions.GetCompiled(selectedBoss).ActorHandle
            : definitions.ResolveActor(definitions.Get(handle).Definition.Id));
        var compiled = boss is null ? definitions.Get(handle) : definitions.Get(actor.EnemyHandle);
        var entity = CreateCore(
            world, compiled.Definition, position, isBoss, boss, compiled, definitions, bossHandle, actor);
        if (boss is null && compiled.AttackPatternHandles.Count > 0 &&
            entity.Get<AttackTimelineComponent>().CompiledPatterns is null)
            throw new InvalidOperationException($"Enemy '{compiled.Definition.Id}' attack patterns were not compiled.");
        return entity;
    }

    public Entity CreateActor(
        World world,
        CompiledCatalog definitions,
        ActorHandle actorHandle,
        Vector2 position,
        bool isBoss = false,
        BossHandle? bossHandle = null)
    {
        var actor = definitions.Get(actorHandle);
        var enemy = definitions.Get(actor.EnemyHandle);
        var boss = bossHandle is { } resolvedBoss ? definitions.Get(resolvedBoss) : null;
        return CreateCore(
            world, enemy.Definition, position, isBoss, boss, enemy, definitions, bossHandle, actor);
    }

    private Entity CreateCore(
        World world,
        EnemyDefinition definition,
        Vector2 position,
        bool isBoss,
        BossDefinition? boss,
        CompiledEnemyDefinition? compiled,
        CompiledCatalog? compiledDefinitions,
        BossHandle? bossHandle,
        CompiledActorDefinition? actor)
    {
        var entity = world.CreateEntity()
            .Add(new TransformComponent(position))
            .Add(new VelocityComponent(boss is null && string.IsNullOrWhiteSpace(definition.MotionPatternId)
                ? new Vector2(0, definition.Speed)
                : Vector2.Zero))
            .Add(new HealthComponent(Math.Max(1, (int)MathF.Round(
                definition.Hp * (_modifiers?.EnemyHealthMultiplier ?? 1)))))
            .Add(new ColliderComponent(definition.Radius, CollisionLayer.Enemy))
            .Add(new EnemyComponent(definition.Id, compiled?.Handle.Value ?? -1))
            .Add(new ScoreValueComponent(definition.Score))
            .Add(new ActorRootComponent(actor?.Definition.Id ?? definition.Id, actor?.Handle.Value ?? -1, actor?.TagMask ?? 0))
            .Add(new LockTargetComponent());

        if (boss is not null)
        {
            entity.Add(new BossComponent(boss.Id, boss.DisplayName, bossHandle?.Value ?? -1));
        }
        else if (isBoss)
        {
            entity.Add(new BossComponent());
        }

        if (!string.IsNullOrWhiteSpace(definition.WeaponId))
            entity.Add(new WeaponHolderComponent(definition.WeaponId, compiled?.WeaponHandle?.Value ?? -1));

        if (boss is null && !string.IsNullOrWhiteSpace(definition.MotionPatternId))
        {
            var commands = compiled is not null && compiledDefinitions is not null && compiled.MotionPatternHandle is { } patternHandle
                ? compiledDefinitions.Get(patternHandle).Commands
                : null;
            entity.Add(new MotionTimelineComponent(definition.MotionPatternId, commands));
        }

        if (boss is null && definition.AttackPatternIds.Count > 0)
        {
            var patterns = compiled is not null && compiledDefinitions is not null
                ? compiled.AttackPatternHandles.Select(compiledDefinitions.Get).ToArray()
                : null;
            entity.Add(new AttackTimelineComponent(definition.AttackPatternIds, patterns));
        }

        if (boss is null && string.IsNullOrWhiteSpace(definition.MotionPatternId))
        {
            var motion = definition.Motion!;
            var factory = compiled?.Motion ??
                _capabilities.ActorMotions.Resolve(motion.Type, $"enemy '{definition.Id}' motion");
            if (compiled is null) factory.Validate(motion, $"enemy '{definition.Id}' motion");
            factory.Apply(entity, position, motion);
        }


        if (actor is not null && compiledDefinitions is not null)
        {
            CreateParts(world, entity, actor);
        }

        return entity;
    }

    private void CreateParts(World world, Entity root, CompiledActorDefinition actor)
    {
        var entities = new Entity[actor.Parts.Count];
        foreach (var part in actor.Parts)
        {
            var parent = part.ParentHandle < 0 ? root : entities[part.ParentHandle];
            var parentTransform = parent.Get<TransformComponent>();
            var parentRotation = parent.TryGet<RotationComponent>(out var rotation) ? rotation.Degrees : 0;
            var offset = Rotate(part.Definition.OffsetX, part.Definition.OffsetY, parentRotation);
            var entity = world.CreateEntity()
                .Add(new TransformComponent(parentTransform.Position + offset))
                .Add(new RotationComponent(parentRotation + part.Definition.RotationDegrees))
                .Add(new ActorPartComponent(
                    root.Id,
                    parent.Id,
                    part.Handle,
                    part.Definition.Id,
                    new Vector2(part.Definition.OffsetX, part.Definition.OffsetY),
                    part.Definition.RotationDegrees,
                    ParseHealthPolicy(part.Definition.HealthPolicy),
                    part.Definition.DamageForwardingRatio,
                    part.Definition.Targetable,
                    part.Definition.LockCapacity,
                    part.Definition.Enabled,
                    part.TagMask,
                    part.Definition.InteractionClass,
                    part.Definition.DestroySignal,
                    part.Definition.DetachSignal))
                .Add(new LockTargetComponent(part.Definition.Targetable, part.Definition.LockCapacity));
            if (part.Definition.VisualId is not null || part.Definition.AnimationId is not null ||
                part.Definition.AnimationStateId is not null)
                entity.Add(new ActorPresentationComponent(
                    part.Definition.VisualId,
                    part.Definition.AnimationId,
                    part.Definition.AnimationStateId,
                    part.Definition.RenderLayer));
            if (part.Hurtboxes.Count > 0)
            {
                var shapes = part.Hurtboxes.Select(hurtbox => new HurtboxShapeData(
                    hurtbox.Definition.Id,
                    ParseShape(hurtbox.Definition.Shape),
                    new Vector2(hurtbox.Definition.OffsetX, hurtbox.Definition.OffsetY),
                    hurtbox.Definition.Radius,
                    hurtbox.Definition.Width,
                    hurtbox.Definition.Height,
                    hurtbox.Definition.Length,
                    hurtbox.Definition.RotationDegrees,
                    hurtbox.BoundingRadius)).ToArray();
                entity.Add(new HurtboxSetComponent(shapes));
                entity.Add(new ColliderComponent(
                    Math.Max(float.Epsilon, entity.Get<HurtboxSetComponent>().MaximumBoundingRadius),
                    CollisionLayer.Enemy));
            }
            if (part.Hardpoints.Count > 0)
            {
                entity.Add(new HardpointSetComponent(part.Hardpoints.Select(hardpoint => new HardpointData(
                    hardpoint.Definition.Id,
                    new Vector2(hardpoint.Definition.OffsetX, hardpoint.Definition.OffsetY),
                    hardpoint.Definition.RotationDegrees,
                    hardpoint.Definition.WeaponId,
                    hardpoint.WeaponHandle?.Value ?? -1)).ToArray()));
                entity.Add(new WeaponRuntimeComponent());
                foreach (var hardpoint in part.Hardpoints.Where(static hardpoint => hardpoint.WeaponHandle is not null))
                {
                    var mountOffset = Rotate(
                        hardpoint.Definition.OffsetX,
                        hardpoint.Definition.OffsetY,
                        parentRotation + part.Definition.RotationDegrees);
                    world.CreateEntity()
                        .Add(new TransformComponent(entity.Get<TransformComponent>().Position + mountOffset))
                        .Add(new RotationComponent(
                            parentRotation + part.Definition.RotationDegrees + hardpoint.Definition.RotationDegrees))
                        .Add(new ParentTransformComponent(
                            root.Id,
                            entity.Id,
                            new Vector2(hardpoint.Definition.OffsetX, hardpoint.Definition.OffsetY),
                            hardpoint.Definition.RotationDegrees))
                        .Add(new WeaponHolderComponent(
                            hardpoint.Definition.WeaponId!, hardpoint.WeaponHandle!.Value.Value))
                        .Add(new WeaponRuntimeComponent());
                }
            }
            if (part.Definition.HealthPolicy == "independent")
                entity.Add(new HealthComponent(Math.Max(1, (int)MathF.Round(
                    part.Definition.MaximumHealth!.Value * (_modifiers?.EnemyHealthMultiplier ?? 1)))));
            entities[part.Handle] = entity;
        }
    }

    private static ActorPartHealthPolicy ParseHealthPolicy(string value) => value switch
    {
        "shared" => ActorPartHealthPolicy.Shared,
        "independent" => ActorPartHealthPolicy.Independent,
        "indestructible" => ActorPartHealthPolicy.Indestructible,
        _ => throw new InvalidOperationException($"Unknown actor part health policy '{value}'.")
    };

    private static HurtboxShape ParseShape(string value) => value switch
    {
        "circle" => HurtboxShape.Circle,
        "capsule" => HurtboxShape.Capsule,
        "aabb" => HurtboxShape.Aabb,
        "obb" => HurtboxShape.Obb,
        _ => throw new InvalidOperationException($"Unknown hurtbox shape '{value}'.")
    };

    private static Vector2 Rotate(float x, float y, float degrees)
    {
        var radians = degrees * MathF.PI / 180;
        var cosine = MathF.Cos(radians);
        var sine = MathF.Sin(radians);
        return new Vector2((x * cosine) - (y * sine), (x * sine) + (y * cosine));
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
        float damageMultiplier = 1,
        float accelerationPerSecond = 0,
        int targetEntityId = 0,
        int interactionPowerBonus = 0)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var behaviorDefinition = definition.Behavior;
        var behaviorFactory = _capabilities.ProjectileBehaviors.Resolve(
            behaviorDefinition.Type,
            $"projectile '{definition.Id}' behavior");
        behaviorFactory.Validate(behaviorDefinition, $"projectile '{definition.Id}' behavior");
        var behavior = behaviorFactory.Create(behaviorDefinition);
        return CreateCore(
            projectiles,
            definition,
            behavior,
            position,
            direction,
            ownerLayer,
            ownerEntityId,
            speedMultiplier,
            damageMultiplier,
            accelerationPerSecond,
            targetEntityId,
            null,
            0,
            checked(definition.InteractionPower + interactionPowerBonus),
            definition.InteractionResistance);
    }

    public int Create(
        ProjectileStore projectiles,
        CompiledProjectileDefinition definition,
        Vector2 position,
        Vector2 direction,
        CollisionLayer ownerLayer,
        int ownerEntityId,
        float speedMultiplier = 1,
        float damageMultiplier = 1,
        float accelerationPerSecond = 0,
        int targetEntityId = 0,
        int interactionPowerBonus = 0)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return CreateCore(
            projectiles,
            definition.Definition,
            definition.Behavior,
            position,
            direction,
            ownerLayer,
            ownerEntityId,
            speedMultiplier,
            damageMultiplier,
            accelerationPerSecond,
            targetEntityId,
            definition.Handle,
            definition.TagMask,
            checked(definition.InteractionPower + interactionPowerBonus),
            definition.InteractionResistance);
    }

    private static int CreateCore(
        ProjectileStore projectiles,
        ProjectileDefinition definition,
        ProjectileBehaviorConfiguration behavior,
        Vector2 position,
        Vector2 direction,
        CollisionLayer ownerLayer,
        int ownerEntityId,
        float speedMultiplier,
        float damageMultiplier,
        float accelerationPerSecond,
        int targetEntityId,
        ProjectileHandle? definitionHandle,
        ulong tagMask,
        int interactionPower,
        int interactionResistance)
    {
        ArgumentNullException.ThrowIfNull(projectiles);
        if (direction == Vector2.Zero) throw new ArgumentException("Projectile direction cannot be zero.", nameof(direction));
        if (!float.IsFinite(speedMultiplier) || speedMultiplier <= 0) throw new ArgumentOutOfRangeException(nameof(speedMultiplier));
        if (!float.IsFinite(damageMultiplier) || damageMultiplier <= 0) throw new ArgumentOutOfRangeException(nameof(damageMultiplier));
        if (!float.IsFinite(accelerationPerSecond)) throw new ArgumentOutOfRangeException(nameof(accelerationPerSecond));
        var team = ownerLayer switch
        {
            CollisionLayer.Player => ProjectileTeam.Player,
            CollisionLayer.Enemy => ProjectileTeam.Enemy,
            _ => throw new ArgumentOutOfRangeException(
                nameof(ownerLayer),
                ownerLayer,
                "Only player or enemy entities may own projectiles.")
        };
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
            ProjectileClearBehavior.Remove,
            accelerationPerSecond,
            targetEntityId,
            definitionHandle,
            TagMask: tagMask,
            InteractionPower: interactionPower,
            InteractionResistance: interactionResistance));
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
