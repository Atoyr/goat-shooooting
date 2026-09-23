using System.Numerics;
using GoatShooooting.Core;
using GoatShooooting.Definitions;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class InteractionSystemTests
{
    [Fact]
    public void ProjectileProfileUsesTagsPowerResistanceAndEmitsFacts()
    {
        var profile = Profile(
            "shot-cancel",
            new InteractionFilterDefinition { Team = "player", RequiredTags = new[] { "shot" }, MinimumPower = 2 },
            new InteractionFilterDefinition { Team = "enemy", RequiredTags = new[] { "bullet" }, MaximumResistance = 1 },
            "destroy-target", "emit-projectile-interaction", "emit-projectile-cancelled");
        var compiled = Compile(profile);
        var store = SpawnPair(compiled, sourcePower: 2, targetResistance: 1);
        var events = new GameEventBuffer();
        events.BeginTick(0);

        new InteractionSystem().Update(new World(), store, compiled, new SimulationTelemetry(), events);

        Assert.False(store.GetSnapshot(0).PendingRemoval);
        Assert.True(store.GetSnapshot(1).PendingRemoval);
        Assert.Single(events.Events.OfType<ProjectileInteractionEvent>());
        var cancelled = Assert.Single(events.Events.OfType<ProjectileCancelledEvent>());
        Assert.Equal(100, cancelled.X);
        Assert.Equal(100, cancelled.Y);
    }

    [Fact]
    public void ResistanceAboveProfileMaximumPreventsInteraction()
    {
        var compiled = Compile(Profile(
            "weak-shot",
            new InteractionFilterDefinition { Team = "player", RequiredTags = new[] { "shot" } },
            new InteractionFilterDefinition { Team = "enemy", RequiredTags = new[] { "bullet" }, MaximumResistance = 0 },
            "destroy-target"));
        var store = SpawnPair(compiled, sourcePower: 1, targetResistance: 2);

        new InteractionSystem().Update(
            new World(), store, compiled, new SimulationTelemetry(), new GameEventBuffer());

        Assert.False(store.GetSnapshot(1).PendingRemoval);
    }

    [Fact]
    public void ConvertCommandChangesTargetDefinitionWithoutDestroyingIt()
    {
        var profile = Profile(
            "convert",
            new InteractionFilterDefinition { Team = "player", RequiredTags = new[] { "shot" } },
            new InteractionFilterDefinition { Team = "enemy", RequiredTags = new[] { "bullet" } },
            "convert-target") with
        { ConvertProjectileId = "converted" };
        var compiled = Compile(profile);
        var store = SpawnPair(compiled, 1, 0);

        new InteractionSystem().Update(
            new World(), store, compiled, new SimulationTelemetry(), new GameEventBuffer());

        Assert.Equal("converted", store.GetSnapshot(1).DefinitionId);
        Assert.False(store.GetSnapshot(1).PendingRemoval);
    }

    [Fact]
    public void LaserPairsUseCapsuleBroadPhaseAndEmitContact()
    {
        var profile = Profile(
            "laser-contact",
            new InteractionFilterDefinition { Team = "player", RequiredTags = new[] { "laser" }, MinimumPower = 2 },
            new InteractionFilterDefinition { Team = "enemy", RequiredTags = new[] { "laser" }, MaximumResistance = 1 },
            "destroy-target", "emit-laser-contact");
        var compiled = Compile(profile);
        var world = new World();
        var laserMask = compiled.Tags.Mask(new[] { "laser" });
        world.CreateEntity().Add(new TransformComponent(Vector2.Zero)).Add(new LaserComponent(
            1, CollisionLayer.Player, Vector2.UnitX, 100, 3, 1, 1, "laser", "none", laserMask, 2, 0));
        var target = world.CreateEntity().Add(new TransformComponent(new Vector2(50, -50))).Add(new LaserComponent(
            2, CollisionLayer.Enemy, Vector2.UnitY, 100, 3, 1, 1, "laser", "none", laserMask, 1, 1));
        var events = new GameEventBuffer();
        events.BeginTick(0);

        new InteractionSystem().Update(world, new ProjectileStore(), compiled, new SimulationTelemetry(), events);

        Assert.True(target.Has<PendingDestroyComponent>());
        Assert.Single(events.Events.OfType<LaserContactEvent>());
    }

    [Fact]
    public void ReflectTargetReversesLaserAndTransfersItsCollisionLayer()
    {
        var profile = Profile(
            "laser-reflect",
            new InteractionFilterDefinition { Team = "player", RequiredTags = new[] { "laser" }, MinimumPower = 2 },
            new InteractionFilterDefinition { Team = "enemy", RequiredTags = new[] { "laser" }, MaximumResistance = 2 },
            "reflect-target", "emit-laser-contact");
        var compiled = Compile(profile);
        var world = new World();
        var laserMask = compiled.Tags.Mask(new[] { "laser" });
        world.CreateEntity().Add(new TransformComponent(Vector2.Zero)).Add(new LaserComponent(
            1, CollisionLayer.Player, Vector2.UnitX, 100, 3, 1, 1, "laser", "none", laserMask, 2, 0));
        var target = world.CreateEntity().Add(new TransformComponent(new Vector2(50, -50))).Add(new LaserComponent(
            2, CollisionLayer.Enemy, Vector2.UnitY, 100, 3, 1, 1, "laser", "none", laserMask, 1, 2));
        var events = new GameEventBuffer();
        events.BeginTick(0);

        new InteractionSystem().Update(world, new ProjectileStore(), compiled, new SimulationTelemetry(), events);

        var reflected = target.Get<LaserComponent>();
        Assert.Equal(CollisionLayer.Player, reflected.OwnerLayer);
        Assert.Equal(-Vector2.UnitY, reflected.Direction);
        Assert.False(target.Has<PendingDestroyComponent>());
        Assert.Single(events.Events.OfType<LaserContactEvent>());
    }

    [Fact]
    public void V2CancelSoftLaserCompilesToInteractionProfile()
    {
        var baseline = TestDefinitions.Create();
        var laserWeapon = new WeaponDefinition
        {
            SchemaVersion = 2,
            Id = "legacy-laser",
            ActionType = "laser",
            Laser = new LaserWeaponDefinition
            {
                Damage = 1,
                DamageInterval = 1,
                Length = 100,
                Width = 4,
                VisualId = "laser",
                ProjectileInteraction = "cancel-soft"
            }
        };
        var definitions = new DefinitionCatalog(
            baseline.Game, baseline.Players.Values, baseline.Enemies.Values, baseline.Bullets.Values,
            baseline.Weapons.Values.Append(laserWeapon), baseline.Stages.Values,
            visuals: new[] { new VisualDefinition { Id = "laser", AssetId = "laser" } });
        var compiled = new DefinitionCompiler().Compile(definitions, RuntimeCapabilityRegistry.CreateBuiltIn());
        var store = new ProjectileStore();
        store.ConfigurePrograms(compiled, null);
        var bulletMask = compiled.Tags.Mask(new[] { "projectile", "bullet" });
        store.QueueSpawn(new ProjectileSpawnCommand(
            1, ProjectileTeam.Enemy, "soft", new Vector2(50, 0), Vector2.UnitY, 2, 1, 5, "soft",
            TagMask: bulletMask, InteractionResistance: (int)ProjectileCancelResistance.Soft));
        store.QueueSpawn(new ProjectileSpawnCommand(
            1, ProjectileTeam.Enemy, "hard", new Vector2(55, 0), Vector2.UnitY, 2, 1, 5, "hard",
            TagMask: bulletMask, InteractionResistance: (int)ProjectileCancelResistance.Hard));
        store.CommitSpawns();
        var world = new World();
        world.CreateEntity().Add(new TransformComponent(Vector2.Zero)).Add(new LaserComponent(
            2, CollisionLayer.Player, Vector2.UnitX, 100, 4, 1, 1, "laser", "cancel-soft",
            compiled.Tags.Mask(new[] { "laser" })));
        var events = new GameEventBuffer();
        events.BeginTick(0);

        new InteractionSystem().Update(world, store, compiled, new SimulationTelemetry(), events);

        Assert.True(store.GetSnapshot(0).PendingRemoval);
        Assert.False(store.GetSnapshot(1).PendingRemoval);
        Assert.Contains(compiled.Interactions, profile => profile.CompatibilityAdapter);
    }

    private static ProjectileStore SpawnPair(CompiledCatalog compiled, int sourcePower, int targetResistance)
    {
        var store = new ProjectileStore();
        store.ConfigurePrograms(compiled, null);
        store.QueueSpawn(Command(
            compiled, "source", ProjectileTeam.Player, sourcePower, 0,
            compiled.Tags.Mask(new[] { "projectile", "shot" })));
        store.QueueSpawn(Command(
            compiled, "target", ProjectileTeam.Enemy, 1, targetResistance,
            compiled.Tags.Mask(new[] { "projectile", "bullet" })));
        store.CommitSpawns();
        return store;
    }

    private static ProjectileSpawnCommand Command(
        CompiledCatalog compiled,
        string id,
        ProjectileTeam team,
        int power,
        int resistance,
        ulong tags)
    {
        var projectile = compiled.Get(compiled.ResolveProjectile(id));
        return new ProjectileSpawnCommand(
            1, team, id, new Vector2(100, 100), Vector2.UnitY, 4, 1, 5, id,
            DefinitionHandle: projectile.Handle,
            TagMask: tags,
            InteractionPower: power,
            InteractionResistance: resistance);
    }

    private static CompiledCatalog Compile(params InteractionProfileDefinition[] profiles)
    {
        var baseline = TestDefinitions.Create();
        ProjectileDefinition Projectile(string id) => new()
        {
            Id = id,
            Speed = 100,
            Damage = 1,
            HitRadius = 4,
            Lifetime = 5,
            VisualId = id
        };
        var definitions = new DefinitionCatalog(
            baseline.Game,
            baseline.Players.Values,
            baseline.Enemies.Values,
            baseline.Bullets.Values,
            baseline.Weapons.Values,
            baseline.Stages.Values,
            projectiles: new[] { Projectile("source"), Projectile("target"), Projectile("converted") },
            visuals: new[]
            {
                new VisualDefinition { Id = "source", AssetId = "source" },
                new VisualDefinition { Id = "target", AssetId = "target" },
                new VisualDefinition { Id = "converted", AssetId = "converted" }
            },
            interactions: profiles);
        return new DefinitionCompiler().Compile(definitions, RuntimeCapabilityRegistry.CreateBuiltIn());
    }

    private static InteractionProfileDefinition Profile(
        string id,
        InteractionFilterDefinition source,
        InteractionFilterDefinition target,
        params string[] actions) => new()
        {
            Id = id,
            Source = source,
            Target = target,
            ShapeTest = "swept",
            Priority = 100,
            Actions = actions
        };
}
