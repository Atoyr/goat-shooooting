using System.Numerics;
using GoatShooooting.Core;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class BombSystemTests
{
    [Fact]
    public void BombWithNoStockDoesNothing()
    {
        var definitions = TestDefinitions.Create(playerBombs: 0);
        var world = new World();
        _ = new PlayerFactory().Create(world, definitions.GetPlayer("player"));
        var enemy = CreateEnemy(world, Vector2.Zero);
        var projectiles = new ProjectileStore();
        projectiles.QueueSpawn(new ProjectileSpawnCommand(
            99,
            ProjectileTeam.Enemy,
            "enemy-shot",
            Vector2.Zero,
            Vector2.UnitY,
            3,
            1,
            5,
            "enemy-shot"));
        projectiles.CommitSpawns();
        var telemetry = new SimulationTelemetry();
        var events = new GameEventBuffer();
        events.BeginTick(0);

        var damage = new BombSystem().Update(
            world,
            projectiles,
            new MutableInputState { Bomb = true },
            200,
            telemetry,
            events);

        Assert.Empty(damage);
        Assert.Equal(100, enemy.Get<HealthComponent>().Current);
        Assert.False(projectiles.GetSnapshot(0).PendingRemoval);
        Assert.Equal(0, telemetry.BombsUsed);
        Assert.Empty(events.Events);
    }

    private static Entity CreateEnemy(World world, Vector2 position) => world.CreateEntity()
        .Add(new EnemyComponent("enemy"))
        .Add(new TransformComponent(position))
        .Add(new HealthComponent(100));
}
