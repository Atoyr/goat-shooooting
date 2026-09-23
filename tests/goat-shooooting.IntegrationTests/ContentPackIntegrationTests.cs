using System.Numerics;
using GoatShooooting.Core;
using GoatShooooting.Definitions;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.IntegrationTests;

public sealed class ContentPackIntegrationTests
{
    [Fact]
    public void SyncDriveLancerLocksAndFiresTargetedOptionShotsThroughProductionSimulation()
    {
        var repository = new JsonDefinitionRepository(
            Path.Combine(AppContext.BaseDirectory, "games", "sync-drive"));
        var simulation = new ShootingSimulation(
            repository,
            new MutableInputState(),
            new RunConfiguration("sync-drive", 123, shipId: "lancer"));
        var lockOn = simulation.Definitions.GetWeapon("lancer-lock").LockOn!;

        Assert.Equal("continuous", lockOn.FireMode);
        Assert.Equal("fire", lockOn.Trigger);
        Assert.Equal(4, lockOn.MaximumTargets);
        Assert.Equal(4, simulation.World.Query<OptionUnitComponent>().Count());
        _ = new EnemyFactory().Create(
            simulation.World,
            simulation.Definitions.GetEnemy("drone-1"),
            new Vector2(400, 300));

        ProjectileSnapshot[] lockedShots = [];
        for (var frame = 0; frame < 90 && lockedShots.Length == 0; frame++)
        {
            simulation.Tick(new InputFrame(0, 0, InputButtons.Fire));
            lockedShots = Enumerable.Range(0, simulation.Projectiles.ActiveCount)
                .Select(simulation.Projectiles.GetSnapshot)
                .Where(static projectile =>
                    projectile is { DefinitionId: "lancer-lock-shot", TargetEntityId: > 0 })
                .ToArray();
        }

        Assert.Equal(4, lockedShots.Length);
        Assert.Single(lockedShots.Select(static projectile => projectile.TargetEntityId).Distinct());
        Assert.Equal(4, lockedShots.Select(static projectile => projectile.Position).Distinct().Count());
        Assert.NotEmpty(simulation.Player.Get<WeaponRuntimeComponent>().States["lancer-lock"].LockedTargetEntityIds);
    }

    [Theory]
    [InlineData("sample")]
    [InlineData("gauntlet")]
    [InlineData("sync-drive")]
    [InlineData("ember-bloom")]
    public void ShippedContentPacksLoadAndAdvanceThroughTheSameRuntime(string gameId)
    {
        var simulation = Load(gameId);

        simulation.Tick(default);

        Assert.Equal(1, simulation.RunState.Frame);
        Assert.NotEmpty(simulation.Definitions.Stages);
        Assert.Contains(simulation.Player, simulation.World.Entities);
    }

    [Fact]
    public void EmberBloomActivatesDefinitionDrivenBreakAndUpgradesPlayerShots()
    {
        var repository = new JsonDefinitionRepository(
            Path.Combine(AppContext.BaseDirectory, "games", "ember-bloom"));
        var simulation = new ShootingSimulation(
            repository,
            new MutableInputState(),
            new RunConfiguration(
                "ember-bloom", 2026, "original", "arcade", "flare-wing",
                variantId: "radiant-bloom"));

        simulation.Tick(new InputFrame(0, 0, InputButtons.Special | InputButtons.Fire));

        var machine = Assert.Single(simulation.CompiledDefinitions.StateMachines,
            static value => value.Definition.Id == "break-drive");
        var breakState = Assert.Single(simulation.StateMachines.CaptureCanonicalSnapshot());
        Assert.Equal("break", machine.States.Single(value => value.Handle == breakState.State).Definition.Id);
        Assert.Contains(
            Enumerable.Range(0, simulation.Projectiles.ActiveCount).Select(simulation.Projectiles.GetSnapshot),
            static projectile => projectile.Team == ProjectileTeam.Player && projectile.InteractionPower == 2);
        Assert.True(simulation.RunState.Gauge < 200);

        simulation.Tick(new InputFrame(0, 0, InputButtons.Fire));
        simulation.Tick(new InputFrame(0, 0, InputButtons.Special | InputButtons.Fire));

        var doubleBreakState = Assert.Single(simulation.StateMachines.CaptureCanonicalSnapshot());
        Assert.Equal("double-break",
            machine.States.Single(value => value.Handle == doubleBreakState.State).Definition.Id);
        for (var frame = 0; frame < 6; frame++)
            simulation.Tick(new InputFrame(0, 0, InputButtons.Fire));
        Assert.Contains(
            Enumerable.Range(0, simulation.Projectiles.ActiveCount).Select(simulation.Projectiles.GetSnapshot),
            static projectile => projectile.Team == ProjectileTeam.Player && projectile.InteractionPower == 3);
        Assert.Equal(8, simulation.Definitions.GetWeapon("flare-lock").LockOn!.MaximumTargets);
        Assert.NotEmpty(simulation.CompiledDefinitions.Programs);
        Assert.NotEmpty(simulation.CompiledDefinitions.Interactions);
    }

    [Fact]
    public void EmberBloomBossSupportsEightStableLockSlots()
    {
        var repository = new JsonDefinitionRepository(
            Path.Combine(AppContext.BaseDirectory, "games", "ember-bloom"));
        var simulation = new ShootingSimulation(
            repository,
            new MutableInputState(),
            new RunConfiguration(
                "ember-bloom", 2027, "original", "arcade", "flare-wing",
                variantId: "radiant-bloom"));
        simulation.BootstrapSandboxTarget(
            SimulationSandboxTargetKind.BossPhase, "solar-bloom", "corolla");

        for (var frame = 0; frame < 30; frame++)
            simulation.Tick(new InputFrame(0, 0, InputButtons.Fire));

        var state = simulation.Player.Get<WeaponRuntimeComponent>().States["flare-lock"];
        Assert.Equal(8, state.LockedTargetEntityIds.Count);
        Assert.Equal([1, 7], state.LockedTargetEntityIds
            .GroupBy(static id => id)
            .Select(static group => group.Count())
            .Order()
            .ToArray());
        Assert.Contains(
            Enumerable.Range(0, simulation.Projectiles.ActiveCount).Select(simulation.Projectiles.GetSnapshot),
            static projectile => projectile.DefinitionId == "flare-lance" && projectile.TargetEntityId > 0);
    }

    private static ShootingSimulation Load(string gameId) => new(
        new JsonDefinitionRepository(Path.Combine(AppContext.BaseDirectory, "games", gameId)),
        new MutableInputState());
}
