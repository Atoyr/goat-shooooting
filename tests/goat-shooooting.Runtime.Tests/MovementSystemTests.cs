using System.Numerics;
using GoatShooooting.Core;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class MovementSystemTests
{
    [Fact]
    public void PositionChangesByVelocityTimesDeltaTime()
    {
        var world = new World();
        var entity = world.CreateEntity()
            .Add(new TransformComponent(new Vector2(2, 3)))
            .Add(new VelocityComponent(new Vector2(4, -2)));

        new MovementSystem().Update(world, 0.5f);

        Assert.Equal(new Vector2(4, 2), entity.Get<TransformComponent>().Position);
    }
}
