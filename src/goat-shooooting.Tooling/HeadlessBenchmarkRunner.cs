using System.Diagnostics;
using System.Numerics;
using GoatShooooting.Core;
using GoatShooooting.Definitions;
using GoatShooooting.Runtime;

namespace GoatShooooting.Tooling;

public sealed record HeadlessBenchmarkOptions(
    int SampleTicks = 600,
    int StressBulletCount = 10_000,
    int StressTicks = 600)
{
    public const int MaximumStressBulletCount = 100_000;

    public void Validate()
    {
        if (SampleTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SampleTicks));
        }

        if (StressBulletCount is < 1 or > MaximumStressBulletCount)
        {
            throw new ArgumentOutOfRangeException(nameof(StressBulletCount));
        }

        if (StressTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(StressTicks));
        }
    }
}

public sealed record HeadlessBenchmarkReport(
    int FormatVersion,
    int TickRate,
    IReadOnlyList<HeadlessBenchmarkResult> Scenarios);

public sealed record HeadlessBenchmarkResult(
    string Name,
    int TickCount,
    int InitialActiveEntities,
    int PeakActiveEntities,
    int FinalActiveEntities,
    int InitialActiveBullets,
    int PeakActiveBullets,
    int FinalActiveBullets,
    double UpdateMilliseconds,
    long AllocatedBytes,
    long CollisionCandidatesChecked,
    long WorkloadChecksum);

/// <summary>Runs repeatable, renderer-free workloads while keeping timing informational.</summary>
public static class HeadlessBenchmarkRunner
{
    public const int ReportFormatVersion = 2;

    public static HeadlessBenchmarkReport Run(
        DefinitionCatalog definitions,
        HeadlessBenchmarkOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        options ??= new HeadlessBenchmarkOptions();
        options.Validate();

        return new HeadlessBenchmarkReport(
            ReportFormatVersion,
            SimulationTiming.TicksPerSecond,
            new[]
            {
                RunSample(definitions, options.SampleTicks),
                RunStress(definitions, options.StressBulletCount, options.StressTicks)
            });
    }

    private static HeadlessBenchmarkResult RunSample(DefinitionCatalog definitions, int tickCount)
    {
        var input = new MutableInputState { Fire = true };
        var simulation = new ShootingSimulation(new MemoryDefinitionRepository(definitions), input);
        return Measure("sample", simulation, tickCount, tick =>
        {
            var horizontalPhase = tick % 240;
            input.MoveX = horizontalPhase switch
            {
                < 60 => 1,
                < 120 => 0,
                < 180 => -1,
                _ => 0
            };
            input.MoveY = tick % 180 < 90 ? -0.25f : 0.25f;
            input.Bomb = tick > 0 && tick % 300 == 0;
            simulation.Tick(InputFrame.Capture(input));
        });
    }

    private static HeadlessBenchmarkResult RunStress(
        DefinitionCatalog definitions,
        int bulletCount,
        int tickCount)
    {
        var input = new MutableInputState();
        var simulation = new ShootingSimulation(new MemoryDefinitionRepository(definitions), input);
        while (simulation.Phase == StagePhase.Opening)
        {
            simulation.Tick(default);
        }

        var playerDefinition = definitions.GetPlayer(definitions.Game.PlayerId);
        var weapon = definitions.GetWeapon(playerDefinition.WeaponId);
        var bullet = definitions.GetBullet(weapon.BulletId);
        var factory = new BulletFactory();
        const int columns = 100;
        for (var index = 0; index < bulletCount; index++)
        {
            var column = index % columns;
            var row = index / columns;
            var position = new Vector2(
                ((column + 0.5f) / columns) * definitions.Game.Width,
                ((row % columns) + 0.5f) / columns * definitions.Game.Height);
            factory.Create(
                simulation.Projectiles,
                bullet,
                position,
                -Vector2.UnitY,
                CollisionLayer.Player,
                simulation.Player.Id);
        }

        simulation.Projectiles.CommitSpawns();

        return Measure($"stress-{bulletCount}-bullets", simulation, tickCount, _ =>
            simulation.Tick(default));
    }

    private static HeadlessBenchmarkResult Measure(
        string name,
        ShootingSimulation simulation,
        int tickCount,
        Action<int> update)
    {
        var initialEntities = CountActiveEntities(simulation);
        var initialBullets = simulation.Projectiles.ActiveCount;
        var peakEntities = initialEntities;
        var peakBullets = initialBullets;
        long allocatedBytes = 0;
        long elapsedTimestampTicks = 0;

        for (var tick = 0; tick < tickCount; tick++)
        {
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            update(tick);
            elapsedTimestampTicks += Stopwatch.GetTimestamp() - started;
            allocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            peakEntities = Math.Max(peakEntities, CountActiveEntities(simulation));
            peakBullets = Math.Max(peakBullets, simulation.Projectiles.ActiveCount);
        }

        var finalEntities = CountActiveEntities(simulation);
        var finalBullets = simulation.Projectiles.ActiveCount;
        return new HeadlessBenchmarkResult(
            name,
            tickCount,
            initialEntities,
            peakEntities,
            finalEntities,
            initialBullets,
            peakBullets,
            finalBullets,
            Math.Round(elapsedTimestampTicks * 1000d / Stopwatch.Frequency, 3),
            allocatedBytes,
            simulation.Telemetry.CollisionCandidatesChecked,
            ComputeChecksum(simulation, finalEntities, finalBullets));
    }

    private static int CountActiveEntities(ShootingSimulation simulation) =>
        simulation.World.Entities.Count(static entity => !entity.Has<PendingDestroyComponent>()) +
        simulation.Projectiles.ActiveCount;

    private static long ComputeChecksum(
        ShootingSimulation simulation,
        int activeEntities,
        int activeBullets)
    {
        const long offsetBasis = 1469598103934665603;
        const long prime = 1099511628211;
        var hash = offsetBasis;
        foreach (var value in new long[]
                 {
                     activeEntities,
                     activeBullets,
                     simulation.Telemetry.EnemiesSpawned,
                     simulation.Telemetry.BulletsSpawned,
                     simulation.Telemetry.CollisionsDetected,
                     simulation.Telemetry.CollisionCandidatesChecked,
                     simulation.Telemetry.DamageEventsApplied,
                     simulation.Telemetry.Score
                 })
        {
            hash = unchecked((hash ^ value) * prime);
        }

        return hash;
    }
}
