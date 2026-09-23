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
    private readonly RunModifierState? _modifiers;

    public AdvancedWeaponSystem(
        BulletFactory bulletFactory,
        RuntimeCapabilityRegistry capabilities,
        IRandomSource? randomSource = null,
        string? difficultyId = null,
        RunModifierState? modifiers = null)
    {
        _bulletFactory = bulletFactory ?? throw new ArgumentNullException(nameof(bulletFactory));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _randomSource = randomSource ?? new SeededRandomSource(0);
        _difficultyId = difficultyId;
        _modifiers = modifiers;
    }

    public GameEventBuffer? Events { get; set; }

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
                var specialWeapon = definitions.GetWeapon(ship.SpecialWeaponId);
                var specialWantsToFire = specialWeapon.ActionType == "lock-on" &&
                    specialWeapon.LockOn?.Trigger == "fire"
                        ? input.Fire
                        : input.Special;
                if (specialWantsToFire && specialWeapon.ActionType == "lock-on" &&
                    specialWeapon.LockOn is { MovementSpeedMultiplier: < 1 } lockOn &&
                    player.Get<WeaponRuntimeComponent>().GetOrCreate(specialWeapon.Id).HeldSeconds + deltaTime >=
                    lockOn.HoldDelaySeconds &&
                    player.TryGet<VelocityComponent>(out var velocity))
                {
                    velocity.Value *= lockOn.MovementSpeedMultiplier;
                }

                ProcessWeapons(
                    world,
                    definitions,
                    player,
                    CollisionLayer.Player,
                    new[] { ship.SpecialWeaponId },
                    specialWantsToFire,
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

        foreach (var enemy in world.Query<WeaponHolderComponent, TransformComponent>().ToArray())
        {
            if (!enemy.Has<EnemyComponent>() && !enemy.Has<ParentTransformComponent>() ||
                !IsEnabledWeaponOwner(world, enemy) ||
                enemy.Has<PendingDestroyComponent>() || enemy.Has<AttackTimelineComponent>() ||
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

    public void Update(
        World world,
        CompiledCatalog definitions,
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
            var weaponHandles = ship.IsFocused ? ship.FocusWeaponHandles : ship.NormalWeaponHandles;
            ProcessCompiledWeapons(
                world, definitions, player, CollisionLayer.Player, weaponHandles, input.Fire,
                deltaTime, ship.Power, telemetry, projectiles);
            if (ship.SpecialWeaponHandle >= 0)
            {
                var specialWeapon = definitions.Get(new WeaponHandle(ship.SpecialWeaponHandle));
                var specialWantsToFire = specialWeapon.Definition.ActionType == "lock-on" &&
                    specialWeapon.Definition.LockOn?.Trigger == "fire"
                        ? input.Fire
                        : input.Special;
                if (specialWantsToFire && specialWeapon.Definition.ActionType == "lock-on" &&
                    specialWeapon.Definition.LockOn is { MovementSpeedMultiplier: < 1 } lockOn &&
                    player.Get<WeaponRuntimeComponent>().GetOrCreate(specialWeapon.Definition.Id).HeldSeconds + deltaTime >=
                    lockOn.HoldDelaySeconds && player.TryGet<VelocityComponent>(out var velocity))
                {
                    velocity.Value *= lockOn.MovementSpeedMultiplier;
                }

                ProcessCompiledWeapons(
                    world,
                    definitions,
                    player,
                    CollisionLayer.Player,
                    new[] { ship.SpecialWeaponHandle },
                    specialWantsToFire,
                    deltaTime,
                    ship.Power,
                    telemetry,
                    projectiles);
            }

            UpdateInactiveCompiledWeapons(
                world,
                definitions,
                player,
                CollisionLayer.Player,
                ship.NormalWeaponHandles.Concat(ship.FocusWeaponHandles)
                    .Append(ship.SpecialWeaponHandle)
                    .Where(static handle => handle >= 0),
                weaponHandles.Concat(ship.SpecialWeaponHandle >= 0
                    ? new[] { ship.SpecialWeaponHandle }
                    : Array.Empty<int>()),
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
            var activeHandles = ship.IsFocused ? option.FocusWeaponHandles : option.NormalWeaponHandles;
            ProcessCompiledWeapons(
                world, definitions, optionEntity, CollisionLayer.Player, activeHandles, input.Fire,
                deltaTime, ship.Power, telemetry, projectiles);
            UpdateInactiveCompiledWeapons(
                world,
                definitions,
                optionEntity,
                CollisionLayer.Player,
                option.NormalWeaponHandles.Concat(option.FocusWeaponHandles),
                activeHandles,
                deltaTime,
                ship.Power,
                telemetry,
                projectiles);
        }

        foreach (var enemy in world.Query<WeaponHolderComponent, TransformComponent>().ToArray())
        {
            if (!enemy.Has<EnemyComponent>() && !enemy.Has<ParentTransformComponent>() ||
                !IsEnabledWeaponOwner(world, enemy) ||
                enemy.Has<PendingDestroyComponent>() || enemy.Has<AttackTimelineComponent>() ||
                enemy.TryGet<BossComponent>(out var boss) && boss.IsManaged) continue;
            var holder = enemy.Get<WeaponHolderComponent>();
            if (holder.WeaponHandle < 0)
                throw new InvalidOperationException($"Enemy weapon '{holder.WeaponId}' was not compiled.");
            ProcessCompiledWeapons(
                world,
                definitions,
                enemy,
                CollisionLayer.Enemy,
                new[] { holder.WeaponHandle },
                wantsToFire: true,
                deltaTime,
                power: 0,
                telemetry,
                projectiles);
        }
    }

    private void UpdateInactiveCompiledWeapons(
        World world,
        CompiledCatalog definitions,
        Entity owner,
        CollisionLayer ownerLayer,
        IEnumerable<int> configuredWeaponHandles,
        IEnumerable<int> activeWeaponHandles,
        float deltaTime,
        int power,
        SimulationTelemetry telemetry,
        ProjectileStore projectiles)
    {
        var active = activeWeaponHandles.ToHashSet();
        var runtime = owner.Get<WeaponRuntimeComponent>();
        foreach (var handleValue in configuredWeaponHandles.Distinct())
        {
            if (active.Contains(handleValue)) continue;
            var weapon = definitions.Get(new WeaponHandle(handleValue));
            if (weapon.Definition.ActionType == "laser")
            {
                UpdateLaserWeapon(world, owner, ownerLayer, weapon.Definition, runtime, wantsToFire: false, definitions);
            }
            else if (weapon.Definition.ActionType == "lock-on")
            {
                UpdateLockOnWeapon(
                    world, definitions.Source, owner, ownerLayer, weapon.Definition, runtime,
                    wantsToFire: false, deltaTime, power, telemetry, projectiles, definitions, weapon);
            }
        }
    }

    private void ProcessCompiledWeapons(
        World world,
        CompiledCatalog definitions,
        Entity owner,
        CollisionLayer ownerLayer,
        IEnumerable<int> weaponHandles,
        bool wantsToFire,
        float deltaTime,
        int power,
        SimulationTelemetry telemetry,
        ProjectileStore projectiles)
    {
        var runtime = owner.TryGet<WeaponRuntimeComponent>(out var existing)
            ? existing
            : owner.Add(new WeaponRuntimeComponent()).Get<WeaponRuntimeComponent>();
        foreach (var handleValue in weaponHandles.Distinct())
        {
            var weapon = definitions.Get(new WeaponHandle(handleValue));
            switch (weapon.Definition.ActionType)
            {
                case "projectile":
                    UpdateProjectileWeapon(
                        world, definitions.Source, owner, ownerLayer, weapon.Definition, runtime,
                        wantsToFire, deltaTime, power, telemetry, projectiles, definitions, weapon);
                    break;
                case "laser":
                    UpdateLaserWeapon(world, owner, ownerLayer, weapon.Definition, runtime, wantsToFire, definitions);
                    break;
                case "lock-on":
                    UpdateLockOnWeapon(
                        world, definitions.Source, owner, ownerLayer, weapon.Definition, runtime,
                        wantsToFire, deltaTime, power, telemetry, projectiles, definitions, weapon);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported weapon action '{weapon.Definition.ActionType}'.");
            }
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

    public void FireOnce(
        World world,
        CompiledCatalog definitions,
        Entity owner,
        WeaponHandle weaponHandle,
        SimulationTelemetry telemetry,
        ProjectileStore projectiles)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(projectiles);
        var compiledWeapon = definitions.Get(weaponHandle);
        var weapon = compiledWeapon.Definition;
        if (weapon.ActionType != "projectile")
        {
            throw new InvalidOperationException(
                $"Timeline fire requires a projectile weapon, but '{weapon.Id}' is '{weapon.ActionType}'.");
        }

        var runtime = owner.TryGet<WeaponRuntimeComponent>(out var existing)
            ? existing
            : owner.Add(new WeaponRuntimeComponent()).Get<WeaponRuntimeComponent>();
        foreach (var emitter in compiledWeapon.Emitters)
        {
            if (!IsEnabled(emitter.Definition.DifficultyTags)) continue;
            var state = runtime.GetOrCreate($"{weapon.Id}/{emitter.Definition.Id}");
            FireEmitter(
                world,
                definitions.Source,
                owner,
                CollisionLayer.Enemy,
                weapon,
                emitter.Definition,
                state,
                0,
                telemetry,
                projectiles,
                compiledProjectile: definitions.Get(emitter.ProjectileHandle),
                compiledFirePattern: compiledWeapon.FirePattern,
                compiledDefinitions: definitions);
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
        ProjectileStore projectiles,
        CompiledCatalog? compiledDefinitions = null,
        CompiledWeaponDefinition? compiledWeapon = null)
    {
        for (var emitterIndex = 0; emitterIndex < weapon.Emitters.Count; emitterIndex++)
        {
            var emitter = weapon.Emitters[emitterIndex];
            var compiledEmitter = compiledWeapon?.Emitters[emitterIndex];
            if (!IsEnabled(emitter.DifficultyTags)) continue;
            var state = runtime.GetOrCreate($"{weapon.Id}/{emitter.Id}");
            state.CooldownRemaining = Math.Max(0, state.CooldownRemaining - deltaTime);
            state.BurstCooldownRemaining = Math.Max(0, state.BurstCooldownRemaining - deltaTime);
            if (state.BurstShotsRemaining > 0 && state.BurstCooldownRemaining <= 0)
            {
                FireEmitter(
                    world, definitions, owner, ownerLayer, weapon, emitter, state, power, telemetry, projectiles,
                    compiledProjectile: compiledDefinitions is not null && compiledEmitter is not null
                        ? compiledDefinitions.Get(compiledEmitter.ProjectileHandle)
                        : null,
                    compiledFirePattern: compiledWeapon?.FirePattern,
                    compiledDefinitions: compiledDefinitions);
                state.BurstShotsRemaining--;
                if (state.BurstShotsRemaining > 0)
                {
                    state.BurstCooldownRemaining = emitter.BurstInterval;
                }
                else
                {
                    state.CooldownRemaining = GetFireInterval(emitter, ownerLayer);
                }

                continue;
            }

            if (!wantsToFire || state.BurstShotsRemaining > 0 || state.CooldownRemaining > 0) continue;
            FireEmitter(
                world, definitions, owner, ownerLayer, weapon, emitter, state, power, telemetry, projectiles,
                compiledProjectile: compiledDefinitions is not null && compiledEmitter is not null
                    ? compiledDefinitions.Get(compiledEmitter.ProjectileHandle)
                    : null,
                compiledFirePattern: compiledWeapon?.FirePattern,
                compiledDefinitions: compiledDefinitions);
            state.BurstShotsRemaining = emitter.BurstCount - 1;
            if (state.BurstShotsRemaining > 0)
            {
                state.BurstCooldownRemaining = emitter.BurstInterval;
            }
            else
            {
                state.CooldownRemaining = GetFireInterval(emitter, ownerLayer);
            }
        }
    }

    private void UpdateLaserWeapon(
        World world,
        Entity owner,
        CollisionLayer ownerLayer,
        WeaponDefinition weapon,
        WeaponRuntimeComponent runtime,
        bool wantsToFire,
        CompiledCatalog? compiledDefinitions = null)
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
                    Math.Max(1, (int)MathF.Round(settings.Damage *
                        (ownerLayer == CollisionLayer.Player ? _modifiers?.PlayerDamageMultiplier ?? 1 : 1))),
                    settings.DamageInterval,
                    settings.VisualId,
                    settings.ProjectileInteraction,
                    compiledDefinitions?.Tags.Mask(settings.Tags.Concat(new[] { "laser" })) ?? 0,
                    checked(settings.InteractionPower +
                        (ownerLayer == CollisionLayer.Player ? _modifiers?.InteractionPowerBonus ?? 0 : 0)),
                    settings.InteractionResistance));
            state.ActiveLaserEntityId = existing.Id;
            Events?.Publish((frame, sequence) => new AudioCueEvent(frame, sequence, "se-laser", owner.Id));
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
        ProjectileStore projectiles,
        CompiledCatalog? compiledDefinitions = null,
        CompiledWeaponDefinition? compiledWeapon = null)
    {
        var settings = weapon.LockOn!;
        var state = runtime.GetOrCreate(weapon.Id);
        state.CooldownRemaining = Math.Max(0, state.CooldownRemaining - deltaTime);
        RemoveInvalidTargets(world, state);
        state.HeldSeconds = wantsToFire ? state.HeldSeconds + deltaTime : 0;
        var isEngaged = wantsToFire && state.HeldSeconds >= settings.HoldDelaySeconds;
        if (isEngaged)
        {
            var newlyLocked = AcquireTargets(world, owner, ownerLayer, settings, state);
            if (newlyLocked > 0 && ownerLayer == CollisionLayer.Player)
            {
                Events?.Publish((frame, sequence) => new TargetsLockedEvent(frame, sequence, owner.Id, newlyLocked));
            }

            if (settings.FireMode == "continuous" && state.CooldownRemaining <= 0 &&
                FireAtLockedTargets(
                    world,
                    definitions,
                    owner,
                    ownerLayer,
                    weapon,
                    settings,
                    state,
                    power,
                    telemetry,
                    projectiles,
                    compiledDefinitions,
                    compiledWeapon))
            {
                state.CooldownRemaining = weapon.Emitters.Max(emitter => GetFireInterval(emitter, ownerLayer));
            }
        }
        else if (!wantsToFire && state.WasHeld)
        {
            if (settings.FireMode == "release" && state.CooldownRemaining <= 0 &&
                FireAtLockedTargets(
                    world,
                    definitions,
                    owner,
                    ownerLayer,
                    weapon,
                    settings,
                    state,
                    power,
                    telemetry,
                    projectiles,
                    compiledDefinitions,
                    compiledWeapon))
            {
                state.CooldownRemaining = weapon.Emitters.Max(emitter => GetFireInterval(emitter, ownerLayer));
            }

            state.LockedTargetEntityIds.Clear();
        }

        state.WasHeld = isEngaged;
    }

    private bool FireAtLockedTargets(
        World world,
        DefinitionCatalog definitions,
        Entity owner,
        CollisionLayer ownerLayer,
        WeaponDefinition weapon,
        LockOnWeaponDefinition settings,
        WeaponActionState state,
        int power,
        SimulationTelemetry telemetry,
        ProjectileStore projectiles,
        CompiledCatalog? compiledDefinitions = null,
        CompiledWeaponDefinition? compiledWeapon = null)
    {
        if (state.LockedTargetEntityIds.Count == 0) return false;

        var optionOrigins = settings.FireFromOptions
            ? world.Query<OptionUnitComponent, TransformComponent>()
                .Where(entity => entity.Get<OptionUnitComponent>().OwnerEntityId == owner.Id &&
                    !entity.Has<PendingDestroyComponent>())
                .OrderBy(static entity => entity.Id)
                .Select(entity => entity.Get<TransformComponent>().Position)
                .ToArray()
            : Array.Empty<Vector2>();
        var fired = false;
        var shotCount = optionOrigins.Length > 0
            ? Math.Max(optionOrigins.Length, state.LockedTargetEntityIds.Count)
            : state.LockedTargetEntityIds.Count;
        for (var shotIndex = 0; shotIndex < shotCount; shotIndex++)
        {
            var targetId = state.LockedTargetEntityIds[shotIndex % state.LockedTargetEntityIds.Count];
            var target = OptionFollowSystem.FindEntity(world, targetId);
            if (target is null || target.Has<PendingDestroyComponent>() ||
                !target.TryGet<TransformComponent>(out var targetTransform)) continue;
            var origin = optionOrigins.Length > 0
                ? optionOrigins[shotIndex % optionOrigins.Length]
                : owner.Get<TransformComponent>().Position;
            var direction = targetTransform.Position - origin;
            if (direction == Vector2.Zero)
            {
                direction = ownerLayer == CollisionLayer.Player ? -Vector2.UnitY : Vector2.UnitY;
            }

            for (var emitterIndex = 0; emitterIndex < weapon.Emitters.Count; emitterIndex++)
            {
                var emitter = weapon.Emitters[emitterIndex];
                var compiledEmitter = compiledWeapon?.Emitters[emitterIndex];
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
                    direction,
                    origin,
                    targetId,
                    compiledDefinitions is not null && compiledEmitter is not null
                        ? compiledDefinitions.Get(compiledEmitter.ProjectileHandle)
                        : null,
                    compiledWeapon?.FirePattern,
                    compiledDefinitions);
                fired = true;
            }
        }

        return fired;
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
        Vector2? forcedDirection = null,
        Vector2? forcedPosition = null,
        int targetEntityId = 0,
        CompiledProjectileDefinition? compiledProjectile = null,
        IFirePatternFactory? compiledFirePattern = null,
        CompiledCatalog? compiledDefinitions = null)
    {
        var position = (forcedPosition ?? owner.Get<TransformComponent>().Position) +
            new Vector2(emitter.OffsetX, emitter.OffsetY);
        var baseDirection = forcedDirection ?? ResolveAngleSource(world, owner, ownerLayer, emitter, state);
        var powerModifier = ResolvePowerModifier(definitions, world, owner, power, compiledDefinitions);
        var count = emitter.ProjectileCount + powerModifier.AdditionalProjectileCount +
            (ownerLayer == CollisionLayer.Enemy ? _modifiers?.AdditionalEnemyProjectiles ?? 0 : 0);
        foreach (var direction in GetDirections(weapon, emitter, state, baseDirection, count, compiledFirePattern))
        {
            foreach (var speedMultiplier in GetSpeedMultipliers(emitter))
            {
                var resolvedSpeedMultiplier = speedMultiplier * (ownerLayer == CollisionLayer.Enemy
                    ? _modifiers?.EnemyProjectileSpeedMultiplier ?? 1
                    : 1);
                var resolvedDamageMultiplier = powerModifier.DamageMultiplier * (ownerLayer == CollisionLayer.Player
                    ? _modifiers?.PlayerDamageMultiplier ?? 1
                    : 1);
                var acceleration = emitter.SpeedMode is "accelerating" or "decelerating"
                    ? emitter.AccelerationPerSecond
                    : 0;
                if (compiledProjectile is null)
                {
                    _bulletFactory.Create(
                        projectiles,
                        definitions.GetProjectile(emitter.ProjectileId),
                        position,
                        direction,
                        ownerLayer,
                        owner.Id,
                        resolvedSpeedMultiplier,
                        resolvedDamageMultiplier,
                        acceleration,
                        targetEntityId,
                        ownerLayer == CollisionLayer.Player ? _modifiers?.InteractionPowerBonus ?? 0 : 0);
                }
                else
                {
                    _bulletFactory.Create(
                        projectiles,
                        compiledProjectile,
                        position,
                        direction,
                        ownerLayer,
                        owner.Id,
                        resolvedSpeedMultiplier,
                        resolvedDamageMultiplier,
                        acceleration,
                        targetEntityId,
                        ownerLayer == CollisionLayer.Player ? _modifiers?.InteractionPowerBonus ?? 0 : 0);
                }
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
        int projectileCount,
        IFirePatternFactory? compiledFirePattern = null)
    {
        if (emitter.UsesLegacyPattern)
        {
            var pattern = weapon.Pattern ?? throw new InvalidOperationException(
                $"Legacy weapon '{weapon.Id}' requires a fire pattern.");
            var holder = new WeaponHolderComponent(weapon.Id)
            {
                PatternAngleDegrees = state.PatternAngleDegrees,
                PatternDirection = state.PatternDirection,
                ShotsSinceDirectionChange = state.ShotsSinceDirectionChange
            };
            var factory = compiledFirePattern ??
                _capabilities.FirePatterns.Resolve(pattern.Type, $"weapon '{weapon.Id}' pattern");
            var baseDirections = factory.GetDirections(pattern, holder, baseDirection).ToArray();
            foreach (var direction in baseDirections) yield return direction;
            for (var index = baseDirections.Length; index < projectileCount; index++)
            {
                yield return RotateDegrees(baseDirection, index * (360f / projectileCount));
            }
            factory.Advance(pattern, holder);
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
        if (owner.TryGet<RotationComponent>(out var rotation))
            forward = RotateDegrees(forward, rotation.Degrees);
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
        _modifiers?.IsPatternEnabled(difficultyTags) ??
        (difficultyTags.Count == 0 || _difficultyId is not null && difficultyTags.Contains(_difficultyId, StringComparer.Ordinal));

    private float GetFireInterval(EmitterDefinition emitter, CollisionLayer ownerLayer) =>
        emitter.FireInterval * (ownerLayer == CollisionLayer.Enemy
            ? _modifiers?.EnemyFireIntervalMultiplier ?? 1
            : _modifiers?.PlayerFireIntervalMultiplier ?? 1);

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
        int power,
        CompiledCatalog? compiledDefinitions = null)
    {
        if (!owner.TryGet<ShipComponent>(out var ship))
        {
            if (!owner.TryGet<OptionUnitComponent>(out var option)) return new PowerLevelModifierDefinition();
            var player = OptionFollowSystem.FindEntity(world, option.OwnerEntityId);
            if (player is null || !player.TryGet<ShipComponent>(out ship)) return new PowerLevelModifierDefinition();
        }

        var definition = compiledDefinitions is not null && ship.DefinitionHandle >= 0
            ? compiledDefinitions.Get(new ShipHandle(ship.DefinitionHandle))
            : definitions.GetShip(ship.DefinitionId);
        var result = new PowerLevelModifierDefinition();
        foreach (var modifier in definition.PowerLevels)
        {
            if (modifier.MinimumPower > power) break;
            result = modifier;
        }

        return result;
    }

    private static int AcquireTargets(
        World world,
        Entity owner,
        CollisionLayer ownerLayer,
        LockOnWeaponDefinition settings,
        WeaponActionState state)
    {
        var targetLayer = ownerLayer == CollisionLayer.Player ? CollisionLayer.Enemy : CollisionLayer.Player;
        var origin = owner.Get<TransformComponent>().Position;
        var rangeSquared = settings.Range * settings.Range;
        var forward = ownerLayer == CollisionLayer.Player ? -Vector2.UnitY : Vector2.UnitY;
        var minimumForwardDot = settings.AcquisitionAngleDegrees >= 360
            ? -1
            : MathF.Cos(settings.AcquisitionAngleDegrees * MathF.PI / 360);
        var candidates = new List<(Entity Entity, float Distance)>();
        var occupied = new Dictionary<int, int>();
        foreach (var weaponOwner in world.Query<WeaponRuntimeComponent>())
            foreach (var weaponState in weaponOwner.Get<WeaponRuntimeComponent>().States.Values)
            {
                if (ReferenceEquals(weaponState, state)) continue;
                foreach (var targetId in weaponState.LockedTargetEntityIds)
                    occupied[targetId] = occupied.GetValueOrDefault(targetId) + 1;
            }
        foreach (var candidate in world.Entities)
        {
            if (candidate.Has<PendingDestroyComponent>() ||
                !candidate.TryGet<ColliderComponent>(out var collider) || collider.Layer != targetLayer ||
                !candidate.TryGet<TransformComponent>(out var transform)) continue;
            if (candidate.TryGet<ActorPartComponent>(out var actorPart) && !actorPart.Enabled) continue;
            if (candidate.TryGet<LockTargetComponent>(out var lockTarget) && !lockTarget.Targetable) continue;
            var distance = Vector2.DistanceSquared(origin, transform.Position);
            if (distance > rangeSquared) continue;
            var offset = transform.Position - origin;
            if (offset != Vector2.Zero && Vector2.Dot(Vector2.Normalize(offset), forward) < minimumForwardDot)
            {
                continue;
            }

            candidates.Add((candidate, distance));
        }

        candidates.Sort(static (left, right) =>
        {
            var distance = left.Distance.CompareTo(right.Distance);
            return distance != 0 ? distance : left.Entity.Id.CompareTo(right.Entity.Id);
        });
        var previous = state.LockedTargetEntityIds
            .GroupBy(static id => id)
            .ToDictionary(static group => group.Key, static group => group.Count());
        state.LockedTargetEntityIds.Clear();
        for (var index = 0; index < candidates.Count && state.LockedTargetEntityIds.Count < settings.MaximumTargets; index++)
        {
            var candidate = candidates[index].Entity;
            var capacity = candidate.TryGet<LockTargetComponent>(out var target) ? target.Capacity : 1;
            var available = Math.Max(0, capacity - occupied.GetValueOrDefault(candidate.Id));
            for (var slot = 0; slot < available && state.LockedTargetEntityIds.Count < settings.MaximumTargets; slot++)
                state.LockedTargetEntityIds.Add(candidate.Id);
        }

        var retained = new Dictionary<int, int>();
        var newlyLocked = 0;
        foreach (var id in state.LockedTargetEntityIds)
        {
            var used = retained.GetValueOrDefault(id);
            if (used >= previous.GetValueOrDefault(id)) newlyLocked++;
            retained[id] = used + 1;
        }
        return newlyLocked;
    }

    private static void RemoveInvalidTargets(World world, WeaponActionState state)
    {
        for (var index = state.LockedTargetEntityIds.Count - 1; index >= 0; index--)
        {
            var target = OptionFollowSystem.FindEntity(world, state.LockedTargetEntityIds[index]);
            if (target is null || target.Has<PendingDestroyComponent>() ||
                target.TryGet<ActorPartComponent>(out var part) && !part.Enabled ||
                target.TryGet<LockTargetComponent>(out var lockTarget) && !lockTarget.Targetable)
                state.LockedTargetEntityIds.RemoveAt(index);
        }
    }

    private static Vector2 RotateDegrees(Vector2 vector, float degrees)
    {
        var radians = degrees * (MathF.PI / 180);
        var cosine = MathF.Cos(radians);
        var sine = MathF.Sin(radians);
        return new Vector2((vector.X * cosine) - (vector.Y * sine), (vector.X * sine) + (vector.Y * cosine));
    }

    private static bool IsEnabledWeaponOwner(World world, Entity owner)
    {
        if (!owner.TryGet<ParentTransformComponent>(out var attachment)) return true;
        var parent = OptionFollowSystem.FindEntity(world, attachment.ParentEntityId);
        return parent is not null && !parent.Has<PendingDestroyComponent>() &&
            (!parent.TryGet<ActorPartComponent>(out var part) || part.Enabled);
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
            if (!projectiles.HasCompiledDefinitions)
                CancelProjectilesCompatibility(laser, start, end, projectiles, telemetry, events);
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
                target.TryGet<ActorPartComponent>(out var part) && !part.Enabled ||
                !target.TryGet<ColliderComponent>(out var collider) || collider.Layer != targetLayer ||
                !target.TryGet<TransformComponent>(out var transform) ||
                !ActorCollision.IntersectsSweptProjectile(target, start, end, laser.Width, out _)) continue;
            _damageEvents.Add(new DamageEvent(target, laser.Damage, laserEntity.Id));
            telemetry.CollisionsDetected++;
        }
    }

    private static void CancelProjectilesCompatibility(
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
                    start, end, projectiles.PositionAt(index),
                    laser.Width + projectiles.HitRadiusAt(index), out _)) continue;
            projectiles.QueueRemoveAt(index);
            telemetry.EnemyBulletsCleared++;
            var projectileId = projectiles.IdAt(index);
            events.Publish((frame, sequence) => new ProjectileCancelledEvent(frame, sequence, projectileId));
        }
    }

}
