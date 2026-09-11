namespace GoatShooooting.Runtime;

/// <summary>Observable facts emitted by the real simulation path for diagnostics and acceptance tests.</summary>
public sealed class SimulationTelemetry
{
    public int EnemiesSpawned { get; internal set; }
    public int EnemyMovementFrames { get; internal set; }
    public int BulletsSpawned { get; internal set; }
    public int EnemyBulletsSpawned { get; internal set; }
    public int BulletMovementFrames { get; internal set; }
    public int CollisionsDetected { get; internal set; }
    public int DamageEventsApplied { get; internal set; }
    public int PlayerDamageEventsApplied { get; internal set; }
    public int EnemiesKilled { get; internal set; }
    public int Score { get; internal set; }
}
