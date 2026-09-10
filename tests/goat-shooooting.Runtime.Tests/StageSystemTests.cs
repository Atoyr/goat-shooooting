using GoatShooooting.Core;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class StageSystemTests
{
    [Fact]
    public void EventRunsAtScheduledTimeExactlyOnce()
    {
        var definitions = TestDefinitions.Create(spawnTime: 5);
        var world = new World();
        var telemetry = new SimulationTelemetry();
        var system = new StageSystem(definitions.GetStage("stage"), new EnemyFactory());

        system.Update(world, definitions, 4.9f, telemetry);
        Assert.Empty(world.Query<EnemyComponent>());

        system.Update(world, definitions, 0.1f, telemetry);
        Assert.Single(world.Query<EnemyComponent>());

        system.Update(world, definitions, 10f, telemetry);
        Assert.Single(world.Query<EnemyComponent>());
        Assert.Equal(1, telemetry.EnemiesSpawned);
    }
}
