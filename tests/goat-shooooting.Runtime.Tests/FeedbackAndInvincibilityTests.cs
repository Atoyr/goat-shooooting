using System.Numerics;
using GoatShooooting.Core;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class FeedbackAndInvincibilityTests
{
    [Fact]
    public void InvincibilityPreventsRepeatedDamageUntilItExpires()
    {
        var world = new World();
        var player = world.CreateEntity()
            .Add(new PlayerComponent("player", 100))
            .Add(new HealthComponent(30))
            .Add(new InvincibilityComponent(1));
        var damage = new[] { new DamageEvent(player, 10) };
        var telemetry = new SimulationTelemetry();
        var damageSystem = new DamageSystem();

        damageSystem.Update(damage, telemetry);
        damageSystem.Update(damage, telemetry);

        Assert.Equal(20, player.Get<HealthComponent>().Current);
        Assert.Equal(1, telemetry.PlayerDamageEventsApplied);
        Assert.True(player.Get<InvincibilityComponent>().Remaining > 0);

        new InvincibilitySystem().Update(world, 1);
        damageSystem.Update(damage, telemetry);

        Assert.Equal(10, player.Get<HealthComponent>().Current);
        Assert.Equal(2, telemetry.PlayerDamageEventsApplied);
    }

    [Fact]
    public void DestroyedEnemyCreatesTransientExplosionRenderItem()
    {
        var world = new World();
        var enemy = world.CreateEntity()
            .Add(new EnemyComponent("enemy"))
            .Add(new TransformComponent(new Vector2(50, 60)))
            .Add(new ColliderComponent(10, CollisionLayer.Enemy))
            .Add(new HealthComponent(10));
        enemy.Get<HealthComponent>().Current = 0;
        enemy.Add(new PendingDestroyComponent());
        var system = new FeedbackSystem();

        system.Update(world, 0);
        new CleanupSystem().Update(world);

        var explosion = Assert.Single(new RenderSystem().Capture(world));
        Assert.Equal(RenderKind.Explosion, explosion.Kind);
        Assert.Equal(new Vector2(50, 60), explosion.Position);

        system.Update(world, 0.36f);
        new CleanupSystem().Update(world);
        Assert.Empty(world.Query<ExplosionComponent>());
    }
}
