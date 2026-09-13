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

        Assert.True(simulation.Player.Get<LivesComponent>().Remaining > 0);
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
        Assert.Equal(1, simulation.Feedback.EnemiesDestroyed);
    }

    [Fact]
    public void BossDefeatShowsResultsThenStartsNextStageWithRunStatePreserved()
    {
        var input = new MutableInputState { Fire = true };
        var simulation = new ShootingSimulation(
            new MemoryDefinitionRepository(CreateStageFlowDefinitions()),
            input);

        Assert.Equal(StagePhase.Opening, simulation.Phase);
        Assert.Equal("OPENING", simulation.CurrentStage.Title);
        Assert.Empty(simulation.World.Query<EnemyComponent>());

        simulation.Update(1);
        Assert.Equal(StagePhase.Playing, simulation.Phase);
        simulation.Update(0);
        Assert.True(Assert.Single(simulation.World.Query<EnemyComponent>()).Has<BossComponent>());

        for (var frame = 0; frame < 300 && simulation.Phase == StagePhase.Playing; frame++)
        {
            simulation.Update(1f / 60f);
        }

        Assert.Equal(StagePhase.Results, simulation.Phase);
        Assert.Equal(SimulationStatus.Running, simulation.Status);
        Assert.Equal(100, simulation.LastStageScore);
        Assert.Equal(100, simulation.Telemetry.Score);
        Assert.Equal(1, simulation.Telemetry.BossesKilled);
        Assert.Empty(simulation.World.Query<EnemyComponent>());

        var remainingLives = simulation.Player.Get<LivesComponent>().Remaining;
        simulation.Update(2);

        Assert.Equal(2, simulation.StageNumber);
        Assert.Equal("stage-02", simulation.CurrentStage.Id);
        Assert.Equal(StagePhase.Opening, simulation.Phase);
        Assert.Equal(100, simulation.Telemetry.Score);
        Assert.Equal(remainingLives, simulation.Player.Get<LivesComponent>().Remaining);
        Assert.Empty(simulation.World.Query<EnemyComponent>());
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
                playerLives: 1,
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
        Assert.Equal(1, simulation.Player.Get<LivesComponent>().Remaining);
        Assert.Equal(2, simulation.Player.Get<BombComponent>().Remaining);
        Assert.Equal(0, simulation.Elapsed);
        Assert.Equal(0, simulation.Telemetry.EnemyBulletsSpawned);
        Assert.Single(simulation.World.Query<PlayerComponent>());
    }

    [Fact]
    public void BombInputDamagesEnemyAndClearsEnemyBulletsEndToEnd()
    {
        var input = new MutableInputState();
        var simulation = new ShootingSimulation(
            new MemoryDefinitionRepository(CreateDefinitions(
                spawnTime: 0,
                enemyHp: 100,
                enemySpeed: 0,
                enemyWeaponId: "weapon")),
            input);
        simulation.Update(0);
        Assert.True(CountProjectiles(simulation.Projectiles, ProjectileTeam.Enemy) > 0);

        input.Bomb = true;
        simulation.Update(0);

        var enemy = Assert.Single(simulation.World.Query<EnemyComponent>());
        Assert.Equal(50, enemy.Get<HealthComponent>().Current);
        Assert.Equal(0, CountProjectiles(simulation.Projectiles, ProjectileTeam.Enemy));
        Assert.Equal(1, simulation.Player.Get<BombComponent>().Remaining);
        Assert.Equal(1, simulation.Telemetry.BombsUsed);
        Assert.True(simulation.Telemetry.EnemyBulletsCleared > 0);
        Assert.Equal(1, simulation.Feedback.BombsUsed);

        simulation.Update(0);
        Assert.Equal(50, enemy.Get<HealthComponent>().Current);
        Assert.Equal(1, simulation.Telemetry.BombsUsed);
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

    [Fact]
    public void PauseFreezesSimulationUntilAPausePressResumesIt()
    {
        var input = new MutableInputState();
        var simulation = new ShootingSimulation(
            new MemoryDefinitionRepository(CreateDefinitions(spawnTime: 10, enemyHp: 10, enemySpeed: 0)),
            input);
        simulation.Update(1);

        input.Pause = true;
        simulation.Update(1);
        Assert.True(simulation.IsPaused);
        Assert.Equal(1, simulation.Elapsed);

        simulation.Update(1);
        Assert.Equal(1, simulation.Elapsed);

        input.Pause = false;
        simulation.Update(0);
        input.Pause = true;
        simulation.Update(0);
        Assert.False(simulation.IsPaused);

        input.Pause = false;
        simulation.Update(1);
        Assert.Equal(2, simulation.Elapsed);
    }

    [Fact]
    public void RuntimeAppliesValidHotReloadAndKeepsLastGoodDefinitionsOnError()
    {
        var repository = new StubReloadableRepository(
            CreateDefinitions(spawnTime: 10, enemyHp: 10, enemySpeed: 0, playerSpeed: 200));
        var simulation = new ShootingSimulation(repository, new MutableInputState());
        var originalWorld = simulation.World;

        repository.Next = new DefinitionReloadResult(
            CreateDefinitions(spawnTime: 10, enemyHp: 10, enemySpeed: 0, playerSpeed: 350),
            null);
        simulation.Update(0);

        Assert.Equal(1, simulation.DefinitionReloadCount);
        Assert.NotSame(originalWorld, simulation.World);
        Assert.Equal(350, simulation.Player.Get<PlayerComponent>().Speed);
        Assert.Null(simulation.DefinitionReloadError);

        var lastGoodWorld = simulation.World;
        repository.Next = new DefinitionReloadResult(null, "enemy hp is invalid");
        simulation.Update(0);

        Assert.Same(lastGoodWorld, simulation.World);
        Assert.Equal("enemy hp is invalid", simulation.DefinitionReloadError);
    }

    private static DefinitionCatalog CreateDefinitions(
        float spawnTime,
        int enemyHp,
        float enemySpeed,
        int playerLives = 2,
        string? enemyWeaponId = null,
        float playerSpeed = 200)
    {
        return new DefinitionCatalog(
            new GameDefinition { PlayerId = "player", StageId = "stage", Width = 800, Height = 600 },
            new[]
            {
                new PlayerDefinition
                {
                    Id = "player", Lives = playerLives, Speed = playerSpeed, WeaponId = "weapon",
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

    private static int CountProjectiles(ProjectileStore projectiles, ProjectileTeam team)
    {
        var count = 0;
        for (var index = 0; index < projectiles.ActiveCount; index++)
        {
            var projectile = projectiles.GetSnapshot(index);
            if (!projectile.PendingRemoval && projectile.Team == team)
            {
                count++;
            }
        }

        return count;
    }

    private static DefinitionCatalog CreateStageFlowDefinitions()
    {
        var baseline = CreateDefinitions(spawnTime: 0, enemyHp: 10, enemySpeed: 0);
        return new DefinitionCatalog(
            baseline.Game with { StageId = "stage-01" },
            baseline.Players.Values,
            baseline.Enemies.Values,
            baseline.Bullets.Values,
            baseline.Weapons.Values,
            new[]
            {
                new StageDefinition
                {
                    Id = "stage-01",
                    Title = "OPENING",
                    OpeningDuration = 1,
                    ResultsDuration = 2,
                    NextStageId = "stage-02",
                    Events = new[]
                    {
                        new StageEventDefinition
                        {
                            Time = 0, Type = "spawn-enemy", EnemyId = "enemy", X = 0, Y = 100,
                            IsBoss = true
                        }
                    }
                },
                new StageDefinition
                {
                    Id = "stage-02",
                    Title = "NEXT",
                    OpeningDuration = 1
                }
            });
    }

    private sealed class StubReloadableRepository(DefinitionCatalog initial) : IReloadableDefinitionRepository
    {
        public DefinitionReloadResult? Next { get; set; }

        public DefinitionCatalog Load() => initial;

        public DefinitionReloadResult? PollChanges()
        {
            var result = Next;
            Next = null;
            return result;
        }
    }
}
