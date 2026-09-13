using System.Numerics;
using GoatShooooting.Core;
using GoatShooooting.Definitions;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class AdvancedWeaponSystemTests
{
    [Fact]
    public void V2ShipSelectionAppliesDistinctNormalAndFocusMovementSpeeds()
    {
        var definitions = CreateDefinitions();
        var striker = CreateSimulation(definitions, "striker");
        var seeker = CreateSimulation(definitions, "seeker");
        var strikerStart = striker.Player.Get<TransformComponent>().Position;
        var seekerStart = seeker.Player.Get<TransformComponent>().Position;

        striker.Tick(new InputFrame(InputFrame.AxisMaximum, 0));
        seeker.Tick(new InputFrame(InputFrame.AxisMaximum, 0, InputButtons.Focus));

        Assert.Equal("striker", striker.CurrentShip.Id);
        Assert.Equal("seeker", seeker.CurrentShip.Id);
        Assert.InRange(
            striker.Player.Get<TransformComponent>().Position.X - strikerStart.X,
            3.999f,
            4.001f);
        Assert.InRange(
            seeker.Player.Get<TransformComponent>().Position.X - seekerStart.X,
            1.332f,
            1.335f);
        Assert.True(seeker.Player.Get<ShipComponent>().IsFocused);
    }

    [Fact]
    public void MultipleEmittersApplyOffsetsFanRingSpeedLayersPowerAndCooldown()
    {
        var definitions = CreateDefinitions();
        var world = new World();
        var player = new PlayerFactory().Create(world, definitions.GetShip("striker"), new Vector2(100, 200));
        player.Get<ShipComponent>().Power = 10;
        var projectiles = new ProjectileStore();
        var system = CreateSystem();
        var telemetry = new SimulationTelemetry();

        system.Update(world, definitions, new MutableInputState { Fire = true }, 1f / 60, telemetry, projectiles);
        projectiles.CommitSpawns();

        Assert.Equal(15, projectiles.ActiveCount);
        Assert.Equal(new Vector2(90, 198), projectiles.GetSnapshot(0).Position);
        Assert.Contains(Enumerable.Range(0, projectiles.ActiveCount).Select(projectiles.GetSnapshot),
            projectile => Math.Abs(projectile.Velocity.Length() - 300) < 0.01f);
        Assert.Contains(Enumerable.Range(0, projectiles.ActiveCount).Select(projectiles.GetSnapshot),
            projectile => projectile.Position.X == 110 && projectile.Velocity.X > 180);

        system.Update(world, definitions, new MutableInputState { Fire = true }, 1f / 60, telemetry, projectiles);
        projectiles.CommitSpawns();

        Assert.Equal(15, projectiles.ActiveCount);
    }

    [Fact]
    public void V2SimulationRoutesNormalAndFocusLoadoutsThroughProductionTick()
    {
        var definitions = CreateDefinitions();
        var normal = CreateSimulation(definitions, "striker");
        var focused = CreateSimulation(definitions, "striker");

        normal.Tick(new InputFrame(0, 0, InputButtons.Fire));
        focused.Tick(new InputFrame(0, 0, InputButtons.Fire | InputButtons.Focus));

        Assert.Equal(11, normal.Projectiles.ActiveCount);
        Assert.Empty(normal.World.Query<LaserComponent>());
        Assert.Single(Enumerable.Range(0, focused.Projectiles.ActiveCount));
        Assert.Single(focused.World.Query<LaserComponent>());
    }

    [Fact]
    public void BurstEmitterFiresScheduledShotsThenHonorsFireInterval()
    {
        var definitions = CreateDefinitions();
        var world = new World();
        _ = new PlayerFactory().Create(world, definitions.GetShip("seeker"), new Vector2(100, 200));
        var projectiles = new ProjectileStore();
        var system = CreateSystem();
        var telemetry = new SimulationTelemetry();
        var fire = new MutableInputState { Fire = true };

        system.Update(world, definitions, fire, 0, telemetry, projectiles);
        projectiles.CommitSpawns();
        Assert.Single(Enumerable.Range(0, projectiles.ActiveCount));

        system.Update(world, definitions, fire, 0.02f, telemetry, projectiles);
        projectiles.CommitSpawns();
        Assert.Equal(2, projectiles.ActiveCount);

        system.Update(world, definitions, fire, 0.1f, telemetry, projectiles);
        projectiles.CommitSpawns();
        Assert.Equal(2, projectiles.ActiveCount);

        system.Update(world, definitions, fire, 0.1f, telemetry, projectiles);
        projectiles.CommitSpawns();
        Assert.Equal(3, projectiles.ActiveCount);
    }

    [Fact]
    public void LaserDamagesAtIntervalsAndCancelsOnlySoftOpposingProjectiles()
    {
        var definitions = CreateDefinitions();
        var world = new World();
        var player = new PlayerFactory().Create(world, definitions.GetShip("striker"), new Vector2(100, 200));
        player.Get<ShipComponent>().IsFocused = true;
        var enemy = CreateEnemy(world, new Vector2(100, 100), hp: 30);
        var projectiles = new ProjectileStore();
        projectiles.QueueSpawn(CreateEnemyProjectile(new Vector2(100, 140), ProjectileCancelResistance.Soft));
        projectiles.QueueSpawn(CreateEnemyProjectile(new Vector2(102, 140), ProjectileCancelResistance.Hard));
        projectiles.CommitSpawns();
        var weaponSystem = CreateSystem();
        var laserSystem = new LaserSystem();
        var damageSystem = new DamageSystem();
        var telemetry = new SimulationTelemetry();
        var events = new GameEventBuffer();
        events.BeginTick(0);

        weaponSystem.Update(world, definitions, new MutableInputState { Fire = true }, 0, telemetry, projectiles);
        var damage = laserSystem.Update(world, projectiles, 0, telemetry, events);
        damageSystem.Update(damage, telemetry, events);
        projectiles.CommitRemovals();

        Assert.Equal(23, enemy.Get<HealthComponent>().Current);
        Assert.Single(world.Query<LaserComponent>());
        Assert.Single(Enumerable.Range(0, projectiles.ActiveCount));
        Assert.Equal(ProjectileCancelResistance.Hard, projectiles.GetSnapshot(0).CancelResistance);
        Assert.Single(events.Events.OfType<ProjectileCancelledEvent>());

        damageSystem.Update(laserSystem.Update(world, projectiles, 0.05f, telemetry, events), telemetry, events);
        Assert.Equal(23, enemy.Get<HealthComponent>().Current);
        damageSystem.Update(laserSystem.Update(world, projectiles, 0.051f, telemetry, events), telemetry, events);
        Assert.Equal(16, enemy.Get<HealthComponent>().Current);

        weaponSystem.Update(world, definitions, new MutableInputState(), 0, telemetry, projectiles);
        Assert.Empty(world.Query<LaserComponent>());
    }

    [Fact]
    public void LockOnAcquiresNearestTargetsAndFiresOnceOnRelease()
    {
        var definitions = CreateDefinitions();
        var world = new World();
        var player = new PlayerFactory().Create(world, definitions.GetShip("seeker"), new Vector2(100, 200));
        var far = CreateEnemy(world, new Vector2(160, 100));
        var nearest = CreateEnemy(world, new Vector2(100, 170));
        var next = CreateEnemy(world, new Vector2(120, 160));
        var projectiles = new ProjectileStore();
        var system = CreateSystem();
        var telemetry = new SimulationTelemetry();

        system.Update(world, definitions, new MutableInputState { Special = true }, 0, telemetry, projectiles);

        var state = player.Get<WeaponRuntimeComponent>().States["lock"];
        Assert.Equal(new[] { nearest.Id, next.Id }, state.LockedTargetEntityIds);
        Assert.DoesNotContain(far.Id, state.LockedTargetEntityIds);
        Assert.Equal(2, new RenderSystem().Capture(world, projectiles)
            .Count(item => item.Kind == RenderKind.LockMarker));

        system.Update(world, definitions, new MutableInputState(), 0, telemetry, projectiles);
        projectiles.CommitSpawns();

        Assert.Equal(2, projectiles.ActiveCount);
        Assert.All(Enumerable.Range(0, projectiles.ActiveCount).Select(projectiles.GetSnapshot),
            projectile => Assert.True(projectile.Velocity.Y < 0));
        system.Update(world, definitions, new MutableInputState(), 1, telemetry, projectiles);
        projectiles.CommitSpawns();
        Assert.Equal(2, projectiles.ActiveCount);
    }

    [Fact]
    public void OptionFollowsOwnerUsesOwnerPowerAndRenderSnapshotExposesShipOptionLaserAndHitbox()
    {
        var definitions = CreateDefinitions();
        var world = new World();
        var player = new PlayerFactory().Create(world, definitions.GetShip("striker"), new Vector2(100, 200));
        var optionEntity = Assert.Single(world.Query<OptionUnitComponent>());
        player.Get<ShipComponent>().Power = 10;
        player.Get<ShipComponent>().IsFocused = true;
        player.Get<TransformComponent>().Position = new Vector2(200, 200);

        new OptionFollowSystem().Update(world, 1);
        Assert.Equal(new Vector2(190, 210), optionEntity.Get<TransformComponent>().Position);

        var projectiles = new ProjectileStore();
        var weaponSystem = CreateSystem();
        weaponSystem.Update(world, definitions, new MutableInputState { Fire = true }, 0,
            new SimulationTelemetry(), projectiles);
        projectiles.CommitSpawns();
        Assert.Equal(2, projectiles.ActiveCount);

        var render = new RenderSystem().Capture(world, projectiles);
        var ship = Assert.Single(render.Where(item => item.Kind == RenderKind.Player));
        Assert.Equal("ship-striker", ship.VisualId);
        Assert.Single(render.Where(item => item.Kind == RenderKind.Option));
        Assert.Single(render.Where(item => item.Kind == RenderKind.Laser));
        Assert.Single(render.Where(item => item.Kind == RenderKind.PlayerHitbox));

        player.Get<ShipComponent>().IsFocused = false;
        weaponSystem.Update(world, definitions, new MutableInputState { Fire = true }, 0,
            new SimulationTelemetry(), projectiles);
        Assert.Empty(world.Query<LaserComponent>());
    }

    [Fact]
    public void DefinitionValidationRejectsInvalidEmitterAndLaserBoundaries()
    {
        var definitions = CreateDefinitions();
        var weapons = definitions.Weapons.Values
            .Where(weapon => weapon.Id != "multi")
            .Append(definitions.GetWeapon("multi") with
            {
                Emitters = new[]
                {
                    definitions.GetWeapon("multi").Emitters[0] with { SpeedMultipliers = Array.Empty<float>() }
                }
            });

        var exception = Assert.Throws<DefinitionValidationException>(() => CopyCatalog(definitions, weapons));

        Assert.Contains("speed layers", exception.Message, StringComparison.Ordinal);
    }

    private static AdvancedWeaponSystem CreateSystem() => new(
        new BulletFactory(),
        RuntimeCapabilityRegistry.CreateBuiltIn());

    private static ShootingSimulation CreateSimulation(DefinitionCatalog definitions, string shipId) => new(
        new MemoryDefinitionRepository(definitions),
        new MutableInputState(),
        new RunConfiguration("test", 123, shipId: shipId));

    private static Entity CreateEnemy(World world, Vector2 position, int hp = 10) => world.CreateEntity()
        .Add(new TransformComponent(position))
        .Add(new HealthComponent(hp))
        .Add(new ColliderComponent(5, CollisionLayer.Enemy))
        .Add(new EnemyComponent("target"))
        .Add(new ScoreValueComponent(100));

    private static ProjectileSpawnCommand CreateEnemyProjectile(
        Vector2 position,
        ProjectileCancelResistance resistance) => new(
            999,
            ProjectileTeam.Enemy,
            "enemy-shot",
            position,
            Vector2.Zero,
            2,
            1,
            5,
            "projectile",
            CancelResistance: resistance);

    private static DefinitionCatalog CopyCatalog(
        DefinitionCatalog source,
        IEnumerable<WeaponDefinition> weapons) => new(
            source.Game,
            source.Players.Values,
            source.Enemies.Values,
            source.Bullets.Values,
            weapons,
            source.Stages.Values,
            source.Ships.Values.Where(static ship => ship.Id is "striker" or "seeker"),
            source.Projectiles.Values.Where(static projectile => projectile.Id is "shot" or "missile"),
            visuals: source.Visuals.Values);

    private static DefinitionCatalog CreateDefinitions()
    {
        var straight = new CapabilityDefinition { Type = "straight" };
        var visuals = new[]
        {
            new VisualDefinition { Id = "projectile", AssetId = "projectile" },
            new VisualDefinition { Id = "ship-striker", AssetId = "ship-striker" },
            new VisualDefinition { Id = "ship-seeker", AssetId = "ship-seeker" },
            new VisualDefinition { Id = "option", AssetId = "option" }
        };
        var projectiles = new[]
        {
            new ProjectileDefinition
            {
                Id = "shot", Speed = 200, Damage = 3, HitRadius = 2, Lifetime = 5,
                VisualId = "projectile", Behavior = straight
            },
            new ProjectileDefinition
            {
                Id = "missile", Speed = 250, Damage = 5, HitRadius = 2, Lifetime = 5,
                VisualId = "projectile", Behavior = straight
            }
        };
        var weapons = new[]
        {
            new WeaponDefinition { Id = "legacy-weapon", BulletId = "legacy-bullet", Cooldown = 0.2f },
            new WeaponDefinition
            {
                SchemaVersion = 2,
                Id = "multi",
                ActionType = "projectile",
                Emitters = new[]
                {
                    new EmitterDefinition
                    {
                        Id = "fan", ProjectileId = "shot", OffsetX = -10, OffsetY = -2,
                        FireInterval = 0.2f, Distribution = "fan", ProjectileCount = 3,
                        SpreadDegrees = 30, SpeedMultipliers = new[] { 1f, 1.5f }
                    },
                    new EmitterDefinition
                    {
                        Id = "ring", ProjectileId = "shot", OffsetX = 10, OffsetY = -2,
                        FireInterval = 0.2f, Distribution = "ring", ProjectileCount = 4
                    }
                }
            },
            new WeaponDefinition
            {
                SchemaVersion = 2,
                Id = "burst",
                ActionType = "projectile",
                Emitters = new[]
                {
                    new EmitterDefinition
                    {
                        Id = "burst", ProjectileId = "shot", FireInterval = 0.2f,
                        BurstCount = 2, BurstInterval = 0.02f, Distribution = "fan"
                    }
                }
            },
            new WeaponDefinition
            {
                SchemaVersion = 2,
                Id = "laser",
                ActionType = "laser",
                Laser = new LaserWeaponDefinition
                {
                    Damage = 7, DamageInterval = 0.1f, Length = 150, Width = 4,
                    VisualId = "laser", ProjectileInteraction = "cancel-soft"
                }
            },
            new WeaponDefinition
            {
                SchemaVersion = 2,
                Id = "lock",
                ActionType = "lock-on",
                LockOn = new LockOnWeaponDefinition { MaximumTargets = 2, Range = 200 },
                Emitters = new[]
                {
                    new EmitterDefinition { Id = "missile", ProjectileId = "missile", FireInterval = 0.25f }
                }
            }
        };
        var ships = new[]
        {
            new ShipDefinition
            {
                Id = "striker", HitRadius = 3, GrazeRadius = 20, NormalSpeed = 240, FocusSpeed = 100,
                NormalWeaponIds = new[] { "multi" }, FocusWeaponIds = new[] { "laser" },
                SpecialWeaponId = "lock", VisualId = "ship-striker",
                Options = new[]
                {
                    new OptionUnitDefinition
                    {
                        Id = "left", OffsetX = -10, OffsetY = 10, FollowSpeed = 500,
                        NormalWeaponIds = new[] { "burst" }, FocusWeaponIds = new[] { "burst" },
                        VisualId = "option"
                    }
                },
                PowerLevels = new[]
                {
                    new PowerLevelModifierDefinition(),
                    new PowerLevelModifierDefinition { MinimumPower = 10, AdditionalProjectileCount = 1, DamageMultiplier = 2 }
                }
            },
            new ShipDefinition
            {
                Id = "seeker", HitRadius = 4, GrazeRadius = 24, NormalSpeed = 180, FocusSpeed = 80,
                NormalWeaponIds = new[] { "burst" }, FocusWeaponIds = new[] { "burst" },
                SpecialWeaponId = "lock", VisualId = "ship-seeker"
            }
        };
        return new DefinitionCatalog(
            new GameDefinition { Id = "test", PlayerId = "legacy", StageId = "stage", Width = 800, Height = 600 },
            new[]
            {
                new PlayerDefinition
                {
                    Id = "legacy", Lives = 2, Bombs = 2, Speed = 200, WeaponId = "legacy-weapon",
                    X = 400, Y = 550, Radius = 5
                }
            },
            new[] { new EnemyDefinition { Id = "target", Hp = 30, Speed = 0, Radius = 5 } },
            new[] { new BulletDefinition { Id = "legacy-bullet", Speed = 100, Damage = 1, Radius = 2, Lifetime = 5 } },
            weapons,
            new[]
            {
                new StageDefinition
                {
                    Id = "stage",
                    Events = new[]
                    {
                        new StageEventDefinition
                        {
                            Time = 100, Type = "spawn-enemy", EnemyId = "target", X = 400, Y = 100
                        }
                    }
                }
            },
            ships,
            projectiles,
            visuals: visuals);
    }
}
