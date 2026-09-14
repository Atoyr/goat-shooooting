using System.Numerics;
using GoatShooooting.Core;
using GoatShooooting.Definitions;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class ProjectileSystemTests
{
    [Fact]
    public void SweptCollisionPreventsFastProjectileTunnelingAndChoosesNearestTarget()
    {
        var world = new World();
        var near = CreateEnemy(world, new Vector2(50, 100));
        CreateEnemy(world, new Vector2(50, 180));
        var projectiles = new ProjectileStore();
        projectiles.QueueSpawn(CreateCommand(
            ProjectileTeam.Player,
            new Vector2(50, 20),
            new Vector2(0, 12_000)));
        projectiles.CommitSpawns();
        var telemetry = new SimulationTelemetry();
        new ProjectileMovementSystem().Update(projectiles, world, 1f / 60f, 800, 600, telemetry);
        var grid = new ActorSpatialGrid();
        grid.Rebuild(world);
        var events = new GameEventBuffer();
        events.BeginTick(0);

        var damage = new ProjectileCollisionSystem().Detect(projectiles, grid, telemetry, events);

        var hit = Assert.Single(damage);
        Assert.Same(near, hit.Target);
        Assert.Equal(7, hit.Amount);
        Assert.True(projectiles.GetSnapshot(0).PendingRemoval);
        Assert.Single(events.Events.OfType<ProjectileHitEvent>());
        Assert.Equal(2, telemetry.CollisionCandidatesChecked);
    }

    [Fact]
    public void DefaultProjectileDamagesOnlyOneTarget()
    {
        var world = new World();
        CreateEnemy(world, new Vector2(50, 100));
        CreateEnemy(world, new Vector2(50, 105));
        var projectiles = new ProjectileStore();
        projectiles.QueueSpawn(CreateCommand(
            ProjectileTeam.Player,
            new Vector2(50, 100),
            Vector2.Zero));
        projectiles.CommitSpawns();
        var grid = new ActorSpatialGrid();
        grid.Rebuild(world);
        var telemetry = new SimulationTelemetry();
        var events = new GameEventBuffer();
        events.BeginTick(0);

        var damage = new ProjectileCollisionSystem().Detect(projectiles, grid, telemetry, events);
        projectiles.CommitRemovals();

        Assert.Single(damage);
        Assert.Equal(1, telemetry.CollisionsDetected);
        Assert.Equal(0, projectiles.ActiveCount);
    }

    [Fact]
    public void ProjectileWithoutDamageCapabilityDoesNotHitOrGrazeActors()
    {
        var world = new World();
        world.CreateEntity()
            .Add(new TransformComponent(new Vector2(50, 100)))
            .Add(new ColliderComponent(5, CollisionLayer.Player))
            .Add(new GrazeRadiusComponent(20))
            .Add(new PlayerComponent("player", 200));
        var projectiles = new ProjectileStore();
        projectiles.QueueSpawn(CreateCommand(
            ProjectileTeam.Enemy,
            new Vector2(50, 100),
            Vector2.Zero) with
        { CanDamage = false });
        projectiles.CommitSpawns();
        var grid = new ActorSpatialGrid();
        grid.Rebuild(world);
        var telemetry = new SimulationTelemetry();
        var events = new GameEventBuffer();
        events.BeginTick(0);

        var damage = new ProjectileCollisionSystem().Detect(projectiles, grid, telemetry, events);

        Assert.Empty(damage);
        Assert.Empty(events.Events);
        Assert.False(projectiles.GetSnapshot(0).PendingRemoval);
        Assert.Equal(0, telemetry.CollisionCandidatesChecked);
    }

    [Fact]
    public void EnemyProjectileGrazesTheSamePlayerOnlyOnce()
    {
        var world = new World();
        var player = world.CreateEntity()
            .Add(new TransformComponent(new Vector2(100, 100)))
            .Add(new ColliderComponent(5, CollisionLayer.Player))
            .Add(new GrazeRadiusComponent(20))
            .Add(new PlayerComponent("player", 200));
        var projectiles = new ProjectileStore();
        projectiles.QueueSpawn(CreateCommand(
            ProjectileTeam.Enemy,
            new Vector2(115, 100),
            Vector2.Zero));
        projectiles.CommitSpawns();
        var grid = new ActorSpatialGrid();
        grid.Rebuild(world);
        var telemetry = new SimulationTelemetry();
        var collision = new ProjectileCollisionSystem();
        var events = new GameEventBuffer();
        events.BeginTick(3);

        Assert.Empty(collision.Detect(projectiles, grid, telemetry, events));
        var graze = Assert.Single(events.Events.OfType<PlayerGrazedEvent>());
        Assert.Equal(player.Id, graze.PlayerEntityId);

        events.BeginTick(4);
        Assert.Empty(collision.Detect(projectiles, grid, telemetry, events));
        Assert.Empty(events.Events);
        Assert.Equal(1, telemetry.PlayerGrazes);
    }

    [Fact]
    public void BombCancelsOnlyCancelableEnemyProjectilesAndEmitsEvents()
    {
        var definitions = TestDefinitions.Create(spawnTime: 100);
        var world = new World();
        var player = new PlayerFactory().Create(world, definitions.GetPlayer("player"));
        var projectiles = new ProjectileStore();
        projectiles.QueueSpawn(CreateCommand(ProjectileTeam.Enemy, new Vector2(100, 100), Vector2.Zero));
        projectiles.QueueSpawn(CreateCommand(
            ProjectileTeam.Enemy,
            new Vector2(120, 100),
            Vector2.Zero) with
        {
            CanBeCancelled = false,
            CancelResistance = ProjectileCancelResistance.Uncancelable
        });
        projectiles.QueueSpawn(CreateCommand(ProjectileTeam.Player, new Vector2(140, 100), Vector2.Zero));
        projectiles.CommitSpawns();
        var telemetry = new SimulationTelemetry();
        var events = new GameEventBuffer();
        events.BeginTick(8);

        new BombSystem().Update(
            world,
            projectiles,
            new MutableInputState { Bomb = true },
            100,
            telemetry,
            events);
        projectiles.CommitRemovals();

        Assert.Equal(2, projectiles.ActiveCount);
        Assert.Equal(1, telemetry.EnemyBulletsCleared);
        Assert.Single(events.Events.OfType<BombUsedEvent>());
        Assert.Single(events.Events.OfType<ProjectileCancelledEvent>());
        Assert.Equal(1, player.Get<BombComponent>().Remaining);
    }

    [Fact]
    public void MovementTracksPreviousPositionThenLifetimeAndBoundsRemoveAtBoundary()
    {
        var world = new World();
        var projectiles = new ProjectileStore();
        projectiles.QueueSpawn(CreateCommand(
            ProjectileTeam.Player,
            new Vector2(10, 10),
            new Vector2(-100, 0)) with
        { Lifetime = 0.1f });
        projectiles.CommitSpawns();
        var telemetry = new SimulationTelemetry();

        new ProjectileMovementSystem().Update(projectiles, world, 0.2f, 100, 100, telemetry);
        new ProjectileLifetimeSystem().Update(projectiles);

        var projectile = projectiles.GetSnapshot(0);
        Assert.Equal(new Vector2(10, 10), projectile.PreviousPosition);
        Assert.Equal(new Vector2(-10, 10), projectile.Position);
        Assert.True(projectile.PendingRemoval);
        projectiles.CommitRemovals();
        Assert.Equal(0, projectiles.ActiveCount);
    }

    [Fact]
    public void HomingProjectileWithLockedTargetDoesNotRetargetToNearestEnemy()
    {
        var world = new World();
        _ = CreateEnemy(world, new Vector2(120, 100));
        var locked = CreateEnemy(world, new Vector2(100, 200));
        var projectiles = new ProjectileStore();
        projectiles.QueueSpawn(CreateCommand(
            ProjectileTeam.Player,
            new Vector2(100, 100),
            new Vector2(100, 0)) with
        {
            Behavior = ProjectileBehavior.Homing,
            HomingTurnRadiansPerSecond = MathF.PI / 2,
            TargetEntityId = locked.Id
        });
        projectiles.CommitSpawns();

        new ProjectileMovementSystem().Update(
            projectiles,
            world,
            0.5f,
            800,
            600,
            new SimulationTelemetry());

        var projectile = projectiles.GetSnapshot(0);
        Assert.Equal(locked.Id, projectile.TargetEntityId);
        Assert.InRange(projectile.Velocity.X, 70.70f, 70.72f);
        Assert.InRange(projectile.Velocity.Y, 70.70f, 70.72f);
    }

    [Fact]
    public void BulletFactoryPreservesV1HomingDefinitionInProjectileStore()
    {
        var projectiles = new ProjectileStore();
        var definition = new BulletDefinition
        {
            Id = "homing",
            Speed = 120,
            Damage = 3,
            Radius = 4,
            Lifetime = 2,
            MovementPattern = "homing",
            HomingTurnDegreesPerSecond = 90
        };

        var id = new BulletFactory().Create(
            projectiles,
            definition,
            new Vector2(20, 30),
            Vector2.UnitX,
            CollisionLayer.Enemy,
            ownerEntityId: 4);
        projectiles.CommitSpawns();

        var projectile = projectiles.GetSnapshot(0);
        Assert.Equal(id, projectile.Id);
        Assert.Equal(ProjectileTeam.Enemy, projectile.Team);
        Assert.Equal(ProjectileBehavior.Homing, projectile.Behavior);
        Assert.Equal(new Vector2(120, 0), projectile.Velocity);
        Assert.Equal(4, projectile.HitRadius);
        Assert.Equal(3, projectile.Damage);
    }

    [Fact]
    public void RenderCaptureIncludesActiveStoreProjectilesAndSkipsPendingRemoval()
    {
        var world = new World();
        var projectiles = new ProjectileStore();
        projectiles.QueueSpawn(CreateCommand(
            ProjectileTeam.Player,
            new Vector2(20, 30),
            Vector2.Zero));
        projectiles.QueueSpawn(CreateCommand(
            ProjectileTeam.Enemy,
            new Vector2(40, 50),
            Vector2.Zero));
        projectiles.CommitSpawns();
        projectiles.QueueRemoveAt(0);

        var item = Assert.Single(new RenderSystem().Capture(world, projectiles));

        Assert.Equal(RenderKind.EnemyBullet, item.Kind);
        Assert.Equal(new Vector2(40, 50), item.Position);
        Assert.Equal(2, item.Radius);
    }

    private static Entity CreateEnemy(World world, Vector2 position) => world.CreateEntity()
        .Add(new TransformComponent(position))
        .Add(new ColliderComponent(10, CollisionLayer.Enemy))
        .Add(new EnemyComponent("enemy"))
        .Add(new HealthComponent(10));

    private static ProjectileSpawnCommand CreateCommand(
        ProjectileTeam team,
        Vector2 position,
        Vector2 velocity) => new(
        OwnerEntityId: 1,
        Team: team,
        DefinitionId: "shot",
        Position: position,
        Velocity: velocity,
        HitRadius: 2,
        Damage: 7,
        Lifetime: 5,
        VisualId: "shot");
}
