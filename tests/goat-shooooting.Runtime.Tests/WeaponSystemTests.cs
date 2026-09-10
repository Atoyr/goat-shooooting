using GoatShooooting.Core;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class WeaponSystemTests
{
    [Fact]
    public void FireCreatesBulletButCooldownPreventsImmediateSecondShot()
    {
        var definitions = TestDefinitions.Create(cooldown: 0.5f);
        var world = new World();
        _ = new PlayerFactory().Create(world, definitions.GetPlayer("player"));
        var input = new MutableInputState { Fire = true };
        var telemetry = new SimulationTelemetry();
        var system = new WeaponSystem(new BulletFactory());

        system.Update(world, definitions, input, 0, telemetry);
        Assert.Single(world.Query<BulletComponent>());

        system.Update(world, definitions, input, 0.49f, telemetry);
        Assert.Single(world.Query<BulletComponent>());

        system.Update(world, definitions, input, 0.01f, telemetry);
        Assert.Equal(2, world.Query<BulletComponent>().Count());
    }
}
