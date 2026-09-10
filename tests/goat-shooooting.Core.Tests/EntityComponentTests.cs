using GoatShooooting.Core;
using Xunit;

namespace GoatShooooting.Core.Tests;

public sealed class EntityComponentTests
{
    [Fact]
    public void ComponentCanBeAddedAndRetrieved()
    {
        var entity = new World().CreateEntity();
        var component = new HealthComponent(10);

        entity.Add(component);

        Assert.Same(component, entity.Get<HealthComponent>());
        Assert.True(entity.Has<HealthComponent>());
    }

    [Fact]
    public void ComponentCanBeRemoved()
    {
        var entity = new World().CreateEntity().Add(new HealthComponent(10));

        var removed = entity.Remove<HealthComponent>();

        Assert.True(removed);
        Assert.False(entity.Has<HealthComponent>());
        Assert.Throws<InvalidOperationException>(() => entity.Get<HealthComponent>());
    }

    [Fact]
    public void AddingSameComponentTypeTwiceIsRejected()
    {
        var entity = new World().CreateEntity().Add(new HealthComponent(10));

        Assert.Throws<InvalidOperationException>(() => entity.Add(new HealthComponent(20)));
    }
}
