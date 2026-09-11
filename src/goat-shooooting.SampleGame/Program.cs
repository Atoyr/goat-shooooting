using GoatShooooting.Core;
using GoatShooooting.Definitions;
using GoatShooooting.Framework;
using GoatShooooting.Runtime;

namespace GoatShooooting.SampleGame;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var gameId = GetGameId(args);
            var gameDirectory = Path.Combine(AppContext.BaseDirectory, "games", gameId);
            IDefinitionRepository definitions = args.Contains("--smoke-test", StringComparer.Ordinal)
                ? new JsonDefinitionRepository(gameDirectory)
                : new ReloadableJsonDefinitionRepository(gameDirectory);
            if (args.Contains("--smoke-test", StringComparer.Ordinal))
            {
                return RunSmokeTest(definitions);
            }

            using var game = new ShootingGame(definitions);
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
        const float deltaTime = 1f / 60f;
        const int maximumFrames = 60 * 150;
        var stage = simulation.Definitions.GetStage(simulation.Definitions.Game.StageId);
        var expectedEnemies = stage.Events.Sum(static stageEvent => stageEvent.Count);
        var expectedScore = stage.Events.Sum(stageEvent =>
            simulation.Definitions.GetEnemy(stageEvent.EnemyId).Score * stageEvent.Count);
        var lastEventTime = stage.Events.Count == 0
            ? 0
            : stage.Events.Max(static stageEvent =>
                stageEvent.Time + ((stageEvent.Count - 1) * stageEvent.SpawnInterval));

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

            simulation.Update(deltaTime);
        }

        var telemetry = simulation.Telemetry;
        Require(simulation.Player.Has<PlayerComponent>(), "Player was not created.");
        Require(telemetry.EnemiesSpawned == expectedEnemies, "The complete stage definition was not simulated.");
        Require(telemetry.EnemyMovementFrames > 0, "No enemy movement was observed.");
        Require(telemetry.BulletsSpawned > 0, "No bullet was spawned by the weapon system.");
        Require(telemetry.EnemyBulletsSpawned > 0, "No enemy bullet was spawned by the weapon system.");
        Require(telemetry.BulletMovementFrames > 0, "No bullet movement was observed.");
        Require(telemetry.CollisionsDetected > 0, "No bullet/enemy collision was detected.");
        Require(telemetry.DamageEventsApplied > 0, "No damage was applied.");
        Require(telemetry.EnemiesKilled > 0, "No enemy reached zero HP.");
        Require(
            telemetry.EnemiesKilled == telemetry.EnemiesSpawned,
            $"Not every spawned enemy was defeated (spawned={telemetry.EnemiesSpawned}, killed={telemetry.EnemiesKilled}, " +
            $"status={simulation.Status}, hp={simulation.Player.Get<HealthComponent>().Current}).");
        Require(telemetry.Score == expectedScore, "The expected score was not awarded for the complete stage.");
        Require(simulation.Elapsed >= lastEventTime, "The simulation did not run through the final wave.");
        Require(!simulation.World.Query<EnemyComponent>().Any(), "A dead enemy remained in the world.");
        Require(simulation.Feedback.EnemiesDestroyed > 0, "No enemy destruction feedback was emitted.");
        Require(simulation.World.Query<ExplosionComponent>().Any(), "No explosion effect was created.");
        Require(simulation.Status == SimulationStatus.StageClear, "The simulation did not reach Stage Clear.");

        input.Fire = false;
        input.Retry = true;
        simulation.Update(0);
        Require(simulation.Status == SimulationStatus.Running, "Retry did not start a new run.");
        Require(simulation.Player.Get<HealthComponent>().Current == simulation.Player.Get<HealthComponent>().Maximum,
            "Retry did not restore player health.");
        Require(simulation.Telemetry.EnemiesSpawned == 0, "Retry did not reset telemetry.");

        Console.WriteLine(
            $"SMOKE TEST PASSED: spawned={telemetry.EnemiesSpawned}, enemyMovementFrames={telemetry.EnemyMovementFrames}, " +
            $"bullets={telemetry.BulletsSpawned}, enemyBullets={telemetry.EnemyBulletsSpawned}, " +
            $"movementFrames={telemetry.BulletMovementFrames}, collisions={telemetry.CollisionsDetected}, " +
            $"damage={telemetry.DamageEventsApplied}, killed={telemetry.EnemiesKilled}, retry=passed");
        return 0;
    }

    private static string GetGameId(string[] args)
    {
        var optionIndex = Array.FindIndex(args, static argument => string.Equals(argument, "--game", StringComparison.Ordinal));
        var gameId = optionIndex >= 0 && optionIndex + 1 < args.Length ? args[optionIndex + 1] : "sample";
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
