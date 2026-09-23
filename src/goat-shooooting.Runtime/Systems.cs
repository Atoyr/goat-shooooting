using System.Numerics;
using GoatShooooting.Core;
using GoatShooooting.Definitions;

namespace GoatShooooting.Runtime;

public sealed class PlayerInputSystem
{
    public void Update(World world, IInputState input)
    {
        foreach (var entity in world.Query<PlayerComponent, VelocityComponent>())
        {
            if (entity.TryGet<PlayerLifeCycleComponent>(out var lifeCycle) && !lifeCycle.CanAct)
            {
                entity.Get<VelocityComponent>().Value = Vector2.Zero;
                continue;
            }

            var movement = new Vector2(
                Math.Clamp(input.MoveX, -1f, 1f),
                Math.Clamp(input.MoveY, -1f, 1f));
            if (movement.LengthSquared() > 1f)
            {
                movement = Vector2.Normalize(movement);
            }

            var speed = entity.Get<PlayerComponent>().Speed;
            if (entity.TryGet<ShipComponent>(out var ship))
            {
                ship.IsFocused = input.Focus;
                speed = ship.IsFocused ? ship.FocusSpeed : ship.NormalSpeed;
            }

            entity.Get<VelocityComponent>().Value = movement * speed;
        }
    }
}

public sealed class WeaponSystem
{
    private readonly BulletFactory _bulletFactory;
    private readonly RuntimeCapabilityRegistry _capabilities;
    private readonly AdvancedWeaponSystem _advancedWeaponSystem;

    public WeaponSystem(
        BulletFactory bulletFactory,
        RuntimeCapabilityRegistry? capabilities = null,
        IRandomSource? randomSource = null,
        string? difficultyId = null,
        RunModifierState? modifiers = null)
    {
        _bulletFactory = bulletFactory ?? throw new ArgumentNullException(nameof(bulletFactory));
        _capabilities = capabilities ?? RuntimeCapabilityRegistry.CreateBuiltIn();
        _advancedWeaponSystem = new AdvancedWeaponSystem(_bulletFactory, _capabilities, randomSource, difficultyId, modifiers);
    }

    internal AdvancedWeaponSystem Advanced => _advancedWeaponSystem;

    public void Update(
        World world,
        DefinitionCatalog definitions,
        IInputState input,
        float deltaTime,
        SimulationTelemetry telemetry) =>
        UpdateCore(world, definitions, input, deltaTime, telemetry, projectiles: null);

    public void Update(
        World world,
        DefinitionCatalog definitions,
        IInputState input,
        float deltaTime,
        SimulationTelemetry telemetry,
        ProjectileStore projectiles)
    {
        ArgumentNullException.ThrowIfNull(projectiles);
        UpdateCore(world, definitions, input, deltaTime, telemetry, projectiles);
    }

    public void Update(
        World world,
        CompiledCatalog definitions,
        IInputState input,
        float deltaTime,
        SimulationTelemetry telemetry,
        ProjectileStore projectiles)
    {
        ArgumentNullException.ThrowIfNull(projectiles);
        _advancedWeaponSystem.Update(world, definitions, input, deltaTime, telemetry, projectiles);
    }

    private void UpdateCore(
        World world,
        DefinitionCatalog definitions,
        IInputState input,
        float deltaTime,
        SimulationTelemetry telemetry,
        ProjectileStore? projectiles)
    {
        if (projectiles is not null)
        {
            _advancedWeaponSystem.Update(world, definitions, input, deltaTime, telemetry, projectiles);
            return;
        }

        foreach (var entity in world.Query<WeaponHolderComponent, TransformComponent>().ToArray())
        {
            var holder = entity.Get<WeaponHolderComponent>();
            holder.CooldownRemaining = Math.Max(0, holder.CooldownRemaining - deltaTime);

            var ownerLayer = entity.Has<PlayerComponent>()
                ? CollisionLayer.Player
                : entity.Has<EnemyComponent>()
                    ? CollisionLayer.Enemy
                    : (CollisionLayer?)null;
            var wantsToFire = ownerLayer switch
            {
                CollisionLayer.Player => input.Fire,
                CollisionLayer.Enemy => true,
                _ => false
            };
            if (ownerLayer is null || !wantsToFire || holder.CooldownRemaining > 0)
            {
                continue;
            }

            var weapon = definitions.GetWeapon(holder.WeaponId);
            var bullet = definitions.GetBullet(weapon.BulletId);
            var baseDirection = ownerLayer == CollisionLayer.Player ? -Vector2.UnitY : Vector2.UnitY;
            var pattern = weapon.Pattern!;
            var patternFactory = _capabilities.FirePatterns.Resolve(pattern.Type, $"weapon '{weapon.Id}' pattern");
            foreach (var direction in patternFactory.GetDirections(pattern, holder, baseDirection))
            {
                if (projectiles is null)
                {
                    _bulletFactory.Create(
                        world,
                        bullet,
                        entity.Get<TransformComponent>().Position,
                        direction,
                        ownerLayer.Value);
                }
                else
                {
                    _bulletFactory.Create(
                        projectiles,
                        bullet,
                        entity.Get<TransformComponent>().Position,
                        direction,
                        ownerLayer.Value,
                        entity.Id);
                }

                telemetry.BulletsSpawned++;
                if (ownerLayer == CollisionLayer.Enemy)
                {
                    telemetry.EnemyBulletsSpawned++;
                }
            }

            patternFactory.Advance(pattern, holder);
            holder.CooldownRemaining = weapon.Cooldown;
        }
    }
}

public sealed class HomingMovementSystem
{
    public void Update(World world, float deltaTime)
    {
        var targets = world.Query<TransformComponent, ColliderComponent>()
            .Where(static entity => !entity.Has<PendingDestroyComponent>())
            .ToArray();

        foreach (var bullet in world.Query<BulletComponent, HomingMovementComponent>().ToArray())
        {
            if (bullet.Has<PendingDestroyComponent>() || !bullet.TryGet<VelocityComponent>(out var velocity))
            {
                continue;
            }

            var bulletState = bullet.Get<BulletComponent>();
            var position = bullet.Get<TransformComponent>().Position;
            var target = targets
                .Where(entity => entity.Get<ColliderComponent>().Layer == bulletState.TargetLayer)
                .MinBy(entity => Vector2.DistanceSquared(position, entity.Get<TransformComponent>().Position));
            if (target is null)
            {
                continue;
            }

            var desired = target.Get<TransformComponent>().Position - position;
            if (desired == Vector2.Zero || velocity.Value == Vector2.Zero)
            {
                continue;
            }

            var speed = velocity.Value.Length();
            var currentDirection = velocity.Value / speed;
            var desiredDirection = Vector2.Normalize(desired);
            var signedAngle = MathF.Atan2(
                (currentDirection.X * desiredDirection.Y) - (currentDirection.Y * desiredDirection.X),
                Vector2.Dot(currentDirection, desiredDirection));
            var maximumTurn = bullet.Get<HomingMovementComponent>().TurnRadiansPerSecond * deltaTime;
            velocity.Value = Rotate(currentDirection, Math.Clamp(signedAngle, -maximumTurn, maximumTurn)) * speed;
        }
    }

    private static Vector2 Rotate(Vector2 vector, float angle)
    {
        var cosine = MathF.Cos(angle);
        var sine = MathF.Sin(angle);
        return new Vector2(
            (vector.X * cosine) - (vector.Y * sine),
            (vector.X * sine) + (vector.Y * cosine));
    }
}

public sealed class MovementSystem
{
    public void Update(World world, float deltaTime, SimulationTelemetry? telemetry = null)
    {
        foreach (var entity in world.Query<TransformComponent, VelocityComponent>())
        {
            if (entity.Has<MotionTimelineComponent>()) continue;
            var velocity = entity.Get<VelocityComponent>().Value;
            entity.Get<TransformComponent>().Position += velocity * deltaTime;
            if (telemetry is not null && entity.Has<BulletComponent>() && velocity != Vector2.Zero)
            {
                telemetry.BulletMovementFrames++;
            }

            if (telemetry is not null && entity.Has<EnemyComponent>() && velocity != Vector2.Zero)
            {
                telemetry.EnemyMovementFrames++;
            }
        }
    }
}

public sealed class MovementPatternSystem
{
    public void Update(World world, float deltaTime)
    {
        foreach (var entity in world.Query<TransformComponent, SineMovementComponent>())
        {
            var pattern = entity.Get<SineMovementComponent>();
            pattern.Elapsed += deltaTime;
            entity.Get<TransformComponent>().Position = new Vector2(
                pattern.OriginX + (pattern.Amplitude * MathF.Sin(2 * MathF.PI * pattern.Frequency * pattern.Elapsed)),
                entity.Get<TransformComponent>().Position.Y);
        }

        foreach (var entity in world.Query<TransformComponent, ZigzagMovementComponent>())
        {
            var pattern = entity.Get<ZigzagMovementComponent>();
            pattern.Elapsed += deltaTime;
            var phase = 2 * MathF.PI * pattern.Frequency * pattern.Elapsed;
            var triangleWave = (2 / MathF.PI) * MathF.Asin(MathF.Sin(phase));
            entity.Get<TransformComponent>().Position = new Vector2(
                pattern.OriginX + (pattern.Amplitude * triangleWave),
                entity.Get<TransformComponent>().Position.Y);
        }
    }
}

public sealed class PlayerBoundsSystem
{
    public void Update(World world, float width, float height)
    {
        foreach (var player in world.Query<PlayerComponent, TransformComponent, ColliderComponent>())
        {
            var transform = player.Get<TransformComponent>();
            var radius = player.Get<ColliderComponent>().Radius;
            var minX = Math.Min(radius, width / 2);
            var maxX = Math.Max(width - radius, width / 2);
            var minY = Math.Min(radius, height / 2);
            var maxY = Math.Max(height - radius, height / 2);
            transform.Position = new Vector2(
                Math.Clamp(transform.Position.X, minX, maxX),
                Math.Clamp(transform.Position.Y, minY, maxY));
        }
    }
}

public sealed class OutOfBoundsSystem
{
    public void Update(World world, float width, float height)
    {
        foreach (var entity in world.Query<TransformComponent, ColliderComponent>().ToArray())
        {
            if (entity.Has<PlayerComponent>() || entity.Has<BossComponent>() || entity.Has<PendingDestroyComponent>())
            {
                continue;
            }

            if (entity.Has<MotionTimelineComponent>()) continue;

            var position = entity.Get<TransformComponent>().Position;
            var radius = entity.Get<ColliderComponent>().Radius;
            if (position.X + radius < 0 || position.X - radius > width ||
                position.Y + radius < 0 || position.Y - radius > height)
            {
                entity.Add(new PendingDestroyComponent());
            }
        }
    }
}

public sealed class StageSystem
{
    private readonly StageDefinition _definition;
    private readonly CompiledStageDefinition? _compiledDefinition;
    private readonly EnemyFactory _enemyFactory;
    private readonly RuntimeCapabilityRegistry _capabilities;
    private readonly int[] _spawnedCounts;
    private readonly int _bossesKilledAtStart;
    private readonly StageProgramRuntime? _programRuntime;
    private readonly FixedWorldClock _compatibilityWorldClock = new();
    private readonly StagePresentationState _legacyPresentation;
    private readonly HashSet<string> _completedBossIds = new(StringComparer.Ordinal);
    private double _elapsed;
    private long _elapsedTicks;
    private long _worldFrameAtStart = -1;

    public StageSystem(
        StageDefinition definition,
        EnemyFactory enemyFactory,
        int bossesKilledAtStart = 0,
        RuntimeCapabilityRegistry? capabilities = null)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _enemyFactory = enemyFactory ?? throw new ArgumentNullException(nameof(enemyFactory));
        _capabilities = capabilities ?? RuntimeCapabilityRegistry.CreateBuiltIn();
        _spawnedCounts = new int[definition.Events.Count];
        _bossesKilledAtStart = bossesKilledAtStart;
        _legacyPresentation = new StagePresentationState { BackgroundId = _definition.BackgroundId };
    }

    public StageSystem(
        CompiledStageDefinition definition,
        EnemyFactory enemyFactory,
        int bossesKilledAtStart = 0)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definition = definition.Definition;
        _compiledDefinition = definition;
        _enemyFactory = enemyFactory ?? throw new ArgumentNullException(nameof(enemyFactory));
        _capabilities = null!;
        _spawnedCounts = new int[_definition.Events.Count];
        _bossesKilledAtStart = bossesKilledAtStart;
        _legacyPresentation = new StagePresentationState { BackgroundId = _definition.BackgroundId };
        _programRuntime = definition.Program is null ? null : new StageProgramRuntime(
            definition.Program, _enemyFactory, _definition.BackgroundId);
    }

    public double Elapsed => _elapsed;
    public int BossCount => _programRuntime?.BossCount ?? _definition.Events
        .Where(static stageEvent => stageEvent.IsBoss)
        .Sum(static stageEvent => stageEvent.Count);
    public StagePresentationState Presentation => _programRuntime?.Presentation ?? _legacyPresentation;
    internal IReadOnlyCollection<string> CompletedBossIds => _completedBossIds;
    public bool IsComplete => _programRuntime?.IsComplete ?? _definition.Events
        .Select((stageEvent, index) => _spawnedCounts[index] >= stageEvent.Count)
        .All(static complete => complete);

    public bool IsCleared(World world, SimulationTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(telemetry);
        if (_programRuntime?.ForceClear == true) return true;
        if (_definition.Objectives.Count > 0)
        {
            return IsComplete && _definition.Objectives.All(objective => objective.Type switch
            {
                "defeat-all-enemies" => !world.Query<EnemyComponent>()
                    .Any(static enemy => !enemy.Has<PendingDestroyComponent>()),
                "complete-boss" => _completedBossIds.Contains(objective.BossId!),
                _ => false
            });
        }

        if (BossCount > 0)
        {
            var allBossesSpawned = _definition.Events
                .Select((stageEvent, index) => !stageEvent.IsBoss || _spawnedCounts[index] >= stageEvent.Count)
                .All(static spawned => spawned);
            return allBossesSpawned && telemetry.BossesKilled - _bossesKilledAtStart >= BossCount;
        }

        return IsComplete && !world.Query<EnemyComponent>().Any();
    }

    public void ObserveEvents(IEnumerable<IGameplayEvent> events)
    {
        foreach (var completed in events.OfType<BossCompletedEvent>()) _completedBossIds.Add(completed.BossDefinitionId);
    }

    public void Update(World world, DefinitionCatalog definitions, float deltaTime, SimulationTelemetry telemetry)
    {
        _elapsed += deltaTime;
        SpawnDueEvents(world, definitions, telemetry);
    }

    public void Update(World world, CompiledCatalog definitions, float deltaTime, SimulationTelemetry telemetry)
    {
        _elapsed += deltaTime;
        SpawnDueEvents(world, definitions, telemetry);
    }

    public void Tick(World world, DefinitionCatalog definitions, SimulationTelemetry telemetry)
    {
        _elapsedTicks++;
        _elapsed = _elapsedTicks / (double)SimulationTiming.TicksPerSecond;
        SpawnDueEvents(world, definitions, telemetry);
    }

    public void Tick(World world, CompiledCatalog definitions, SimulationTelemetry telemetry)
    {
        _elapsedTicks++;
        _elapsed = _elapsedTicks / (double)SimulationTiming.TicksPerSecond;
        if (_programRuntime is not null)
        {
            _compatibilityWorldClock.Advance();
            if (_worldFrameAtStart < 0) _worldFrameAtStart = _compatibilityWorldClock.Frame;
            var events = new GameEventBuffer();
            events.BeginTick(_elapsedTicks);
            _programRuntime.Update(
                world, definitions, _elapsedTicks - 1, _compatibilityWorldClock.Frame - _worldFrameAtStart,
                _compatibilityWorldClock, telemetry, events);
            return;
        }
        SpawnDueEvents(world, definitions, telemetry);
    }

    public void Tick(
        World world,
        CompiledCatalog definitions,
        SimulationTelemetry telemetry,
        long worldFrame,
        FixedWorldClock worldClock,
        GameEventBuffer events)
    {
        _elapsedTicks++;
        _elapsed = _elapsedTicks / (double)SimulationTiming.TicksPerSecond;
        if (_programRuntime is null)
        {
            SpawnDueEvents(world, definitions, telemetry);
            return;
        }
        if (_worldFrameAtStart < 0) _worldFrameAtStart = worldFrame;
        _programRuntime.Update(
            world, definitions, _elapsedTicks - 1, worldFrame - _worldFrameAtStart, worldClock, telemetry, events);
    }

    public StageProgramRuntimeSnapshot? CaptureProgramSnapshot() =>
        _programRuntime?.CaptureSnapshot();

    internal StageSystemCheckpoint CaptureCheckpoint() => new(
        _definition,
        _compiledDefinition,
        _bossesKilledAtStart,
        (int[])_spawnedCounts.Clone(),
        _completedBossIds.Order(StringComparer.Ordinal).ToArray(),
        _elapsed,
        _elapsedTicks,
        _worldFrameAtStart,
        _compatibilityWorldClock.ScaleQ16,
        _compatibilityWorldClock.TimeQ16,
        _programRuntime?.CaptureCheckpoint());

    internal void RestoreCheckpoint(StageSystemCheckpoint checkpoint)
    {
        if (checkpoint.SpawnedCounts.Length != _spawnedCounts.Length)
            throw new InvalidOperationException("Stage checkpoint does not match the active stage.");
        checkpoint.SpawnedCounts.CopyTo(_spawnedCounts, 0);
        _completedBossIds.Clear();
        foreach (var bossId in checkpoint.CompletedBossIds) _completedBossIds.Add(bossId);
        _elapsed = checkpoint.Elapsed;
        _elapsedTicks = checkpoint.ElapsedTicks;
        _worldFrameAtStart = checkpoint.WorldFrameAtStart;
        _compatibilityWorldClock.Restore(checkpoint.CompatibilityScaleQ16, checkpoint.CompatibilityTimeQ16);
        if (checkpoint.Program is not null)
            (_programRuntime ?? throw new InvalidOperationException("Stage checkpoint requires a stage program."))
                .RestoreCheckpoint(checkpoint.Program);
    }

    private void SpawnDueEvents(
        World world,
        DefinitionCatalog definitions,
        SimulationTelemetry telemetry)
    {
        for (var index = 0; index < _definition.Events.Count; index++)
        {
            var stageEvent = _definition.Events[index];
            while (_spawnedCounts[index] < stageEvent.Count)
            {
                var spawnIndex = _spawnedCounts[index];
                var spawnTime = stageEvent.Time + (spawnIndex * stageEvent.SpawnInterval);
                if (spawnTime > _elapsed)
                {
                    break;
                }

                var handler = _capabilities.StageEventHandlers.Resolve(
                    stageEvent.Type,
                    $"stage '{_definition.Id}' event[{index}]");
                handler.Execute(
                    world,
                    definitions,
                    stageEvent,
                    spawnIndex,
                    _enemyFactory);
                _spawnedCounts[index]++;
                telemetry.EnemiesSpawned++;
            }
        }
    }

    private void SpawnDueEvents(
        World world,
        CompiledCatalog definitions,
        SimulationTelemetry telemetry)
    {
        if (_programRuntime is not null) return;
        var compiledDefinition = _compiledDefinition ?? throw new InvalidOperationException(
            "A compiled stage system is required for compiled execution.");
        for (var index = 0; index < compiledDefinition.Events.Count; index++)
        {
            var compiledEvent = compiledDefinition.Events[index];
            var stageEvent = compiledEvent.Definition;
            while (_spawnedCounts[index] < stageEvent.Count)
            {
                var spawnIndex = _spawnedCounts[index];
                var spawnTime = stageEvent.Time + (spawnIndex * stageEvent.SpawnInterval);
                if (spawnTime > _elapsed) break;
                compiledEvent.Handler.Execute(world, definitions, compiledEvent, spawnIndex, _enemyFactory);
                _spawnedCounts[index]++;
                telemetry.EnemiesSpawned++;
            }
        }
    }
}

internal sealed record StageSystemCheckpoint(
    StageDefinition Definition,
    CompiledStageDefinition? CompiledDefinition,
    int BossesKilledAtStart,
    int[] SpawnedCounts,
    IReadOnlyList<string> CompletedBossIds,
    double Elapsed,
    long ElapsedTicks,
    long WorldFrameAtStart,
    int CompatibilityScaleQ16,
    long CompatibilityTimeQ16,
    StageProgramCheckpoint? Program);

public readonly record struct CollisionPair(Entity Bullet, Entity Target);

public sealed class CollisionSystem
{
    public IReadOnlyList<CollisionPair> Detect(World world)
    {
        var colliders = world.Query<TransformComponent, ColliderComponent>()
            .Where(static entity => !entity.Has<PendingDestroyComponent>())
            .ToArray();
        var collisions = new List<CollisionPair>();

        foreach (var bullet in colliders.Where(static entity => entity.Has<BulletComponent>()))
        {
            var bulletState = bullet.Get<BulletComponent>();
            var bulletPosition = bullet.Get<TransformComponent>().Position;
            var bulletRadius = bullet.Get<ColliderComponent>().Radius;

            foreach (var target in colliders.Where(entity =>
                         entity.Get<ColliderComponent>().Layer == bulletState.TargetLayer))
            {
                var targetPosition = target.Get<TransformComponent>().Position;
                var combinedRadius = bulletRadius + target.Get<ColliderComponent>().Radius;
                if (Vector2.DistanceSquared(bulletPosition, targetPosition) <= combinedRadius * combinedRadius)
                {
                    collisions.Add(new CollisionPair(bullet, target));
                }
            }
        }

        return collisions;
    }
}

public readonly record struct DamageEvent(Entity Target, int Amount, int? SourceEntityId = null);

public sealed class BulletHitSystem
{
    public IReadOnlyList<DamageEvent> Update(
        IReadOnlyList<CollisionPair> collisions,
        SimulationTelemetry telemetry)
    {
        var damageEvents = new List<DamageEvent>();
        foreach (var collision in collisions)
        {
            if (collision.Bullet.Has<PendingDestroyComponent>())
            {
                continue;
            }

            damageEvents.Add(new DamageEvent(
                collision.Target,
                collision.Bullet.Get<DamageComponent>().Value,
                collision.Bullet.Id));
            collision.Bullet.Add(new PendingDestroyComponent());
            telemetry.CollisionsDetected++;
        }

        return damageEvents;
    }
}

public sealed class BombSystem
{
    private const float EffectDuration = 0.45f;
    private const float ForwardDropDistanceMultiplier = 1.25f;
    private bool _bombWasPressed;

    internal bool CaptureCheckpoint() => _bombWasPressed;
    internal void RestoreCheckpoint(bool value) => _bombWasPressed = value;

    public IReadOnlyList<DamageEvent> Update(
        World world,
        IInputState input,
        float effectRadius,
        SimulationTelemetry telemetry,
        GameEventBuffer? events = null)
    {
        var activation = TryUseBomb(world, input, effectRadius, telemetry, events);
        if (activation is null)
        {
            return Array.Empty<DamageEvent>();
        }

        foreach (var bullet in world.Query<BulletComponent, ColliderComponent>().ToArray())
        {
            if (!bullet.Has<PendingDestroyComponent>() &&
                bullet.Get<ColliderComponent>().Layer == CollisionLayer.EnemyBullet)
            {
                bullet.Add(new PendingDestroyComponent());
                telemetry.EnemyBulletsCleared++;
            }
        }

        return CreateEffectAndDamage(world, activation.Player, activation.EffectPosition, effectRadius);
    }

    public IReadOnlyList<DamageEvent> Update(
        World world,
        ProjectileStore projectiles,
        IInputState input,
        float effectRadius,
        SimulationTelemetry telemetry,
        GameEventBuffer events,
        int bombCost = 1,
        float invincibilitySeconds = 0,
        ScopedResourceStore? resources = null,
        ResourceHandle? sharedResource = null)
    {
        ArgumentNullException.ThrowIfNull(projectiles);
        ArgumentNullException.ThrowIfNull(events);
        var activation = TryUseBomb(
            world,
            input,
            effectRadius,
            telemetry,
            events,
            bombCost,
            invincibilitySeconds,
            BombUsageKind.Manual,
            resources,
            sharedResource);
        if (activation is null)
        {
            return Array.Empty<DamageEvent>();
        }

        ClearEnemyProjectiles(projectiles, telemetry, events);

        return CreateEffectAndDamage(world, activation.Player, activation.EffectPosition, effectRadius);
    }

    public IReadOnlyList<DamageEvent> UseAutoBomb(
        World world,
        ProjectileStore projectiles,
        Entity player,
        float effectRadius,
        int bombCost,
        float invincibilitySeconds,
        SimulationTelemetry telemetry,
        GameEventBuffer events,
        ScopedResourceStore? resources = null,
        ResourceHandle? sharedResource = null)
    {
        ArgumentNullException.ThrowIfNull(player);
        var bombs = player.Get<BombComponent>();
        if (bombCost <= 0 ||
            (sharedResource is { } resource
                ? resources is null || resources.Get(resource, player.Id) < bombCost
                : bombs.Remaining < bombCost)) return Array.Empty<DamageEvent>();
        var effectPosition = player.Get<TransformComponent>().Position;
        if (!ConsumeBomb(player, effectPosition, bombCost, invincibilitySeconds, BombUsageKind.Auto,
            telemetry, events, resources, sharedResource)) return Array.Empty<DamageEvent>();
        ClearEnemyProjectiles(projectiles, telemetry, events);
        return CreateEffectAndDamage(world, player, effectPosition, effectRadius);
    }

    public void Reset(bool bombPressed) => _bombWasPressed = bombPressed;

    private BombActivation? TryUseBomb(
        World world,
        IInputState input,
        float effectRadius,
        SimulationTelemetry telemetry,
        GameEventBuffer? events,
        int bombCost = 1,
        float invincibilitySeconds = 0,
        BombUsageKind kind = BombUsageKind.Manual,
        ScopedResourceStore? resources = null,
        ResourceHandle? sharedResource = null)
    {
        if (!input.Bomb)
        {
            _bombWasPressed = false;
            return null;
        }

        if (_bombWasPressed)
        {
            return null;
        }

        _bombWasPressed = true;
        var player = world.Query<PlayerComponent, BombComponent, TransformComponent>()
            .FirstOrDefault(static entity => !entity.Has<PendingDestroyComponent>());
        if (player is null)
        {
            return null;
        }

        if (player.TryGet<PlayerLifeCycleComponent>(out var lifeCycle) && !lifeCycle.CanAct)
        {
            return null;
        }

        var bombs = player.Get<BombComponent>();
        if (bombCost <= 0 ||
            (sharedResource is { } resource
                ? resources is null || resources.Get(resource, player.Id) < bombCost
                : bombs.Remaining < bombCost))
        {
            return null;
        }

        var effectPosition = GetEffectPosition(player, effectRadius, kind);
        if (!ConsumeBomb(player, effectPosition, bombCost, invincibilitySeconds, kind,
            telemetry, events, resources, sharedResource)) return null;
        return new BombActivation(player, effectPosition);
    }

    private static bool ConsumeBomb(
        Entity player,
        Vector2 effectPosition,
        int bombCost,
        float invincibilitySeconds,
        BombUsageKind kind,
        SimulationTelemetry telemetry,
        GameEventBuffer? events,
        ScopedResourceStore? resources = null,
        ResourceHandle? sharedResource = null)
    {
        if (sharedResource is { } resource)
        {
            if (resources is null || !resources.TryConsume(resource, bombCost, player.Id)) return false;
            if (resources.GetDefinition(resource).Adapter == StandardResourceAdapter.Bomb)
                player.Get<BombComponent>().Remaining = checked((int)resources.Get(resource, player.Id));
        }
        else
        {
            player.Get<BombComponent>().Remaining -= bombCost;
        }
        telemetry.BombsUsed++;
        if (kind == BombUsageKind.Auto) telemetry.AutoBombsUsed++;
        if (invincibilitySeconds > 0 && player.TryGet<InvincibilityComponent>(out var invincibility))
        {
            invincibility.Remaining = Math.Max(invincibility.Remaining, invincibilitySeconds);
        }

        events?.Publish((frame, sequence) => new BombUsedEvent(
            frame,
            sequence,
            player.Id,
            kind,
            effectPosition.X,
            effectPosition.Y));
        return true;
    }

    internal static void ClearEnemyProjectiles(
        ProjectileStore projectiles,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        for (var index = 0; index < projectiles.ActiveCount; index++)
        {
            if (projectiles.TeamAt(index) != ProjectileTeam.Enemy ||
                projectiles.IsPendingRemovalAt(index) ||
                !projectiles.CanBeCancelledAt(index)) continue;
            projectiles.QueueRemoveAt(index);
            telemetry.EnemyBulletsCleared++;
            var projectileId = projectiles.IdAt(index);
            events.Publish((frame, sequence) => new ProjectileCancelledEvent(frame, sequence, projectileId));
        }
    }

    private static IReadOnlyList<DamageEvent> CreateEffectAndDamage(
        World world,
        Entity player,
        Vector2 effectPosition,
        float effectRadius)
    {
        var bombs = player.Get<BombComponent>();

        world.CreateEntity()
            .Add(new TransformComponent(effectPosition))
            .Add(new ExplosionComponent(effectRadius, EffectDuration));

        return world.Query<EnemyComponent, HealthComponent>()
            .Where(static enemy => !enemy.Has<PendingDestroyComponent>())
            .Select(enemy => new DamageEvent(enemy, bombs.Damage))
            .ToArray();
    }

    private static Vector2 GetEffectPosition(Entity player, float effectRadius, BombUsageKind kind)
    {
        var playerPosition = player.Get<TransformComponent>().Position;
        if (kind == BombUsageKind.Auto) return playerPosition;

        return new Vector2(
            playerPosition.X,
            Math.Max(0, playerPosition.Y - (effectRadius * ForwardDropDistanceMultiplier)));
    }

    private sealed record BombActivation(Entity Player, Vector2 EffectPosition);
}

public sealed class DamageSystem
{
    public void Update(
        IReadOnlyList<DamageEvent> damageEvents,
        SimulationTelemetry telemetry,
        GameEventBuffer? events = null,
        World? world = null)
    {
        foreach (var damageEvent in damageEvents)
        {
            if (damageEvent.Target.Has<PendingDestroyComponent>())
            {
                continue;
            }

            if (damageEvent.Target.TryGet<InvincibilityComponent>(out var invincibility) &&
                invincibility.Remaining > 0)
            {
                continue;
            }

            if (damageEvent.Target.Has<PlayerComponent>() &&
                damageEvent.Target.Has<PlayerLifeCycleComponent>())
            {
                continue;
            }

            if (damageEvent.Target.TryGet<ActorPartComponent>(out var inactivePart) &&
                (!inactivePart.Enabled || inactivePart.HealthPolicy == ActorPartHealthPolicy.Indestructible))
            {
                continue;
            }

            if (damageEvent.Target.TryGet<HitFlashComponent>(out var hitFlash))
            {
                hitFlash.Remaining = 0.1f;
            }
            else
            {
                damageEvent.Target.Add(new HitFlashComponent(0.1f));
            }

            if (invincibility is not null)
            {
                invincibility.Remaining = invincibility.Duration;
            }

            telemetry.DamageEventsApplied++;
            if (damageEvent.Target.Has<PlayerComponent>())
            {
                var lives = damageEvent.Target.Get<LivesComponent>();
                lives.Remaining--;
                telemetry.PlayerDamageEventsApplied++;
                events?.Publish((frame, sequence) => new PlayerHitEvent(
                    frame,
                    sequence,
                    damageEvent.Target.Id,
                    damageEvent.SourceEntityId));
                if (lives.Remaining <= 0)
                {
                    damageEvent.Target.Add(new PendingDestroyComponent());
                }

                continue;
            }

            var resolvedTarget = damageEvent.Target;
            Entity? forwardedTarget = null;
            var forwardedDamage = 0;
            if (damageEvent.Target.TryGet<ActorPartComponent>(out var actorPart))
            {
                var root = world is null ? null : OptionFollowSystem.FindEntity(world, actorPart.RootEntityId);
                if (root is null || root.Has<PendingDestroyComponent>()) continue;
                if (actorPart.HealthPolicy == ActorPartHealthPolicy.Shared)
                    resolvedTarget = root;
                else if (actorPart.DamageForwardingRatio > 0)
                {
                    forwardedTarget = root;
                    forwardedDamage = Math.Max(0, (int)MathF.Round(
                        damageEvent.Amount * actorPart.DamageForwardingRatio,
                        MidpointRounding.AwayFromZero));
                }
            }

            var health = resolvedTarget.Get<HealthComponent>();
            health.Current -= damageEvent.Amount;
            if (resolvedTarget.Has<EnemyComponent>())
            {
                events?.Publish((frame, sequence) => new EnemyDamagedEvent(
                    frame,
                    sequence,
                    resolvedTarget.Id,
                    damageEvent.Amount));
            }

            if (forwardedTarget is not null && forwardedDamage > 0)
            {
                var forwardedHealth = forwardedTarget.Get<HealthComponent>();
                forwardedHealth.Current -= forwardedDamage;
                events?.Publish((frame, sequence) => new EnemyDamagedEvent(
                    frame, sequence, forwardedTarget.Id, forwardedDamage));
                if (forwardedHealth.Current <= 0 &&
                    !(forwardedTarget.TryGet<BossComponent>(out var forwardedBoss) && forwardedBoss.IsManaged))
                {
                    forwardedTarget.Add(new PendingDestroyComponent());
                    telemetry.EnemiesKilled++;
                    events?.Publish((frame, sequence) => new EnemyDestroyedEvent(
                        frame,
                        sequence,
                        forwardedTarget.Id,
                        forwardedTarget.Get<EnemyComponent>().DefinitionId,
                        forwardedTarget.Get<ScoreValueComponent>().Value,
                        DistanceToPlayer(world, forwardedTarget)));
                }
            }

            if (health.Current <= 0)
            {
                if (resolvedTarget.TryGet<BossComponent>(out var boss) && boss.IsManaged)
                {
                    continue;
                }

                resolvedTarget.Add(new PendingDestroyComponent());
                if (resolvedTarget.TryGet<ActorPartComponent>(out var destroyedPart))
                {
                    events?.Publish((frame, sequence) => new ActorPartDestroyedEvent(
                        frame,
                        sequence,
                        destroyedPart.RootEntityId,
                        resolvedTarget.Id,
                        destroyedPart.PartId,
                        resolvedTarget.Get<TransformComponent>().Position));
                    if (!string.IsNullOrWhiteSpace(destroyedPart.DestroySignal))
                        events?.Publish((frame, sequence) => new RuleSignalEvent(
                            frame, sequence, destroyedPart.DestroySignal, destroyedPart.PartId, false));
                }
                else if (resolvedTarget.Has<EnemyComponent>())
                {
                    telemetry.EnemiesKilled++;
                    events?.Publish((frame, sequence) => new EnemyDestroyedEvent(
                        frame,
                        sequence,
                        resolvedTarget.Id,
                        resolvedTarget.Get<EnemyComponent>().DefinitionId,
                        resolvedTarget.Get<ScoreValueComponent>().Value,
                        DistanceToPlayer(world, resolvedTarget)));
                    if (resolvedTarget.Has<BossComponent>())
                    {
                        telemetry.BossesKilled++;
                    }
                }
            }
        }
    }

    private static float? DistanceToPlayer(World? world, Entity enemy)
    {
        if (world is null) return null;
        var player = world.Query<PlayerComponent, TransformComponent>()
            .Where(static entity => !entity.Has<PendingDestroyComponent>())
            .OrderBy(static entity => entity.Id)
            .FirstOrDefault();
        return player is null
            ? null
            : Vector2.Distance(
                enemy.Get<TransformComponent>().Position,
                player.Get<TransformComponent>().Position);
    }
}

public sealed class InvincibilitySystem
{
    public void Update(World world, float deltaTime)
    {
        foreach (var entity in world.Query<InvincibilityComponent>())
        {
            var invincibility = entity.Get<InvincibilityComponent>();
            invincibility.Remaining = Math.Max(0, invincibility.Remaining - deltaTime);
        }
    }
}

public sealed class FeedbackSystem
{
    private const float ExplosionDuration = 0.35f;

    public void Update(World world, float deltaTime)
    {
        foreach (var entity in world.Query<HitFlashComponent>().ToArray())
        {
            var flash = entity.Get<HitFlashComponent>();
            flash.Remaining -= deltaTime;
            if (flash.Remaining <= 0)
            {
                entity.Remove<HitFlashComponent>();
            }
        }

        foreach (var entity in world.Query<ExplosionComponent>().ToArray())
        {
            var explosion = entity.Get<ExplosionComponent>();
            explosion.Remaining -= deltaTime;
            if (explosion.Remaining <= 0)
            {
                entity.Add(new PendingDestroyComponent());
            }
        }

        foreach (var entity in world.Query<PendingDestroyComponent, TransformComponent>().ToArray())
        {
            var destroyedEnemy = entity.Has<EnemyComponent>() &&
                entity.TryGet<HealthComponent>(out var health) && health.Current <= 0;
            var destroyedPlayer = entity.Has<PlayerComponent>() &&
                entity.TryGet<LivesComponent>(out var lives) && lives.Remaining <= 0;
            if (!destroyedEnemy && !destroyedPlayer)
            {
                continue;
            }

            var radius = entity.TryGet<ColliderComponent>(out var collider) ? collider.Radius * 2 : 20;
            world.CreateEntity()
                .Add(new TransformComponent(entity.Get<TransformComponent>().Position))
                .Add(new ExplosionComponent(radius, ExplosionDuration));
        }
    }
}

public sealed class LifetimeSystem
{
    public void Update(World world, float deltaTime)
    {
        foreach (var entity in world.Query<LifetimeComponent>().ToArray())
        {
            if (entity.Has<PendingDestroyComponent>())
            {
                continue;
            }
            if (entity.TryGet<ActorPartComponent>(out var actorPart) && !actorPart.Enabled) continue;

            var lifetime = entity.Get<LifetimeComponent>();
            lifetime.Remaining -= deltaTime;
            if (lifetime.Remaining <= 0)
            {
                entity.Add(new PendingDestroyComponent());
            }
        }
    }
}

public sealed class CleanupSystem
{
    public void Update(World world)
    {
        foreach (var entity in world.Query<PendingDestroyComponent>().ToArray())
        {
            world.DestroyEntity(entity);
        }
    }
}

public enum RenderKind
{
    Player,
    Enemy,
    PlayerBullet,
    EnemyBullet,
    Explosion,
    Option,
    Laser,
    LockMarker,
    PlayerHitbox,
    GrazeRing,
    Item
}

public readonly record struct RenderItem(
    int EntityId,
    RenderKind Kind,
    Vector2 Position,
    float Radius,
    float HealthFraction,
    bool IsFlashing = false,
    float EffectProgress = 0,
    string? VisualId = null,
    Vector2 Size = default,
    float Rotation = 0,
    string? BossName = null,
    string? BossPhaseName = null,
    float? BossRemainingTime = null,
    bool BossWarning = false,
    Vector2 PreviousPosition = default,
    string? AnimationId = null,
    float Scale = 1,
    uint Tint = uint.MaxValue,
    int Layer = 20,
    bool FlipX = false,
    bool FlipY = false,
    string? BossDefinitionId = null);

public sealed class TransformHistorySystem
{
    public void BeginTick(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        foreach (var entity in world.Query<TransformComponent>())
        {
            var transform = entity.Get<TransformComponent>();
            transform.PreviousPosition = transform.Position;
        }
    }
}

/// <summary>Transforms runtime state into renderer-neutral draw data.</summary>
public sealed class RenderSystem
{
    public IReadOnlyList<RenderItem> Capture(World world) => CaptureCore(world, projectiles: null);

    public IReadOnlyList<RenderItem> Capture(World world, ProjectileStore projectiles)
    {
        ArgumentNullException.ThrowIfNull(projectiles);
        return CaptureCore(world, projectiles);
    }

    private static IReadOnlyList<RenderItem> CaptureCore(World world, ProjectileStore? projectiles)
    {
        ArgumentNullException.ThrowIfNull(world);
        var items = new List<RenderItem>();
        foreach (var entity in world.Query<TransformComponent, ColliderComponent>())
        {
            if (entity.Has<PendingDestroyComponent>())
            {
                continue;
            }

            if (entity.TryGet<PlayerLifeCycleComponent>(out var lifeCycle) &&
                lifeCycle.State is PlayerLifeCycleState.Dying or
                    PlayerLifeCycleState.Respawning or
                    PlayerLifeCycleState.GameOverPending)
            {
                continue;
            }

            var kind = GetRenderKind(entity);
            if (kind is null)
            {
                continue;
            }

            var healthFraction = entity.TryGet<HealthComponent>(out var health)
                ? Math.Clamp((float)health.Current / health.Maximum, 0, 1)
                : 1;
            entity.TryGet<BossComponent>(out var boss);
            entity.TryGet<ActorPresentationComponent>(out var actorPresentation);
            var visualId = actorPresentation?.VisualId ?? (entity.TryGet<ShipComponent>(out var shipComponent)
                ? shipComponent.VisualId ?? shipComponent.DefinitionId
                : entity.TryGet<EnemyComponent>(out var enemyComponent)
                    ? enemyComponent.DefinitionId
                    : null);
            var transform = entity.Get<TransformComponent>();
            items.Add(new RenderItem(
                entity.Id,
                kind.Value,
                transform.Position,
                entity.Get<ColliderComponent>().Radius,
                healthFraction,
                entity.Has<HitFlashComponent>() ||
                (entity.TryGet<InvincibilityComponent>(out var invincibility) && invincibility.Remaining > 0),
                VisualId: visualId,
                BossName: boss?.IsManaged == true ? boss.DisplayName : null,
                BossDefinitionId: boss?.IsManaged == true ? boss.DefinitionId : null,
                BossPhaseName: boss?.IsManaged == true ? boss.PhaseDisplayName : null,
                BossRemainingTime: boss?.IsManaged == true ? boss.RemainingTime : null,
                BossWarning: boss?.IsWarning == true,
                PreviousPosition: transform.PreviousPosition,
                AnimationId: actorPresentation?.AnimationStateId is null
                    ? actorPresentation?.AnimationId
                    : $"{actorPresentation.AnimationStateId}:{actorPresentation.SemanticState}",
                Layer: actorPresentation?.RenderLayer ?? (kind == RenderKind.Enemy ? 25 : 30)));
        }

        foreach (var entity in world.Query<TransformComponent, ExplosionComponent>())
        {
            var explosion = entity.Get<ExplosionComponent>();
            var progress = Math.Clamp(1 - (explosion.Remaining / explosion.Duration), 0, 1);
            var transform = entity.Get<TransformComponent>();
            items.Add(new RenderItem(
                entity.Id,
                RenderKind.Explosion,
                transform.Position,
                explosion.MaxRadius * Math.Max(0.2f, progress),
                1,
                EffectProgress: progress,
                VisualId: "explosion",
                PreviousPosition: transform.PreviousPosition,
                AnimationId: "explosion",
                Layer: 50));
        }

        foreach (var entity in world.Query<TransformComponent, OptionUnitComponent>())
        {
            var option = entity.Get<OptionUnitComponent>();
            var owner = OptionFollowSystem.FindEntity(world, option.OwnerEntityId);
            if (owner?.TryGet<PlayerLifeCycleComponent>(out var lifeCycle) == true && !lifeCycle.CanAct) continue;
            var transform = entity.Get<TransformComponent>();
            items.Add(new RenderItem(
                entity.Id,
                RenderKind.Option,
                transform.Position,
                option.Radius,
                1,
                VisualId: option.VisualId ?? option.DefinitionId,
                PreviousPosition: transform.PreviousPosition,
                Layer: 29));
        }

        foreach (var entity in world.Query<TransformComponent, ItemComponent>())
        {
            if (entity.Has<PendingDestroyComponent>()) continue;
            var item = entity.Get<ItemComponent>();
            var transform = entity.Get<TransformComponent>();
            items.Add(new RenderItem(
                entity.Id,
                RenderKind.Item,
                transform.Position,
                6,
                1,
                VisualId: item.VisualId,
                PreviousPosition: transform.PreviousPosition,
                Layer: 35));
        }

        foreach (var entity in world.Query<TransformComponent, LaserComponent>())
        {
            var laser = entity.Get<LaserComponent>();
            var direction = Vector2.Normalize(laser.Direction);
            var transform = entity.Get<TransformComponent>();
            items.Add(new RenderItem(
                entity.Id,
                RenderKind.Laser,
                transform.Position + (direction * laser.Length * 0.5f),
                laser.Width,
                1,
                VisualId: laser.VisualId,
                Size: new Vector2(laser.Width * 2, laser.Length),
                Rotation: MathF.Atan2(direction.Y, direction.X) + (MathF.PI * 0.5f),
                PreviousPosition: transform.PreviousPosition + (direction * laser.Length * 0.5f),
                Layer: 32));
        }

        foreach (var player in world.Query<TransformComponent, ShipComponent>())
        {
            var ship = player.Get<ShipComponent>();
            if (ship.IsFocused)
            {
                var transform = player.Get<TransformComponent>();
                items.Add(new RenderItem(
                    player.Id,
                    RenderKind.GrazeRing,
                    transform.Position,
                    ship.GrazeRadius,
                    1,
                    PreviousPosition: transform.PreviousPosition,
                    Layer: 69));
                items.Add(new RenderItem(
                    player.Id,
                    RenderKind.PlayerHitbox,
                    transform.Position,
                    ship.HitRadius,
                    1,
                    PreviousPosition: transform.PreviousPosition,
                    Layer: 70));
            }
        }

        foreach (var owner in world.Query<WeaponRuntimeComponent>())
        {
            foreach (var state in owner.Get<WeaponRuntimeComponent>().States.Values)
            {
                foreach (var targetId in state.LockedTargetEntityIds)
                {
                    var target = OptionFollowSystem.FindEntity(world, targetId);
                    if (target is null || !target.TryGet<TransformComponent>(out var transform)) continue;
                    items.Add(new RenderItem(
                        targetId,
                        RenderKind.LockMarker,
                        transform.Position,
                        12,
                        1,
                        PreviousPosition: transform.PreviousPosition,
                    Layer: 65));
                }
            }
        }

        if (projectiles is not null)
        {
            for (var index = 0; index < projectiles.ActiveCount; index++)
            {
                if (projectiles.IsPendingRemovalAt(index))
                {
                    continue;
                }

                items.Add(new RenderItem(
                    projectiles.IdAt(index),
                    projectiles.TeamAt(index) == ProjectileTeam.Player
                        ? RenderKind.PlayerBullet
                        : RenderKind.EnemyBullet,
                    projectiles.PositionAt(index),
                    projectiles.HitRadiusAt(index),
                    1,
                    VisualId: projectiles.VisualIdAt(index),
                    PreviousPosition: projectiles.PreviousPositionAt(index),
                    Layer: projectiles.TeamAt(index) == ProjectileTeam.Player ? 40 : 60));
            }
        }

        return items;
    }

    private static RenderKind? GetRenderKind(Entity entity)
    {
        if (entity.Has<PlayerComponent>()) return RenderKind.Player;
        if (entity.Has<EnemyComponent>() || entity.Has<ActorPartComponent>()) return RenderKind.Enemy;
        if (!entity.TryGet<BulletComponent>(out _)) return null;
        return entity.Get<ColliderComponent>().Layer == CollisionLayer.PlayerBullet
            ? RenderKind.PlayerBullet
            : RenderKind.EnemyBullet;
    }
}
