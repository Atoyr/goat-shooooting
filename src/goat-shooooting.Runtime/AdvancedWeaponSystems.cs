using System.Numerics;
using GoatShooooting.Core;
using GoatShooooting.Definitions;

namespace GoatShooooting.Runtime;

public sealed class OptionFollowSystem
{
    public void Update(World world, float deltaTime)
    {
        ArgumentNullException.ThrowIfNull(world);
        foreach (var optionEntity in world.Query<OptionUnitComponent, TransformComponent>())
        {
            var option = optionEntity.Get<OptionUnitComponent>();
            var owner = FindEntity(world, option.OwnerEntityId);
            if (owner is null || owner.Has<PendingDestroyComponent>() ||
                !owner.TryGet<TransformComponent>(out var ownerTransform))
            {
                optionEntity.Add(new PendingDestroyComponent());
                continue;
            }

            var transform = optionEntity.Get<TransformComponent>();
            var target = ownerTransform.Position + option.Offset;
            var distance = target - transform.Position;
            var maximumDistance = option.FollowSpeed * deltaTime;
            transform.Position = distance.LengthSquared() <= maximumDistance * maximumDistance
                ? target
                : transform.Position + (Vector2.Normalize(distance) * maximumDistance);
        }
    }

    internal static Entity? FindEntity(World world, int id)
    {
        foreach (var entity in world.Entities)
        {
            if (entity.Id == id) return entity;
        }

        return null;
    }
}

public sealed class AdvancedWeaponSystem
{
    private readonly BulletFactory _bulletFactory;
    private readonly RuntimeCapabilityRegistry _capabilities;
    private readonly IRandomSource _randomSource;
    private readonly string? _difficultyId;

    public AdvancedWeaponSystem(
        BulletFactory bulletFactory,
        RuntimeCapabilityRegistry capabilities,
        IRandomSource? randomSource = null,
        string? difficultyId = null)
    {
        _bulletFactory = bulletFactory ?? throw new ArgumentNullException(nameof(bulletFactory));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _randomSource = randomSource ?? new SeededRandomSource(0);
        _difficultyId = difficultyId;
    }

    public void Update(
        World world,
        DefinitionCatalog definitions,
        IInputState input,
        float deltaTime,
        SimulationTelemetry telemetry,
        ProjectileStore projectiles)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(projectiles);

        foreach (var player in world.Query<PlayerComponent, ShipComponent, TransformComponent>().ToArray())
        {
            if (player.Has<PendingDestroyComponent>()) continue;
            if (player.TryGet<PlayerLifeCycleComponent>(out var lifeCycle) && !lifeCycle.CanAct) continue;
            var ship = player.Get<ShipComponent>();
            var weaponIds = ship.IsFocused ? ship.FocusWeaponIds : ship.NormalWeaponIds;
            ProcessWeapons(
                world,
                definitions,
                player,
                CollisionLayer.Player,
                weaponIds,
                input.Fire,
                deltaTime,
                ship.Power,
                telemetry,
                projectiles);
            if (!string.IsNullOrWhiteSpace(ship.SpecialWeaponId))
            {
                ProcessWeapons(
                    world,
                    definitions,
                    player,
                    CollisionLayer.Player,
                    new[] { ship.SpecialWeaponId },
                    input.Special,
                    deltaTime,
                    ship.Power,
                    telemetry,
                    projectiles);
            }

            UpdateInactiveContinuousWeapons(
                world,
                definitions,
                player,
                CollisionLayer.Player,
                ship.NormalWeaponIds.Concat(ship.FocusWeaponIds)
                    .Append(ship.SpecialWeaponId)
                    .OfType<string>(),
                weaponIds.Concat(ship.SpecialWeaponId is not null
                    ? new[] { ship.SpecialWeaponId }
                    : Array.Empty<string>()),
                deltaTime,
                ship.Power,
                telemetry,
                projectiles);
        }

        foreach (var optionEntity in world.Query<OptionUnitComponent, TransformComponent>().ToArray())
        {
            if (optionEntity.Has<PendingDestroyComponent>()) continue;
            var option = optionEntity.Get<OptionUnitComponent>();
            var owner = OptionFollowSystem.FindEntity(world, option.OwnerEntityId);
            if (owner is null || !owner.TryGet<ShipComponent>(out var ship)) continue;
            if (owner.TryGet<PlayerLifeCycleComponent>(out var lifeCycle) && !lifeCycle.CanAct) continue;
            ProcessWeapons(
                world,
                definitions,
                optionEntity,
                CollisionLayer.Player,
                ship.IsFocused ? option.FocusWeaponIds : option.NormalWeaponIds,
                input.Fire,
                deltaTime,
                ship.Power,
                telemetry,
                projectiles);
            UpdateInactiveContinuousWeapons(
                world,
                definitions,
                optionEntity,
                CollisionLayer.Player,
                option.NormalWeaponIds.Concat(option.FocusWeaponIds),
                ship.IsFocused ? option.FocusWeaponIds : option.NormalWeaponIds,
                deltaTime,
                ship.Power,
                telemetry,
                projectiles);
        }

        foreach (var enemy in world.Query<EnemyComponent, WeaponHolderComponent, TransformComponent>().ToArray())
        {
            if (enemy.Has<PendingDestroyComponent>() || enemy.Has<AttackTimelineComponent>() ||
                enemy.TryGet<BossComponent>(out var boss) && boss.IsManaged) continue;
            ProcessWeapons(
                world,
                definitions,
                enemy,
                CollisionLayer.Enemy,
                new[] { enemy.Get<WeaponHolderComponent>().WeaponId },
                wantsToFire: true,
                deltaTime,
                power: 0,
                telemetry,
                projectiles);
        }
    }

    public void FireOnce(
        World world,
        DefinitionCatalog definitions,
        Entity owner,
        string weaponId,
        SimulationTelemetry telemetry,
        ProjectileStore projectiles)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(projectiles);
        var weapon = definitions.GetWeapon(weaponId);
        if (weapon.ActionType != "projectile")
        {
            throw new InvalidOperationException($"Timeline fire requires a projectile weapon, but '{weaponId}' is '{weapon.ActionType}'.");
        }

        var runtime = owner.TryGet<WeaponRuntimeComponent>(out var existing)
            ? existing
            : owner.Add(new WeaponRuntimeComponent()).Get<WeaponRuntimeComponent>();
        foreach (var emitter in weapon.Emitters)
        {
            if (!IsEnabled(emitter.DifficultyTags)) continue;
            var state = runtime.GetOrCreate($"{weapon.Id}/{emitter.Id}");
            FireEmitter(world, definitions, owner, CollisionLayer.Enemy, weapon, emitter, state, 0, telemetry, projectiles);
        }
    }

    private void UpdateInactiveContinuousWeapons(
        World world,
        DefinitionCatalog definitions,
        Entity owner,
        CollisionLayer ownerLayer,
        IEnumerable<string> configuredWeaponIds,
        IEnumerable<string> activeWeaponIds,
        float deltaTime,
        int power,
        SimulationTelemetry telemetry,
        ProjectileStore projectiles)
    {
        var active = activeWeaponIds.ToHashSet(StringComparer.Ordinal);
        var runtime = owner.Get<WeaponRuntimeComponent>();
        foreach (var weaponId in configuredWeaponIds.Distinct(StringComparer.Ordinal))
        {
            if (active.Contains(weaponId)) continue;
            var weapon = definitions.GetWeapon(weaponId);
            if (weapon.ActionType == "laser")
            {
                UpdateLaserWeapon(world, owner, ownerLayer, weapon, runtime, wantsToFire: false);
            }
            else if (weapon.ActionType == "lock-on")
            {
                UpdateLockOnWeapon(
                    world,
                    definitions,
                    owner,
                    ownerLayer,
                    weapon,
                    runtime,
                    wantsToFire: false,
                    deltaTime,
                    power,
                    telemetry,
                    projectiles);
            }
        }
    }

    private void ProcessWeapons(
        World world,
        DefinitionCatalog definitions,
        Entity owner,
        CollisionLayer ownerLayer,
        IReadOnlyList<string> weaponIds,
        bool wantsToFire,
        float deltaTime,
        int power,
        SimulationTelemetry telemetry,
        ProjectileStore projectiles)
    {
        var runtime = owner.TryGet<WeaponRuntimeComponent>(out var existing)
            ? existing
            : owner.Add(new WeaponRuntimeComponent()).Get<WeaponRuntimeComponent>();
        foreach (var weaponId in weaponIds.Distinct(StringComparer.Ordinal))
        {
            var weapon = definitions.GetWeapon(weaponId);
            switch (weapon.ActionType)
            {
                case "projectile":
                    UpdateProjectileWeapon(
                        world,
                        definitions,
                        owner,
                        ownerLayer,
                        weapon,
                        runtime,
                        wantsToFire,
                        deltaTime,
                        power,
                        telemetry,
                        projectiles);
                    break;
                case "laser":
                    UpdateLaserWeapon(world, owner, ownerLayer, weapon, runtime, wantsToFire);
                    break;
                case "lock-on":
                    UpdateLockOnWeapon(
                        world,
                        definitions,
                        owner,
                        ownerLayer,
                        weapon,
                        runtime,
                        wantsToFire,
                        deltaTime,
                        power,
                        telemetry,
                        projectiles);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported weapon action '{weapon.ActionType}'.");
            }
        }
    }

    private void UpdateProjectileWeapon(
        World world,
        DefinitionCatalog definitions,
        Entity owner,
        CollisionLayer ownerLayer,
        WeaponDefinition weapon,
        WeaponRuntimeComponent runtime,
        bool wantsToFire,
        float deltaTime,
        int power,
        SimulationTelemetry telemetry,
        ProjectileStore projectiles)
    {
        foreach (var emitter in weapon.Emitters)
        {
            if (!IsEnabled(emitter.DifficultyTags)) continue;
            var state = runtime.GetOrCreate($"{weapon.Id}/{emitter.Id}");
            state.CooldownRemaining = Math.Max(0, state.CooldownRemaining - deltaTime);
            state.BurstCooldownRemaining = Math.Max(0, state.BurstCooldownRemaining - deltaTime);
            if (state.BurstShotsRemaining > 0 && state.BurstCooldownRemaining <= 0)
            {
                FireEmitter(world, definitions, owner, ownerLayer, weapon, emitter, state, power, telemetry, projectiles);
                state.BurstShotsRemaining--;
                if (state.BurstShotsRemaining > 0)
                {
                    state.BurstCooldownRemaining = emitter.BurstInterval;
                }
                else
                {
                    state.CooldownRemaining = emitter.FireInterval;
                }

                continue;
            }

            if (!wantsToFire || state.BurstShotsRemaining > 0 || state.CooldownRemaining > 0) continue;
            FireEmitter(world, definitions, owner, ownerLayer, weapon, emitter, state, power, telemetry, projectiles);
            state.BurstShotsRemaining = emitter.BurstCount - 1;
            if (state.BurstShotsRemaining > 0)
            {
                state.BurstCooldownRemaining = emitter.BurstInterval;
            }
            else
            {
                state.CooldownRemaining = emitter.FireInterval;
            }
        }
    }

    private void UpdateLaserWeapon(
        World world,
        Entity owner,
        CollisionLayer ownerLayer,
        WeaponDefinition weapon,
        WeaponRuntimeComponent runtime,
        bool wantsToFire)
    {
        var settings = weapon.Laser!;
        var state = runtime.GetOrCreate(weapon.Id);
        var existing = state.ActiveLaserEntityId == 0
            ? null
            : OptionFollowSystem.FindEntity(world, state.ActiveLaserEntityId);
        if (!wantsToFire)
        {
            if (existing is not null) world.DestroyEntity(existing);
            state.ActiveLaserEntityId = 0;
            return;
        }

        var direction = ownerLayer == CollisionLayer.Player ? -Vector2.UnitY : Vector2.UnitY;
        if (existing is null)
        {
            existing = world.CreateEntity()
                .Add(new TransformComponent(owner.Get<TransformComponent>().Position))
                .Add(new LaserComponent(
                    owner.Id,
                    ownerLayer,
                    direction,
                    settings.Length,
                    settings.Width,
                    settings.Damage,
                    settings.DamageInterval,
                    settings.VisualId,
                    settings.ProjectileInteraction));
            state.ActiveLaserEntityId = existing.Id;
        }

        existing.Get<TransformComponent>().Position = owner.Get<TransformComponent>().Position;
        existing.Get<LaserComponent>().Direction = direction;
    }

    private void UpdateLockOnWeapon(
        World world,
        DefinitionCatalog definitions,
        Entity owner,
        CollisionLayer ownerLayer,
        WeaponDefinition weapon,
        WeaponRuntimeComponent runtime,
        bool wantsToFire,
        float deltaTime,
        int power,
        SimulationTelemetry telemetry,
        ProjectileStore projectiles)
    {
        var settings = weapon.LockOn!;
        var state = runtime.GetOrCreate(weapon.Id);
        state.CooldownRemaining = Math.Max(0, state.CooldownRemaining - deltaTime);
        RemoveInvalidTargets(world, state);
        if (wantsToFire)
        {
            AcquireTargets(world, owner, ownerLayer, settings, state);
        }
        else if (state.WasHeld && state.CooldownRemaining <= 0)
        {
            foreach (var targetId in state.LockedTargetEntityIds)
            {
                var target = OptionFollowSystem.FindEntity(world, targetId);
                if (target is null || target.Has<PendingDestroyComponent>()) continue;
                var direction = target.Get<TransformComponent>().Position - owner.Get<TransformComponent>().Position;
                if (direction == Vector2.Zero) direction = ownerLayer == CollisionLayer.Player ? -Vector2.UnitY : Vector2.UnitY;
                foreach (var emitter in weapon.Emitters)
                {
                    FireEmitter(
                        world,
                        definitions,
                        owner,
                        ownerLayer,
                        weapon,
                        emitter,
                        state,
                        power,
                        telemetry,
                        projectiles,
                        direction);
                }
            }

            state.LockedTargetEntityIds.Clear();
            state.CooldownRemaining = weapon.Emitters.Max(static emitter => emitter.FireInterval);
        }

        state.WasHeld = wantsToFire;
    }

    private void FireEmitter(
        World world,
        DefinitionCatalog definitions,
        Entity owner,
        CollisionLayer ownerLayer,
        WeaponDefinition weapon,
        EmitterDefinition emitter,
        WeaponActionState state,
        int power,
        SimulationTelemetry telemetry,
        ProjectileStore projectiles,
        Vector2? forcedDirection = null)
    {
        var position = owner.Get<TransformComponent>().Position + new Vector2(emitter.OffsetX, emitter.OffsetY);
        var baseDirection = forcedDirection ?? ResolveAngleSource(world, owner, ownerLayer, emitter, state);
        var powerModifier = ResolvePowerModifier(definitions, world, owner, power);
        var count = emitter.ProjectileCount + powerModifier.AdditionalProjectileCount;
        foreach (var direction in GetDirections(weapon, emitter, state, baseDirection, count))
        {
            foreach (var speedMultiplier in GetSpeedMultipliers(emitter))
            {
                _bulletFactory.Create(
                    projectiles,
                    definitions.GetProjectile(emitter.ProjectileId),
                    position,
                    direction,
                    ownerLayer,
                    owner.Id,
                    speedMultiplier,
                    powerModifier.DamageMultiplier,
                    emitter.SpeedMode is "accelerating" or "decelerating"
                        ? emitter.AccelerationPerSecond
                        : 0);
                telemetry.BulletsSpawned++;
                if (ownerLayer == CollisionLayer.Enemy) telemetry.EnemyBulletsSpawned++;
            }
        }

        if (emitter.AngleSource == "rotating")
        {
            state.PatternAngleDegrees = NormalizeDegrees(
                state.PatternAngleDegrees + emitter.RotationDegreesPerShot);
        }
    }

    private IEnumerable<Vector2> GetDirections(
        WeaponDefinition weapon,
        EmitterDefinition emitter,
        WeaponActionState state,
        Vector2 baseDirection,
        int projectileCount)
    {
        if (emitter.UsesLegacyPattern)
        {
            var holder = new WeaponHolderComponent(weapon.Id)
            {
                PatternAngleDegrees = state.PatternAngleDegrees,
                PatternDirection = state.PatternDirection,
                ShotsSinceDirectionChange = state.ShotsSinceDirectionChange
            };
            var factory = _capabilities.FirePatterns.Resolve(weapon.Pattern!.Type, $"weapon '{weapon.Id}' pattern");
            foreach (var direction in factory.GetDirections(weapon.Pattern, holder, baseDirection)) yield return direction;
            factory.Advance(weapon.Pattern, holder);
            state.PatternAngleDegrees = holder.PatternAngleDegrees;
            state.PatternDirection = holder.PatternDirection;
            state.ShotsSinceDirectionChange = holder.ShotsSinceDirectionChange;
            yield break;
        }

        switch (emitter.Distribution)
        {
            case "single":
                yield return baseDirection;
                break;
            case "fan":
            case "arc":
            case "layers":
                for (var index = 0; index < projectileCount; index++)
                {
                    var offset = projectileCount == 1 ? 0 : ((float)index / (projectileCount - 1)) - 0.5f;
                    yield return RotateDegrees(baseDirection, offset * emitter.SpreadDegrees);
                }

                break;
            case "random-arc":
                for (var index = 0; index < projectileCount; index++)
                {
                    var offset = (_randomSource.NextSingle() - 0.5f) * emitter.SpreadDegrees;
                    yield return RotateDegrees(baseDirection, offset);
                }

                break;
            case "ring":
                for (var index = 0; index < projectileCount; index++)
                {
                    yield return RotateDegrees(baseDirection, index * (360f / projectileCount));
                }

                break;
            default:
                throw new InvalidOperationException($"Unsupported emitter distribution '{emitter.Distribution}'.");
        }
    }

    private static Vector2 ResolveAngleSource(
        World world,
        Entity owner,
        CollisionLayer ownerLayer,
        EmitterDefinition emitter,
        WeaponActionState state)
    {
        var forward = ownerLayer == CollisionLayer.Player ? -Vector2.UnitY : Vector2.UnitY;
        return emitter.AngleSource switch
        {
            "forward" => forward,
            "fixed" => RotateDegrees(forward, emitter.FixedAngleDegrees),
            "rotating" => RotateDegrees(forward, state.PatternAngleDegrees),
            "aim-at-target" => FindAimDirection(world, owner, ownerLayer) ?? forward,
            "aim-at-player" => FindPlayerDirection(world, owner) ?? forward,
            "current-heading" => owner.TryGet<VelocityComponent>(out var velocity) && velocity.Value != Vector2.Zero
                ? Vector2.Normalize(velocity.Value)
                : forward,
            _ => throw new InvalidOperationException($"Unsupported angle source '{emitter.AngleSource}'.")
        };
    }

    private static Vector2? FindPlayerDirection(World world, Entity owner)
    {
        var origin = owner.Get<TransformComponent>().Position;
        var player = world.Query<PlayerComponent, TransformComponent>()
            .Where(static candidate => !candidate.Has<PendingDestroyComponent>())
            .OrderBy(candidate => Vector2.DistanceSquared(origin, candidate.Get<TransformComponent>().Position))
            .ThenBy(static candidate => candidate.Id)
            .FirstOrDefault();
        if (player is null) return null;
        var direction = player.Get<TransformComponent>().Position - origin;
        return direction == Vector2.Zero ? null : Vector2.Normalize(direction);
    }

    private IEnumerable<float> GetSpeedMultipliers(EmitterDefinition emitter)
    {
        if (emitter.SpeedMode != "range") return emitter.SpeedMultipliers;
        if (emitter.SpeedLayerCount == 1) return new[] { emitter.MinimumSpeedMultiplier };
        return Enumerable.Range(0, emitter.SpeedLayerCount)
            .Select(index => emitter.MinimumSpeedMultiplier +
                ((emitter.MaximumSpeedMultiplier - emitter.MinimumSpeedMultiplier) * index / (emitter.SpeedLayerCount - 1f)));
    }

    private bool IsEnabled(IReadOnlyList<string> difficultyTags) =>
        difficultyTags.Count == 0 || _difficultyId is not null && difficultyTags.Contains(_difficultyId, StringComparer.Ordinal);

    private static Vector2? FindAimDirection(World world, Entity owner, CollisionLayer ownerLayer)
    {
        var targetLayer = ownerLayer == CollisionLayer.Player ? CollisionLayer.Enemy : CollisionLayer.Player;
        var origin = owner.Get<TransformComponent>().Position;
        Entity? nearest = null;
        var nearestDistance = float.MaxValue;
        foreach (var candidate in world.Entities)
        {
            if (candidate.Has<PendingDestroyComponent>() ||
                !candidate.TryGet<ColliderComponent>(out var collider) || collider.Layer != targetLayer ||
                !candidate.TryGet<TransformComponent>(out var transform)) continue;
            var distance = Vector2.DistanceSquared(origin, transform.Position);
            if (distance < nearestDistance || distance == nearestDistance && candidate.Id < nearest!.Id)
            {
                nearest = candidate;
                nearestDistance = distance;
            }
        }

        if (nearest is null) return null;
        var direction = nearest.Get<TransformComponent>().Position - origin;
        return direction == Vector2.Zero ? null : Vector2.Normalize(direction);
    }

    private static PowerLevelModifierDefinition ResolvePowerModifier(
        DefinitionCatalog definitions,
        World world,
        Entity owner,
        int power)
    {
        if (!owner.TryGet<ShipComponent>(out var ship))
        {
            if (!owner.TryGet<OptionUnitComponent>(out var option)) return new PowerLevelModifierDefinition();
            var player = OptionFollowSystem.FindEntity(world, option.OwnerEntityId);
            if (player is null || !player.TryGet<ShipComponent>(out ship)) return new PowerLevelModifierDefinition();
        }

        var definition = definitions.GetShip(ship.DefinitionId);
        var result = new PowerLevelModifierDefinition();
        foreach (var modifier in definition.PowerLevels)
        {
            if (modifier.MinimumPower > power) break;
            result = modifier;
        }

        return result;
    }

    private static void AcquireTargets(
        World world,
        Entity owner,
        CollisionLayer ownerLayer,
        LockOnWeaponDefinition settings,
        WeaponActionState state)
    {
        var targetLayer = ownerLayer == CollisionLayer.Player ? CollisionLayer.Enemy : CollisionLayer.Player;
        var origin = owner.Get<TransformComponent>().Position;
        var rangeSquared = settings.Range * settings.Range;
        var candidates = new List<(Entity Entity, float Distance)>();
        foreach (var candidate in world.Entities)
        {
            if (candidate.Has<PendingDestroyComponent>() ||
                !candidate.TryGet<ColliderComponent>(out var collider) || collider.Layer != targetLayer ||
                !candidate.TryGet<TransformComponent>(out var transform)) continue;
            var distance = Vector2.DistanceSquared(origin, transform.Position);
            if (distance <= rangeSquared) candidates.Add((candidate, distance));
        }

        candidates.Sort(static (left, right) =>
        {
            var distance = left.Distance.CompareTo(right.Distance);
            return distance != 0 ? distance : left.Entity.Id.CompareTo(right.Entity.Id);
        });
        state.LockedTargetEntityIds.Clear();
        for (var index = 0; index < candidates.Count && index < settings.MaximumTargets; index++)
        {
            state.LockedTargetEntityIds.Add(candidates[index].Entity.Id);
        }
    }

    private static void RemoveInvalidTargets(World world, WeaponActionState state)
    {
        for (var index = state.LockedTargetEntityIds.Count - 1; index >= 0; index--)
        {
            var target = OptionFollowSystem.FindEntity(world, state.LockedTargetEntityIds[index]);
            if (target is null || target.Has<PendingDestroyComponent>()) state.LockedTargetEntityIds.RemoveAt(index);
        }
    }

    private static Vector2 RotateDegrees(Vector2 vector, float degrees)
    {
        var radians = degrees * (MathF.PI / 180);
        var cosine = MathF.Cos(radians);
        var sine = MathF.Sin(radians);
        return new Vector2((vector.X * cosine) - (vector.Y * sine), (vector.X * sine) + (vector.Y * cosine));
    }

    private static float NormalizeDegrees(float angle)
    {
        angle %= 360;
        return angle < 0 ? angle + 360 : angle;
    }
}

public sealed class LaserSystem
{
    private readonly List<DamageEvent> _damageEvents = new();

    public IReadOnlyList<DamageEvent> Update(
        World world,
        ProjectileStore projectiles,
        float deltaTime,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        _damageEvents.Clear();
        foreach (var laserEntity in world.Query<LaserComponent, TransformComponent>().ToArray())
        {
            var laser = laserEntity.Get<LaserComponent>();
            var owner = OptionFollowSystem.FindEntity(world, laser.OwnerEntityId);
            if (owner is null || owner.Has<PendingDestroyComponent>())
            {
                laserEntity.Add(new PendingDestroyComponent());
                continue;
            }

            var start = owner.Get<TransformComponent>().Position;
            laserEntity.Get<TransformComponent>().Position = start;
            var end = start + (Vector2.Normalize(laser.Direction) * laser.Length);
            CancelProjectiles(laser, start, end, projectiles, telemetry, events);
            laser.DamageCooldownRemaining = Math.Max(0, laser.DamageCooldownRemaining - deltaTime);
            if (laser.DamageCooldownRemaining > 0) continue;
            DetectDamage(world, laserEntity, laser, start, end, telemetry);
            laser.DamageCooldownRemaining = laser.DamageInterval;
        }

        return _damageEvents;
    }

    private void DetectDamage(
        World world,
        Entity laserEntity,
        LaserComponent laser,
        Vector2 start,
        Vector2 end,
        SimulationTelemetry telemetry)
    {
        var targetLayer = laser.OwnerLayer == CollisionLayer.Player ? CollisionLayer.Enemy : CollisionLayer.Player;
        foreach (var target in world.Entities)
        {
            if (target.Has<PendingDestroyComponent>() ||
                !target.TryGet<ColliderComponent>(out var collider) || collider.Layer != targetLayer ||
                !target.TryGet<TransformComponent>(out var transform) ||
                !ProjectileCollisionSystem.IntersectsSweptCircle(
                    start,
                    end,
                    transform.Position,
                    laser.Width + collider.Radius,
                    out _)) continue;
            _damageEvents.Add(new DamageEvent(target, laser.Damage, laserEntity.Id));
            telemetry.CollisionsDetected++;
        }
    }

    private static void CancelProjectiles(
        LaserComponent laser,
        Vector2 start,
        Vector2 end,
        ProjectileStore projectiles,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        if (laser.ProjectileInteraction != "cancel-soft") return;
        var targetTeam = laser.OwnerLayer == CollisionLayer.Player ? ProjectileTeam.Enemy : ProjectileTeam.Player;
        for (var index = 0; index < projectiles.ActiveCount; index++)
        {
            if (projectiles.IsPendingRemovalAt(index) || projectiles.TeamAt(index) != targetTeam ||
                !projectiles.CanBeCancelledAt(index) ||
                projectiles.CancelResistanceAt(index) != ProjectileCancelResistance.Soft ||
                !ProjectileCollisionSystem.IntersectsSweptCircle(
                    start,
                    end,
                    projectiles.PositionAt(index),
                    laser.Width + projectiles.HitRadiusAt(index),
                    out _)) continue;
            projectiles.QueueRemoveAt(index);
            telemetry.EnemyBulletsCleared++;
            var projectileId = projectiles.IdAt(index);
            events.Publish((frame, sequence) => new ProjectileCancelledEvent(frame, sequence, projectileId));
        }
    }
}
