using System.Numerics;
using GoatShooooting.Core;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class BombSystemTests
{
    [Fact]
    public void PlayerFactoryUsesConfiguredLivesBombsAndDamage()
    {
        var definitions = TestDefinitions.Create(playerLives: 4, playerBombs: 3, bombDamage: 75);

        var player = new PlayerFactory().Create(new World(), definitions.GetPlayer("player"));

        Assert.Equal(4, player.Get<LivesComponent>().Remaining);
        Assert.Equal(3, player.Get<BombComponent>().Remaining);
        Assert.Equal(75, player.Get<BombComponent>().Damage);
        Assert.False(player.Has<HealthComponent>());
    }

    [Fact]
    public void BombDamagesEveryEnemyAndClearsEnemyBulletsOncePerPress()
    {
        var world = new World();
        var player = world.CreateEntity()
            .Add(new PlayerComponent("player", 100))
            .Add(new TransformComponent(new Vector2(50, 60)))
            .Add(new BombComponent(2, 30));
        var enemies = new[]
        {
            CreateEnemy(world, new Vector2(10, 10)),
            CreateEnemy(world, new Vector2(90, 90))
        };
        var enemyBullet = CreateBullet(world, CollisionLayer.EnemyBullet);
        var playerBullet = CreateBullet(world, CollisionLayer.PlayerBullet);
        var input = new MutableInputState { Bomb = true };
        var telemetry = new SimulationTelemetry();
        var system = new BombSystem();

        var damage = system.Update(world, input, 200, telemetry);
        new DamageSystem().Update(damage, telemetry);
        var heldDamage = system.Update(world, input, 200, telemetry);

        Assert.Equal(2, damage.Count);
        Assert.Empty(heldDamage);
        Assert.All(enemies, enemy => Assert.Equal(70, enemy.Get<HealthComponent>().Current));
        Assert.Equal(1, player.Get<BombComponent>().Remaining);
        Assert.True(enemyBullet.Has<PendingDestroyComponent>());
        Assert.False(playerBullet.Has<PendingDestroyComponent>());
        Assert.Equal(1, telemetry.BombsUsed);
        Assert.Equal(1, telemetry.EnemyBulletsCleared);
        Assert.Single(world.Query<ExplosionComponent>());
    }

    [Fact]
    public void BombWithNoStockDoesNothing()
    {
        var world = new World();
        world.CreateEntity()
            .Add(new PlayerComponent("player", 100))
            .Add(new TransformComponent(Vector2.Zero))
            .Add(new BombComponent(0, 30));
        var enemy = CreateEnemy(world, Vector2.Zero);
        var enemyBullet = CreateBullet(world, CollisionLayer.EnemyBullet);
        var telemetry = new SimulationTelemetry();

        var damage = new BombSystem().Update(
            world,
            new MutableInputState { Bomb = true },
            200,
            telemetry);

        Assert.Empty(damage);
        Assert.Equal(100, enemy.Get<HealthComponent>().Current);
        Assert.False(enemyBullet.Has<PendingDestroyComponent>());
        Assert.Equal(0, telemetry.BombsUsed);
    }

    private static Entity CreateEnemy(World world, Vector2 position) => world.CreateEntity()
        .Add(new EnemyComponent("enemy"))
        .Add(new TransformComponent(position))
        .Add(new HealthComponent(100));

    private static Entity CreateBullet(World world, CollisionLayer layer) => world.CreateEntity()
        .Add(new BulletComponent("bullet", layer == CollisionLayer.EnemyBullet
            ? CollisionLayer.Player
            : CollisionLayer.Enemy))
        .Add(new ColliderComponent(3, layer));
}
