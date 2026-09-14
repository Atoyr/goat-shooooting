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

    [Fact]
    public void ShippedContentPacksCreateDistinctGamesThroughTheSameRuntime()
    {
        var sample = Load("sample");
        var gauntlet = Load("gauntlet");

        Assert.Equal(800, sample.Definitions.Game.Width);
        Assert.Equal(640, gauntlet.Definitions.Game.Width);
        Assert.Equal("stage-01", sample.Definitions.Game.StageId);
        Assert.Equal("gauntlet-01", gauntlet.Definitions.Game.StageId);
        Assert.Equal("player-basic", sample.Player.Get<WeaponHolderComponent>().WeaponId);
        Assert.Equal("rapid-fire", gauntlet.Player.Get<WeaponHolderComponent>().WeaponId);
        Assert.Equal("touhou", sample.Definitions.Game.ScreenLayout);
        Assert.Equal("right-panel", sample.Definitions.Game.ScorePosition);
        Assert.Equal("donpachi", gauntlet.Definitions.Game.ScreenLayout);
        Assert.Equal("left-panel", gauntlet.Definitions.Game.ScorePosition);
        Assert.Equal("homing", sample.Definitions.GetBullet("enemy-homing-shot").MovementPattern);
        Assert.Equal("zigzag", sample.Definitions.GetEnemy("fighter-a").MovementPattern);
        Assert.Equal("double-washing-machine", sample.Definitions.GetWeapon("enemy-double-washer").FirePattern);
        Assert.Equal("washing-machine", gauntlet.Definitions.GetWeapon("gauntlet-washer").FirePattern);
        Assert.Equal(2, sample.Definitions.Game.SchemaVersion);
        Assert.Equal(2, gauntlet.Definitions.Game.SchemaVersion);
        Assert.Equal(sample.Definitions.Players.Count, sample.Definitions.Ships.Count);
        Assert.Equal(gauntlet.Definitions.Bullets.Count, gauntlet.Definitions.Projectiles.Count);
        Assert.Equal("homing", sample.Definitions.GetProjectile("enemy-homing-shot").Behavior.Type);
        Assert.Equal("THE SILENT HORIZON", sample.CurrentStage.Title);
        Assert.Equal("stage-02", sample.CurrentStage.NextStageId);
        Assert.True(sample.CurrentStage.Events.Single(static stageEvent => stageEvent.IsBoss).IsBoss);

        sample.Update(3);
        Assert.Equal(StagePhase.Playing, sample.Phase);
        sample.Update(3);
        gauntlet.Update(3);

        Assert.Equal(2, sample.World.Query<EnemyComponent>().Count());
        Assert.Equal(2, gauntlet.World.Query<EnemyComponent>().Count());
        Assert.All(sample.World.Query<EnemyComponent>(),
            enemy => Assert.Equal("scout", enemy.Get<EnemyComponent>().DefinitionId));
        Assert.All(gauntlet.World.Query<EnemyComponent>(),
            enemy => Assert.Equal("dart", enemy.Get<EnemyComponent>().DefinitionId));
    }

    private static ShootingSimulation Load(string gameId) => new(
        new JsonDefinitionRepository(Path.Combine(AppContext.BaseDirectory, "games", gameId)),
        new MutableInputState());
}
