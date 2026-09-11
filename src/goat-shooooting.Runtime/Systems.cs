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
            var direction = ownerLayer == CollisionLayer.Player ? -Vector2.UnitY : Vector2.UnitY;
            _bulletFactory.Create(
                world,
                bullet,
                entity.Get<TransformComponent>().Position,
                direction,
                ownerLayer.Value);
            holder.CooldownRemaining = weapon.Cooldown;
            telemetry.BulletsSpawned++;
            if (ownerLayer == CollisionLayer.Enemy)
            {
                telemetry.EnemyBulletsSpawned++;
            }
        }
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
    private readonly HashSet<int> _executedEvents = new();
    private double _elapsed;

    public double Elapsed => _elapsed;
    public bool IsComplete => _executedEvents.Count == _definition.Events.Count;

    public void Update(World world, DefinitionCatalog definitions, float deltaTime, SimulationTelemetry telemetry)
    {
        _elapsed += deltaTime;
        for (var index = 0; index < _definition.Events.Count; index++)
        {
            var stageEvent = _definition.Events[index];
            if (_executedEvents.Contains(index) || stageEvent.Time > _elapsed)
            {
                continue;
            }

            _enemyFactory.Create(
                world,
                definitions.GetEnemy(stageEvent.EnemyId),
                new Vector2(stageEvent.X, stageEvent.Y));
            _executedEvents.Add(index);
            telemetry.EnemiesSpawned++;
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

            var health = damageEvent.Target.Get<HealthComponent>();
            health.Current -= damageEvent.Amount;
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
                telemetry.PlayerDamageEventsApplied++;
            }

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

        foreach (var entity in world.Query<PendingDestroyComponent, TransformComponent, HealthComponent>().ToArray())
        {
            if (entity.Get<HealthComponent>().Current > 0 ||
                (!entity.Has<EnemyComponent>() && !entity.Has<PlayerComponent>()))
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
