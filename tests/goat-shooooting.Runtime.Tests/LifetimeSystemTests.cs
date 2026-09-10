using GoatShooooting.Core;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class LifetimeSystemTests
{
    [Fact]
    public void BulletIsRemovedAfterLifetimeExpires()
    {
        var world = new World();
        world.CreateEntity().Add(new BulletComponent("bullet", CollisionLayer.Enemy)).Add(new LifetimeComponent(1));

        new LifetimeSystem().Update(world, 1.01f);
        new CleanupSystem().Update(world);

        Assert.Empty(world.Query<BulletComponent>());
    }
}
