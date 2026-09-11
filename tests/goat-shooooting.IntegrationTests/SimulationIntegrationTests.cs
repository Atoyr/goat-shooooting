using GoatShooooting.Core;
using GoatShooooting.Definitions;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.IntegrationTests;

public sealed class SimulationIntegrationTests
{
    [Fact]
    public void DefinitionThroughStageAndWeaponKillsEnemyEndToEnd()
    {
        var input = new MutableInputState { Fire = true };
        var simulation = new ShootingSimulation(
            new MemoryDefinitionRepository(CreateDefinitions(spawnTime: 1, enemyHp: 10, enemySpeed: 5)),
            input);

        for (var frame = 0; frame < 60 * 5 && simulation.Telemetry.EnemiesKilled == 0; frame++)
        {
            simulation.Update(1f / 60f);
        }

        Assert.True(simulation.Player.Get<HealthComponent>().Current > 0);
        Assert.Equal(1, simulation.Telemetry.EnemiesSpawned);
        Assert.True(simulation.Telemetry.EnemyMovementFrames > 0);
        Assert.True(simulation.Telemetry.BulletsSpawned > 0);
        Assert.True(simulation.Telemetry.BulletMovementFrames > 0);
        Assert.True(simulation.Telemetry.CollisionsDetected > 0);
        Assert.True(simulation.Telemetry.DamageEventsApplied > 0);
        Assert.Equal(1, simulation.Telemetry.EnemiesKilled);
        Assert.Equal(100, simulation.Telemetry.Score);
        Assert.Empty(simulation.World.Query<EnemyComponent>());
        Assert.Equal(SimulationStatus.StageClear, simulation.Status);
    }

    [Fact]
    public void EnemyFireCausesGameOverAndRetryStartsFreshRun()
    {
        var input = new MutableInputState();
        var simulation = new ShootingSimulation(
            new MemoryDefinitionRepository(CreateDefinitions(
                spawnTime: 0,
                enemyHp: 10,
                enemySpeed: 0,
                playerHp: 10,
                enemyWeaponId: "weapon")),
            input);

        for (var frame = 0; frame < 60 * 4 && simulation.Status == SimulationStatus.Running; frame++)
        {
            simulation.Update(1f / 60f);
        }

        Assert.Equal(SimulationStatus.GameOver, simulation.Status);
        Assert.True(simulation.Telemetry.EnemyBulletsSpawned > 0);
        Assert.True(simulation.Telemetry.PlayerDamageEventsApplied > 0);
        Assert.Empty(simulation.World.Query<PlayerComponent>());

        var completedWorld = simulation.World;
        var completedElapsed = simulation.Elapsed;
        simulation.Update(1);
        Assert.Equal(completedElapsed, simulation.Elapsed);

        input.Retry = true;
        simulation.Update(1f / 60f);

        Assert.Equal(SimulationStatus.Running, simulation.Status);
        Assert.NotSame(completedWorld, simulation.World);
        Assert.Equal(10, simulation.Player.Get<HealthComponent>().Current);
        Assert.Equal(0, simulation.Elapsed);
        Assert.Equal(0, simulation.Telemetry.EnemyBulletsSpawned);
        Assert.Single(simulation.World.Query<PlayerComponent>());
    }

    [Fact]
    public void ChangingOnlyDefinitionValuesChangesHpSpeedAndSpawnTime()
    {
        var baseline = new ShootingSimulation(
            new MemoryDefinitionRepository(CreateDefinitions(spawnTime: 1, enemyHp: 10, enemySpeed: 5)),
            new MutableInputState());
        var changed = new ShootingSimulation(
            new MemoryDefinitionRepository(CreateDefinitions(spawnTime: 2, enemyHp: 25, enemySpeed: 30)),
            new MutableInputState());

        baseline.Update(1.5f);
        changed.Update(1.5f);

        var baselineEnemy = Assert.Single(baseline.World.Query<EnemyComponent>());
        Assert.Empty(changed.World.Query<EnemyComponent>());
        Assert.Equal(10, baselineEnemy.Get<HealthComponent>().Maximum);
        Assert.Equal(5, baselineEnemy.Get<VelocityComponent>().Value.Y);

        changed.Update(0.5f);
        var changedEnemy = Assert.Single(changed.World.Query<EnemyComponent>());
        Assert.Equal(25, changedEnemy.Get<HealthComponent>().Maximum);
        Assert.Equal(30, changedEnemy.Get<VelocityComponent>().Value.Y);
    }

    private static DefinitionCatalog CreateDefinitions(
        float spawnTime,
        int enemyHp,
        float enemySpeed,
        int playerHp = 100,
        string? enemyWeaponId = null)
    {
        return new DefinitionCatalog(
            new GameDefinition { PlayerId = "player", StageId = "stage", Width = 800, Height = 600 },
            new[]
            {
                new PlayerDefinition
                {
                    Id = "player", Hp = playerHp, Speed = 200, WeaponId = "weapon",
                    X = 0, Y = 300, Radius = 10
                }
            },
            new[]
            {
                new EnemyDefinition
                {
                    Id = "enemy", Hp = enemyHp, Speed = enemySpeed,
                    WeaponId = enemyWeaponId, Radius = 10
                }
            },
            new[]
            {
                new BulletDefinition
                {
                    Id = "bullet", Speed = 100, Damage = 10, Radius = 3, Lifetime = 5
                }
            },
            new[] { new WeaponDefinition { Id = "weapon", BulletId = "bullet", Cooldown = 0.2f } },
            new[]
            {
                new StageDefinition
                {
                    Id = "stage",
                    Events = new[]
                    {
                        new StageEventDefinition
                        {
                            Time = spawnTime, Type = "spawn-enemy", EnemyId = "enemy", X = 0, Y = 100
                        }
                    }
                }
            });
    }
}
