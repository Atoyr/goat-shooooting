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

    [Fact]
    public void SpreadWeaponCreatesConfiguredProjectileFan()
    {
        var baseline = TestDefinitions.Create();
        var spreadWeapon = baseline.GetWeapon("weapon") with
        {
            ProjectileCount = 3,
            SpreadDegrees = 60
        };
        var definitions = new DefinitionCatalog(
            baseline.Game,
            baseline.Players.Values,
            baseline.Enemies.Values,
            baseline.Bullets.Values,
            new[] { spreadWeapon },
            baseline.Stages.Values);
        var world = new World();
        _ = new PlayerFactory().Create(world, definitions.GetPlayer("player"));

        new WeaponSystem(new BulletFactory()).Update(
            world,
            definitions,
            new MutableInputState { Fire = true },
            0,
            new SimulationTelemetry());

        var velocities = world.Query<BulletComponent>()
            .Select(bullet => bullet.Get<VelocityComponent>().Value)
            .OrderBy(static velocity => velocity.X)
            .ToArray();
        Assert.Equal(3, velocities.Length);
        Assert.True(velocities[0].X < 0);
        Assert.Equal(0, velocities[1].X, precision: 3);
        Assert.True(velocities[2].X > 0);
        Assert.All(velocities, static velocity => Assert.True(velocity.Y < 0));
    }

    [Fact]
    public void WashingMachineRotatesAndReversesAfterConfiguredShots()
    {
        var baseline = TestDefinitions.Create(cooldown: 0);
        var weapon = baseline.GetWeapon("weapon") with
        {
            FirePattern = "washing-machine",
            RotationDegreesPerShot = 30,
            RotationSwitchShots = 2
        };
        var definitions = ReplaceWeapon(baseline, weapon);
        var world = new World();
        var player = new PlayerFactory().Create(world, definitions.GetPlayer("player"));
        var system = new WeaponSystem(new BulletFactory());

        system.Update(world, definitions, new MutableInputState { Fire = true }, 0, new SimulationTelemetry());
        Assert.Equal(30, player.Get<WeaponHolderComponent>().PatternAngleDegrees);

        system.Update(world, definitions, new MutableInputState { Fire = true }, 0, new SimulationTelemetry());
        Assert.Equal(60, player.Get<WeaponHolderComponent>().PatternAngleDegrees);
        Assert.Equal(-1, player.Get<WeaponHolderComponent>().PatternDirection);

        system.Update(world, definitions, new MutableInputState { Fire = true }, 0, new SimulationTelemetry());
        Assert.Equal(30, player.Get<WeaponHolderComponent>().PatternAngleDegrees);
    }

    [Fact]
    public void DoubleWashingMachineCreatesTwoCounterRotatingLayers()
    {
        var baseline = TestDefinitions.Create();
        var weapon = baseline.GetWeapon("weapon") with
        {
            FirePattern = "double-washing-machine",
            ProjectileCount = 3,
            RotationDegreesPerShot = 10,
            RotationSwitchShots = 8
        };
        var definitions = ReplaceWeapon(baseline, weapon);
        var world = new World();
        _ = new PlayerFactory().Create(world, definitions.GetPlayer("player"));

        new WeaponSystem(new BulletFactory()).Update(
            world,
            definitions,
            new MutableInputState { Fire = true },
            0,
            new SimulationTelemetry());

        Assert.Equal(6, world.Query<BulletComponent>().Count());
    }

    private static DefinitionCatalog ReplaceWeapon(
        DefinitionCatalog baseline,
        WeaponDefinition weapon) => new(
            baseline.Game,
            baseline.Players.Values,
            baseline.Enemies.Values,
            baseline.Bullets.Values,
            new[] { weapon },
            baseline.Stages.Values);
}
