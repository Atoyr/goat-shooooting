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
    public void ShippedContentPacksLoadAndAdvanceThroughTheSameRuntime(string gameId)
    {
        var simulation = Load(gameId);

        simulation.Tick(default);

        Assert.Equal(1, simulation.RunState.Frame);
        Assert.NotEmpty(simulation.Definitions.Stages);
        Assert.Contains(simulation.Player, simulation.World.Entities);
    }

    private static ShootingSimulation Load(string gameId) => new(
        new JsonDefinitionRepository(Path.Combine(AppContext.BaseDirectory, "games", gameId)),
        new MutableInputState());
}
