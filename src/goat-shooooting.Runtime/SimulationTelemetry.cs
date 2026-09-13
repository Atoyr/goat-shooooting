namespace GoatShooooting.Runtime;

public readonly record struct SimulationFeedback(int Hits, int EnemiesDestroyed, int PlayerHits, int BombsUsed)
{
    public static SimulationFeedback FromEvents(IEnumerable<IGameplayEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var hits = 0;
        var enemiesDestroyed = 0;
        var playerHits = 0;
        var bombsUsed = 0;
        foreach (var gameplayEvent in events)
        {
            switch (gameplayEvent)
            {
                case EnemyDamagedEvent:
                    hits++;
                    break;
                case EnemyDestroyedEvent:
                    enemiesDestroyed++;
                    break;
                case PlayerHitEvent:
                    hits++;
                    playerHits++;
                    break;
                case BombUsedEvent:
                    bombsUsed++;
                    break;
            }
        }

        return new SimulationFeedback(hits, enemiesDestroyed, playerHits, bombsUsed);
    }

    public static SimulationFeedback operator +(SimulationFeedback left, SimulationFeedback right) => new(
        left.Hits + right.Hits,
        left.EnemiesDestroyed + right.EnemiesDestroyed,
        left.PlayerHits + right.PlayerHits,
        left.BombsUsed + right.BombsUsed);
}

/// <summary>Observable facts emitted by the real simulation path for diagnostics and acceptance tests.</summary>
public sealed class SimulationTelemetry
{
    public int EnemiesSpawned { get; internal set; }
    public int EnemyMovementFrames { get; internal set; }
    public int BulletsSpawned { get; internal set; }
    public int EnemyBulletsSpawned { get; internal set; }
    public int BulletMovementFrames { get; internal set; }
    public int CollisionsDetected { get; internal set; }
    public long CollisionCandidatesChecked { get; internal set; }
    public int PlayerGrazes { get; internal set; }
    public int DamageEventsApplied { get; internal set; }
    public int PlayerDamageEventsApplied { get; internal set; }
    public int BombsUsed { get; internal set; }
    public int AutoBombsUsed { get; internal set; }
    public int EnemyBulletsCleared { get; internal set; }
    public int EnemiesKilled { get; internal set; }
    public int BossesKilled { get; internal set; }
    public long Score { get; internal set; }
    public int ItemsSpawned { get; internal set; }
    public int ItemsCollected { get; internal set; }
    public int PlayerDeaths { get; internal set; }
    public int PlayerRespawns { get; internal set; }
    public int ExtendsAwarded { get; internal set; }
    public int ContinuesUsed { get; internal set; }
}
