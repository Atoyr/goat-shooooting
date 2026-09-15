using System.Numerics;
using GoatShooooting.Core;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class BombSystemTests
{
    [Fact]
    public void ManualBombDropsExplosionInFrontOfPlayer()
    {
        var definitions = TestDefinitions.Create();
        var world = new World();
        var player = new PlayerFactory().Create(world, definitions.GetPlayer("player"));
        var telemetry = new SimulationTelemetry();
        var events = new GameEventBuffer();
        events.BeginTick(0);

        _ = new BombSystem().Update(
            world,
            new ProjectileStore(),
            new MutableInputState { Bomb = true },
            200,
            telemetry,
            events);

        var explosion = Assert.Single(world.Query<ExplosionComponent>());
        Assert.Equal(new Vector2(0, 50), explosion.Get<TransformComponent>().Position);
        var used = Assert.Single(events.Events.OfType<BombUsedEvent>());
        Assert.Equal(BombUsageKind.Manual, used.Kind);
        Assert.Equal(new Vector2(0, 50), used.EffectPosition);
        Assert.Equal(1, player.Get<BombComponent>().Remaining);
    }

    [Fact]
    public void AutoBombExplodesAtPlayerPosition()
    {
        var definitions = TestDefinitions.Create();
        var world = new World();
        var player = new PlayerFactory().Create(world, definitions.GetPlayer("player"));
        var telemetry = new SimulationTelemetry();
        var events = new GameEventBuffer();
        events.BeginTick(0);

        _ = new BombSystem().UseAutoBomb(
            world,
            new ProjectileStore(),
            player,
            200,
            1,
            0,
            telemetry,
            events);

        var explosion = Assert.Single(world.Query<ExplosionComponent>());
        Assert.Equal(player.Get<TransformComponent>().Position, explosion.Get<TransformComponent>().Position);
        var used = Assert.Single(events.Events.OfType<BombUsedEvent>());
        Assert.Equal(BombUsageKind.Auto, used.Kind);
        Assert.Equal(player.Get<TransformComponent>().Position, used.EffectPosition);
    }

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
