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
            var definitions = new JsonDefinitionRepository(Path.Combine(AppContext.BaseDirectory, "game"));
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
        const int maximumFrames = 60 * 12;

        for (var frame = 0; frame < maximumFrames && simulation.Telemetry.EnemiesKilled == 0; frame++)
        {
            simulation.Update(deltaTime);
        }

        var telemetry = simulation.Telemetry;
        Require(simulation.Player.Has<PlayerComponent>(), "Player was not created.");
        Require(telemetry.EnemiesSpawned > 0, "No enemy was spawned from the stage definition.");
        Require(telemetry.EnemyMovementFrames > 0, "No enemy movement was observed.");
        Require(telemetry.BulletsSpawned > 0, "No bullet was spawned by the weapon system.");
        Require(telemetry.BulletMovementFrames > 0, "No bullet movement was observed.");
        Require(telemetry.CollisionsDetected > 0, "No bullet/enemy collision was detected.");
        Require(telemetry.DamageEventsApplied > 0, "No damage was applied.");
        Require(telemetry.EnemiesKilled > 0, "No enemy reached zero HP.");
        Require(!simulation.World.Query<EnemyComponent>().Any(), "A dead enemy remained in the world.");

        Console.WriteLine(
            $"SMOKE TEST PASSED: spawned={telemetry.EnemiesSpawned}, enemyMovementFrames={telemetry.EnemyMovementFrames}, " +
            $"bullets={telemetry.BulletsSpawned}, " +
            $"movementFrames={telemetry.BulletMovementFrames}, collisions={telemetry.CollisionsDetected}, " +
            $"damage={telemetry.DamageEventsApplied}, killed={telemetry.EnemiesKilled}");
        return 0;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Smoke test failed: {message}");
        }
    }
}
