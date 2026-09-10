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
        Assert.Empty(simulation.World.Query<EnemyComponent>());
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

    private static DefinitionCatalog CreateDefinitions(float spawnTime, int enemyHp, float enemySpeed)
    {
        return new DefinitionCatalog(
            new GameDefinition { PlayerId = "player", StageId = "stage", Width = 800, Height = 600 },
            new[]
            {
                new PlayerDefinition
                {
                    Id = "player", Hp = 100, Speed = 200, WeaponId = "weapon",
                    X = 0, Y = 300, Radius = 10
                }
            },
            new[] { new EnemyDefinition { Id = "enemy", Hp = enemyHp, Speed = enemySpeed, Radius = 10 } },
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
