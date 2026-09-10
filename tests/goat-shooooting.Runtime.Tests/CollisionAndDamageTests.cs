using System.Numerics;
using GoatShooooting.Core;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class CollisionAndDamageTests
{
    [Fact]
    public void OverlappingPlayerBulletAndEnemyProduceCollision()
    {
        var world = CreateOverlappingWorld(out var bullet, out var enemy);

        var collision = Assert.Single(new CollisionSystem().Detect(world));

        Assert.Same(bullet, collision.Bullet);
        Assert.Same(enemy, collision.Target);
    }

    [Fact]
    public void BulletHitReducesHealthAndDeadEnemyIsRemoved()
    {
        var world = CreateOverlappingWorld(out _, out var enemy);
        var telemetry = new SimulationTelemetry();
        var collisions = new CollisionSystem().Detect(world);
        var damage = new BulletHitSystem().Update(collisions, telemetry);

        new DamageSystem().Update(damage, telemetry);

        Assert.Equal(0, enemy.Get<HealthComponent>().Current);
        Assert.True(enemy.Has<PendingDestroyComponent>());
        Assert.Equal(1, telemetry.DamageEventsApplied);
        Assert.Equal(1, telemetry.EnemiesKilled);

        new CleanupSystem().Update(world);
        Assert.Empty(world.Query<EnemyComponent>());
    }

    private static World CreateOverlappingWorld(out Entity bullet, out Entity enemy)
    {
        var world = new World();
        bullet = world.CreateEntity()
            .Add(new TransformComponent(Vector2.Zero))
            .Add(new ColliderComponent(3, CollisionLayer.PlayerBullet))
            .Add(new BulletComponent("bullet", CollisionLayer.Enemy))
            .Add(new DamageComponent(10));
        enemy = world.CreateEntity()
            .Add(new TransformComponent(new Vector2(10, 0)))
            .Add(new ColliderComponent(10, CollisionLayer.Enemy))
            .Add(new HealthComponent(10))
            .Add(new EnemyComponent("enemy"));
        return world;
    }
}
