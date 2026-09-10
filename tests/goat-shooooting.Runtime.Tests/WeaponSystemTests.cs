using GoatShooooting.Core;
using GoatShooooting.Definitions;
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

    [Fact]
    public void EnemyWithWeaponAutomaticallyFiresDownwardAtPlayerLayer()
    {
        var definitions = TestDefinitions.Create();
        var world = new World();
        var enemyDefinition = definitions.GetEnemy("enemy") with { WeaponId = "weapon" };
        _ = new EnemyFactory().Create(world, enemyDefinition, new System.Numerics.Vector2(50, 100));
        var telemetry = new SimulationTelemetry();

        new WeaponSystem(new BulletFactory()).Update(
            world,
            definitions,
            new MutableInputState(),
            0,
            telemetry);

        var bullet = Assert.Single(world.Query<BulletComponent>());
        Assert.Equal(CollisionLayer.Player, bullet.Get<BulletComponent>().TargetLayer);
        Assert.True(bullet.Get<VelocityComponent>().Value.Y > 0);
        Assert.Equal(1, telemetry.EnemyBulletsSpawned);
    }
}
