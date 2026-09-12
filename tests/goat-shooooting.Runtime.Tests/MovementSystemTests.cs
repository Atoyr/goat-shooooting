using System.Numerics;
using GoatShooooting.Core;
using GoatShooooting.Definitions;
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

    [Fact]
    public void SinePatternMovesAroundItsDefinitionDrivenOrigin()
    {
        var world = new World();
        var entity = world.CreateEntity()
            .Add(new TransformComponent(new Vector2(100, 50)))
            .Add(new SineMovementComponent(originX: 100, amplitude: 40, frequency: 1));

        new MovementPatternSystem().Update(world, 0.25f);

        Assert.Equal(140, entity.Get<TransformComponent>().Position.X, precision: 3);
        Assert.Equal(50, entity.Get<TransformComponent>().Position.Y);
    }

    [Fact]
    public void HomingBulletTurnsTowardNearestTargetWithoutChangingSpeed()
    {
        var world = new World();
        var bullet = world.CreateEntity()
            .Add(new TransformComponent(Vector2.Zero))
            .Add(new VelocityComponent(new Vector2(0, 100)))
            .Add(new ColliderComponent(3, CollisionLayer.EnemyBullet))
            .Add(new BulletComponent("homing", CollisionLayer.Player))
            .Add(new HomingMovementComponent(MathF.PI / 2));
        _ = world.CreateEntity()
            .Add(new TransformComponent(new Vector2(100, 0)))
            .Add(new ColliderComponent(10, CollisionLayer.Player));

        new HomingMovementSystem().Update(world, 0.5f);

        var velocity = bullet.Get<VelocityComponent>().Value;
        Assert.Equal(100, velocity.Length(), precision: 3);
        Assert.Equal(70.711f, velocity.X, precision: 3);
        Assert.Equal(70.711f, velocity.Y, precision: 3);
    }

    [Fact]
    public void BulletFactoryAddsHomingBehaviorFromDefinition()
    {
        var world = new World();

        var bullet = new BulletFactory().Create(
            world,
            new BulletDefinition
            {
                Id = "homing",
                Speed = 100,
                Damage = 1,
                Radius = 3,
                Lifetime = 5,
                MovementPattern = "homing",
                HomingTurnDegreesPerSecond = 90
            },
            Vector2.Zero,
            Vector2.UnitY,
            CollisionLayer.Enemy);

        Assert.Equal(MathF.PI / 2, bullet.Get<HomingMovementComponent>().TurnRadiansPerSecond, precision: 3);
    }
}
