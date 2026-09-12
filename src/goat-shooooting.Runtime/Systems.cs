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
            var movement = new Vector2(
                Math.Clamp(input.MoveX, -1f, 1f),
                Math.Clamp(input.MoveY, -1f, 1f));
            if (movement.LengthSquared() > 1f)
            {
                movement = Vector2.Normalize(movement);
            }

            entity.Get<VelocityComponent>().Value = movement * entity.Get<PlayerComponent>().Speed;
        }
    }
}

public sealed class WeaponSystem(BulletFactory bulletFactory)
{
    private readonly BulletFactory _bulletFactory = bulletFactory ?? throw new ArgumentNullException(nameof(bulletFactory));

    public void Update(
        World world,
        DefinitionCatalog definitions,
        IInputState input,
        float deltaTime,
        SimulationTelemetry telemetry)
    {
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
            foreach (var direction in GetDirections(weapon, holder, baseDirection))
            {
                _bulletFactory.Create(
                    world,
                    bullet,
                    entity.Get<TransformComponent>().Position,
                    direction,
                    ownerLayer.Value);
                telemetry.BulletsSpawned++;
                if (ownerLayer == CollisionLayer.Enemy)
                {
                    telemetry.EnemyBulletsSpawned++;
                }
            }

            AdvancePattern(weapon, holder);
            holder.CooldownRemaining = weapon.Cooldown;
        }
    }

    private static IEnumerable<Vector2> GetDirections(
        WeaponDefinition weapon,
        WeaponHolderComponent holder,
        Vector2 baseDirection)
    {
        if (string.Equals(weapon.FirePattern, "spread", StringComparison.Ordinal))
        {
            for (var projectileIndex = 0; projectileIndex < weapon.ProjectileCount; projectileIndex++)
            {
                var normalizedOffset = weapon.ProjectileCount == 1
                    ? 0
                    : ((float)projectileIndex / (weapon.ProjectileCount - 1)) - 0.5f;
                yield return Rotate(
                    baseDirection,
                    normalizedOffset * weapon.SpreadDegrees * (MathF.PI / 180));
            }

            yield break;
        }

        var armSpacing = 360f / weapon.ProjectileCount;
        for (var arm = 0; arm < weapon.ProjectileCount; arm++)
        {
            yield return RotateDegrees(baseDirection, holder.PatternAngleDegrees + (arm * armSpacing));
        }

        if (string.Equals(weapon.FirePattern, "double-washing-machine", StringComparison.Ordinal))
        {
            for (var arm = 0; arm < weapon.ProjectileCount; arm++)
            {
                yield return RotateDegrees(
                    baseDirection,
                    -holder.PatternAngleDegrees + ((arm + 0.5f) * armSpacing));
            }
        }
    }

    private static void AdvancePattern(WeaponDefinition weapon, WeaponHolderComponent holder)
    {
        if (string.Equals(weapon.FirePattern, "spread", StringComparison.Ordinal))
        {
            return;
        }

        holder.PatternAngleDegrees = NormalizeDegrees(
            holder.PatternAngleDegrees + (weapon.RotationDegreesPerShot * holder.PatternDirection));
        holder.ShotsSinceDirectionChange++;
        if (holder.ShotsSinceDirectionChange >= weapon.RotationSwitchShots)
        {
            holder.PatternDirection *= -1;
            holder.ShotsSinceDirectionChange = 0;
        }
    }

    private static Vector2 RotateDegrees(Vector2 vector, float angleDegrees) =>
        Rotate(vector, angleDegrees * (MathF.PI / 180));

    private static float NormalizeDegrees(float angle)
    {
        angle %= 360;
        return angle < 0 ? angle + 360 : angle;
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
            if (entity.Has<PlayerComponent>() || entity.Has<PendingDestroyComponent>())
            {
                continue;
            }

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

public sealed class StageSystem(StageDefinition definition, EnemyFactory enemyFactory)
{
    private readonly StageDefinition _definition = definition ?? throw new ArgumentNullException(nameof(definition));
    private readonly EnemyFactory _enemyFactory = enemyFactory ?? throw new ArgumentNullException(nameof(enemyFactory));
    private readonly int[] _spawnedCounts = new int[definition.Events.Count];
    private double _elapsed;

    public double Elapsed => _elapsed;
    public bool IsComplete => _definition.Events
        .Select((stageEvent, index) => _spawnedCounts[index] >= stageEvent.Count)
        .All(static complete => complete);

    public void Update(World world, DefinitionCatalog definitions, float deltaTime, SimulationTelemetry telemetry)
    {
        _elapsed += deltaTime;
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

                _enemyFactory.Create(
                    world,
                    definitions.GetEnemy(stageEvent.EnemyId),
                    new Vector2(stageEvent.X + (spawnIndex * stageEvent.SpacingX), stageEvent.Y));
                _spawnedCounts[index]++;
                telemetry.EnemiesSpawned++;
            }
        }
    }
}

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

public readonly record struct DamageEvent(Entity Target, int Amount);

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

            damageEvents.Add(new DamageEvent(collision.Target, collision.Bullet.Get<DamageComponent>().Value));
            collision.Bullet.Add(new PendingDestroyComponent());
            telemetry.CollisionsDetected++;
        }

        return damageEvents;
    }
}

public sealed class BombSystem
{
    private const float EffectDuration = 0.45f;
    private bool _bombWasPressed;

    public IReadOnlyList<DamageEvent> Update(
        World world,
        IInputState input,
        float effectRadius,
        SimulationTelemetry telemetry)
    {
        if (!input.Bomb)
        {
            _bombWasPressed = false;
            return Array.Empty<DamageEvent>();
        }

        if (_bombWasPressed)
        {
            return Array.Empty<DamageEvent>();
        }

        _bombWasPressed = true;
        var player = world.Query<PlayerComponent, BombComponent, TransformComponent>()
            .FirstOrDefault(static entity => !entity.Has<PendingDestroyComponent>());
        if (player is null)
        {
            return Array.Empty<DamageEvent>();
        }

        var bombs = player.Get<BombComponent>();
        if (bombs.Remaining <= 0)
        {
            return Array.Empty<DamageEvent>();
        }

        bombs.Remaining--;
        telemetry.BombsUsed++;

        foreach (var bullet in world.Query<BulletComponent, ColliderComponent>().ToArray())
        {
            if (!bullet.Has<PendingDestroyComponent>() &&
                bullet.Get<ColliderComponent>().Layer == CollisionLayer.EnemyBullet)
            {
                bullet.Add(new PendingDestroyComponent());
                telemetry.EnemyBulletsCleared++;
            }
        }

        world.CreateEntity()
            .Add(new TransformComponent(player.Get<TransformComponent>().Position))
            .Add(new ExplosionComponent(effectRadius, EffectDuration));

        return world.Query<EnemyComponent, HealthComponent>()
            .Where(static enemy => !enemy.Has<PendingDestroyComponent>())
            .Select(enemy => new DamageEvent(enemy, bombs.Damage))
            .ToArray();
    }

    public void Reset(bool bombPressed) => _bombWasPressed = bombPressed;
}

public sealed class DamageSystem
{
    public void Update(IReadOnlyList<DamageEvent> damageEvents, SimulationTelemetry telemetry)
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
                if (lives.Remaining <= 0)
                {
                    damageEvent.Target.Add(new PendingDestroyComponent());
                }

                continue;
            }

            var health = damageEvent.Target.Get<HealthComponent>();
            health.Current -= damageEvent.Amount;
            if (health.Current <= 0)
            {
                damageEvent.Target.Add(new PendingDestroyComponent());
                if (damageEvent.Target.Has<EnemyComponent>())
                {
                    telemetry.EnemiesKilled++;
                    telemetry.Score += damageEvent.Target.Get<ScoreValueComponent>().Value;
                }
            }
        }
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
    Explosion
}

public readonly record struct RenderItem(
    int EntityId,
    RenderKind Kind,
    Vector2 Position,
    float Radius,
    float HealthFraction,
    bool IsFlashing = false,
    float EffectProgress = 0);

/// <summary>Transforms runtime state into renderer-neutral draw data.</summary>
public sealed class RenderSystem
{
    public IReadOnlyList<RenderItem> Capture(World world)
    {
        var items = new List<RenderItem>();
        foreach (var entity in world.Query<TransformComponent, ColliderComponent>())
        {
            if (entity.Has<PendingDestroyComponent>())
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
            items.Add(new RenderItem(
                entity.Id,
                kind.Value,
                entity.Get<TransformComponent>().Position,
                entity.Get<ColliderComponent>().Radius,
                healthFraction,
                entity.Has<HitFlashComponent>() ||
                (entity.TryGet<InvincibilityComponent>(out var invincibility) && invincibility.Remaining > 0)));
        }

        foreach (var entity in world.Query<TransformComponent, ExplosionComponent>())
        {
            var explosion = entity.Get<ExplosionComponent>();
            var progress = Math.Clamp(1 - (explosion.Remaining / explosion.Duration), 0, 1);
            items.Add(new RenderItem(
                entity.Id,
                RenderKind.Explosion,
                entity.Get<TransformComponent>().Position,
                explosion.MaxRadius * Math.Max(0.2f, progress),
                1,
                EffectProgress: progress));
        }

        return items;
    }

    private static RenderKind? GetRenderKind(Entity entity)
    {
        if (entity.Has<PlayerComponent>()) return RenderKind.Player;
        if (entity.Has<EnemyComponent>()) return RenderKind.Enemy;
        if (!entity.TryGet<BulletComponent>(out _)) return null;
        return entity.Get<ColliderComponent>().Layer == CollisionLayer.PlayerBullet
            ? RenderKind.PlayerBullet
            : RenderKind.EnemyBullet;
    }
}
