using System.Numerics;
using GoatShooooting.Core;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class WorldBoundsSystemTests
{
    [Fact]
    public void PlayerIsClampedInsideViewportUsingColliderRadius()
    {
        var world = new World();
        var player = world.CreateEntity()
            .Add(new PlayerComponent("player", 100))
            .Add(new TransformComponent(new Vector2(-50, 700)))
            .Add(new ColliderComponent(10, CollisionLayer.Player));

        new PlayerBoundsSystem().Update(world, 800, 600);

        Assert.Equal(new Vector2(10, 590), player.Get<TransformComponent>().Position);
    }

    [Fact]
    public void FullyOffscreenNonPlayerEntitiesAreRemoved()
    {
        var world = new World();
        var enemy = world.CreateEntity()
            .Add(new EnemyComponent("enemy"))
            .Add(new TransformComponent(new Vector2(400, 611)))
            .Add(new ColliderComponent(10, CollisionLayer.Enemy));
        var bullet = world.CreateEntity()
            .Add(new BulletComponent("bullet", CollisionLayer.Enemy))
            .Add(new TransformComponent(new Vector2(-4, 300)))
            .Add(new ColliderComponent(3, CollisionLayer.PlayerBullet));

        new OutOfBoundsSystem().Update(world, 800, 600);
        new CleanupSystem().Update(world);

        Assert.DoesNotContain(enemy, world.Entities);
        Assert.DoesNotContain(bullet, world.Entities);
    }
}
