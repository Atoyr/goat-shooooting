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
        Assert.False(system.IsComplete);

        system.Update(world, definitions, 0.1f, telemetry);
        Assert.Single(world.Query<EnemyComponent>());
        Assert.True(system.IsComplete);

        system.Update(world, definitions, 10f, telemetry);
        Assert.Single(world.Query<EnemyComponent>());
        Assert.Equal(1, telemetry.EnemiesSpawned);
    }

    [Fact]
    public void RepeatedEventSpawnsAHorizontalWaveAtConfiguredIntervals()
    {
        var definitions = TestDefinitions.Create();
        var stage = new GoatShooooting.Definitions.StageDefinition
        {
            Id = "wave",
            Events = new[]
            {
                new GoatShooooting.Definitions.StageEventDefinition
                {
                    Time = 1, Type = "spawn-enemy", EnemyId = "enemy",
                    X = 100, Y = 50, Count = 3, SpawnInterval = 0.5f, SpacingX = 20
                }
            }
        };
        var world = new World();
        var telemetry = new SimulationTelemetry();
        var system = new StageSystem(stage, new EnemyFactory());

        system.Update(world, definitions, 1, telemetry);
        Assert.Single(world.Query<EnemyComponent>());
        Assert.False(system.IsComplete);

        system.Update(world, definitions, 1, telemetry);

        Assert.True(system.IsComplete);
        Assert.Equal(3, telemetry.EnemiesSpawned);
        Assert.Equal(
            new[] { 100f, 120f, 140f },
            world.Query<TransformComponent>().Select(entity => entity.Get<TransformComponent>().Position.X));
    }
}
