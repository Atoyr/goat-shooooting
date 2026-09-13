using GoatShooooting.Core;
using GoatShooooting.Definitions;
using GoatShooooting.Framework;
using GoatShooooting.Platform;
using GoatShooooting.Runtime;

namespace GoatShooooting.SampleGame;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var requestedGameId = GetRequestedGameId(args);
            if (args.Contains("--smoke-test", StringComparer.Ordinal))
            {
                var smokeGameId = requestedGameId ?? "sample";
                var gameDirectory = Path.Combine(AppContext.BaseDirectory, "games", smokeGameId);
                return RunSmokeTest(new JsonDefinitionRepository(gameDirectory));
            }

            var userDataDirectory = UserDataPathResolver.GetDefaultDirectory();
            var userDataStore = new JsonUserDataStore(userDataDirectory);
            var leaderboard = new LocalLeaderboardService(userDataDirectory);
            var replayStore = new JsonReplayStore(userDataDirectory);
            var settings = userDataStore.LoadSettings().Value;
            var profile = userDataStore.LoadProfile().Value;
            var definitions = GetAvailableGames();
            var gameId = requestedGameId ?? profile.LastGameId;
            if (!definitions.ContainsKey(gameId))
            {
                gameId = definitions.ContainsKey("sample") ? "sample" : definitions.Keys.First();
            }

            if (!string.Equals(profile.LastGameId, gameId, StringComparison.Ordinal))
            {
                profile = new PlayerProfileService().SelectGame(profile, gameId);
                userDataStore.SaveProfile(profile);
            }

            using var game = new ShootingGame(
                definitions, gameId, userDataStore, settings, profile, leaderboard, replayStore);
            game.Run();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"goat-shooooting failed: {exception}");
            return 1;
        }
    }

    private static int RunSmokeTest(IDefinitionRepository definitions)
    {
        var input = new MutableInputState { Fire = true };
        var simulation = new ShootingSimulation(definitions, input);
        // The smoke runner is intentionally long-lived so it can exercise the complete stage path.
        // Retry below verifies that runtime state returns to the configured life count.
        simulation.Player.Get<LivesComponent>().Remaining = 100;
        const int maximumFrames = 60 * 210;
        var stages = GetStageRoute(simulation.Definitions);
        var expectedEnemies = stages.Sum(stage =>
            stage.Events.Sum(static stageEvent => stageEvent.Count));
        var expectedScore = stages.Sum(stage => stage.Events.Sum(stageEvent =>
            simulation.Definitions.GetEnemy(stageEvent.EnemyId).Score * stageEvent.Count));
        var finalStage = stages[^1];
        var lastEventTime = finalStage.Events.Count == 0
            ? 0
            : finalStage.Events.Max(static stageEvent =>
                stageEvent.Time + ((stageEvent.Count - 1) * stageEvent.SpawnInterval));
        var observedDestructionFeedback = false;
        var observedExplosionEffect = false;
        for (var frame = 0; frame < maximumFrames && simulation.Status == SimulationStatus.Running; frame++)
        {
            var target = simulation.World.Query<EnemyComponent>()
                .OrderByDescending(static enemy => enemy.Get<TransformComponent>().Position.Y)
                .FirstOrDefault();
            if (target is null)
            {
                input.MoveX = 0;
            }
            else
            {
                var deltaX = target.Get<TransformComponent>().Position.X
                    - simulation.Player.Get<TransformComponent>().Position.X;
                input.MoveX = Math.Abs(deltaX) < 4 ? 0 : Math.Sign(deltaX);
            }

            var bombWasPressed = input.Bomb;
            input.Bomb = false;
            if (!bombWasPressed &&
                simulation.Player.Get<BombComponent>().Remaining > 0 &&
                CountEnemyProjectiles(simulation.Projectiles) >= 8)
            {
                input.Bomb = true;
            }

            simulation.Tick(InputFrame.Capture(input));
            observedDestructionFeedback |= simulation.Feedback.EnemiesDestroyed > 0;
            observedExplosionEffect |= simulation.World.Query<ExplosionComponent>().Any();
        }

        var telemetry = simulation.Telemetry;
        Require(simulation.Player.Has<PlayerComponent>(), "Player was not created.");
        Require(
            telemetry.EnemiesSpawned == expectedEnemies,
            $"The complete stage definition was not simulated (spawned={telemetry.EnemiesSpawned}, " +
            $"expected={expectedEnemies}, elapsed={simulation.Elapsed:F2}, status={simulation.Status}, " +
            $"lives={simulation.Player.Get<LivesComponent>().Remaining}, bombs={telemetry.BombsUsed}).");
        Require(telemetry.EnemyMovementFrames > 0, "No enemy movement was observed.");
        Require(telemetry.BulletsSpawned > 0, "No bullet was spawned by the weapon system.");
        Require(telemetry.EnemyBulletsSpawned > 0, "No enemy bullet was spawned by the weapon system.");
        Require(telemetry.BulletMovementFrames > 0, "No bullet movement was observed.");
        Require(telemetry.CollisionsDetected > 0, "No bullet/enemy collision was detected.");
        Require(telemetry.DamageEventsApplied > 0, "No damage was applied.");
        Require(telemetry.EnemiesKilled > 0, "No enemy reached zero HP.");
        Require(telemetry.BombsUsed > 0, "No bomb was used.");
        Require(telemetry.EnemyBulletsCleared > 0, "No enemy bullet was cleared by a bomb.");
        Require(
            telemetry.EnemiesKilled == telemetry.EnemiesSpawned,
            $"Not every spawned enemy was defeated (spawned={telemetry.EnemiesSpawned}, killed={telemetry.EnemiesKilled}, " +
            $"status={simulation.Status}, lives={simulation.Player.Get<LivesComponent>().Remaining}).");
        Require(telemetry.Score == expectedScore, "The expected score was not awarded for the complete stage.");
        Require(simulation.Elapsed >= lastEventTime, "The simulation did not run through the final wave.");
        Require(!simulation.World.Query<EnemyComponent>().Any(), "A dead enemy remained in the world.");
        Require(observedDestructionFeedback, "No enemy destruction feedback was emitted.");
        Require(observedExplosionEffect, "No explosion effect was created.");
        Require(simulation.Status == SimulationStatus.StageClear, "The simulation did not reach Stage Clear.");

        input.Fire = false;
        input.Retry = true;
        simulation.Tick(InputFrame.Capture(input));
        Require(simulation.Status == SimulationStatus.Running, "Retry did not start a new run.");
        Require(simulation.Player.Get<LivesComponent>().Remaining == simulation.Player.Get<LivesComponent>().Initial,
            "Retry did not restore player lives.");
        Require(simulation.Player.Get<BombComponent>().Remaining == simulation.Player.Get<BombComponent>().Initial,
            "Retry did not restore player bombs.");
        Require(simulation.Telemetry.EnemiesSpawned == 0, "Retry did not reset telemetry.");

        Console.WriteLine(
            $"SMOKE TEST PASSED: spawned={telemetry.EnemiesSpawned}, enemyMovementFrames={telemetry.EnemyMovementFrames}, " +
            $"bullets={telemetry.BulletsSpawned}, enemyBullets={telemetry.EnemyBulletsSpawned}, " +
            $"movementFrames={telemetry.BulletMovementFrames}, collisions={telemetry.CollisionsDetected}, " +
            $"damage={telemetry.DamageEventsApplied}, killed={telemetry.EnemiesKilled}, bombs={telemetry.BombsUsed}, " +
            $"enemyBulletsCleared={telemetry.EnemyBulletsCleared}, retry=passed");
        return 0;
    }

    private static IReadOnlyList<StageDefinition> GetStageRoute(DefinitionCatalog definitions)
    {
        var stages = new List<StageDefinition>();
        var stage = definitions.GetStage(definitions.Game.StageId);
        while (true)
        {
            stages.Add(stage);
            if (string.IsNullOrWhiteSpace(stage.NextStageId))
            {
                return stages;
            }

            stage = definitions.GetStage(stage.NextStageId);
        }
    }

    private static int CountEnemyProjectiles(ProjectileStore projectiles)
    {
        var count = 0;
        for (var index = 0; index < projectiles.ActiveCount; index++)
        {
            var projectile = projectiles.GetSnapshot(index);
            if (!projectile.PendingRemoval && projectile.Team == ProjectileTeam.Enemy)
            {
                count++;
            }
        }

        return count;
    }

    private static IReadOnlyDictionary<string, IDefinitionRepository> GetAvailableGames()
    {
        var gamesDirectory = Path.Combine(AppContext.BaseDirectory, "games");
        var definitions = Directory
            .EnumerateDirectories(gamesDirectory)
            .Where(directory => File.Exists(Path.Combine(directory, "game.json")))
            .Select(directory => new
            {
                Id = Path.GetFileName(directory),
                Repository = (IDefinitionRepository)new ReloadableJsonDefinitionRepository(directory)
            })
            .OrderBy(static item => item.Id, StringComparer.Ordinal)
            .ToDictionary(static item => item.Id, static item => item.Repository, StringComparer.Ordinal);
        if (definitions.Count == 0)
        {
            throw new DirectoryNotFoundException($"No game content packs were found under '{gamesDirectory}'.");
        }

        return definitions;
    }

    private static string? GetRequestedGameId(string[] args)
    {
        var optionIndex = Array.FindIndex(args, static argument => string.Equals(argument, "--game", StringComparison.Ordinal));
        if (optionIndex < 0)
        {
            return null;
        }

        var gameId = optionIndex + 1 < args.Length ? args[optionIndex + 1] : string.Empty;
        if (string.IsNullOrWhiteSpace(gameId) ||
            !string.Equals(gameId, Path.GetFileName(gameId), StringComparison.Ordinal) ||
            gameId.Any(static character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException($"Invalid game id '{gameId}'. Use a directory name such as 'sample' or 'gauntlet'.");
        }

        return gameId;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Smoke test failed: {message}");
        }
    }
}
