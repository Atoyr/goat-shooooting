namespace GoatShooooting.Runtime;

public interface IGameplayEvent
{
    long Frame { get; }
    int Sequence { get; }
}

public sealed record EnemyDamagedEvent(
    long Frame,
    int Sequence,
    int EnemyEntityId,
    int Damage) : IGameplayEvent;

public sealed record EnemyDestroyedEvent(
    long Frame,
    int Sequence,
    int EnemyEntityId,
    string EnemyDefinitionId,
    int BaseScore = 0,
    float? DistanceToPlayer = null) : IGameplayEvent;

public sealed record PlayerGrazedEvent(
    long Frame,
    int Sequence,
    int PlayerEntityId,
    int ProjectileEntityId) : IGameplayEvent;

public sealed record ProjectileSpawnedEvent(
    long Frame,
    int Sequence,
    int ProjectileEntityId,
    int OwnerEntityId,
    ProjectileTeam Team,
    string ProjectileDefinitionId) : IGameplayEvent;

public sealed record ProjectileHitEvent(
    long Frame,
    int Sequence,
    int ProjectileEntityId,
    int TargetEntityId,
    int Damage,
    ProjectileTeam Team = ProjectileTeam.Player) : IGameplayEvent;

public sealed record ProjectileCancelledEvent(
    long Frame,
    int Sequence,
    int ProjectileEntityId,
    bool AwardsScore = true) : IGameplayEvent;

public sealed record ItemCollectedEvent(
    long Frame,
    int Sequence,
    int PlayerEntityId,
    string ItemDefinitionId,
    int Value,
    string Kind = "",
    int ScoreValue = 0) : IGameplayEvent;

public sealed record ItemSpawnedEvent(
    long Frame,
    int Sequence,
    int ItemEntityId,
    string ItemDefinitionId) : IGameplayEvent;

public sealed record PlayerHitEvent(
    long Frame,
    int Sequence,
    int PlayerEntityId,
    int? ProjectileEntityId) : IGameplayEvent;

public sealed record PlayerDiedEvent(
    long Frame,
    int Sequence,
    int PlayerEntityId,
    int RemainingLives) : IGameplayEvent;

public sealed record PlayerRespawnedEvent(
    long Frame,
    int Sequence,
    int PlayerEntityId) : IGameplayEvent;

public sealed record PowerChangedEvent(
    long Frame,
    int Sequence,
    int PlayerEntityId,
    int PreviousPower,
    int CurrentPower,
    string Reason) : IGameplayEvent;

public sealed record ExtendAwardedEvent(
    long Frame,
    int Sequence,
    int PlayerEntityId,
    string Source,
    long? ScoreThreshold = null) : IGameplayEvent;

public sealed record ContinueUsedEvent(
    long Frame,
    int Sequence,
    int PlayerEntityId,
    int CreditsRemaining) : IGameplayEvent;

public enum BombUsageKind
{
    Manual,
    Auto
}

public sealed record BombUsedEvent(
    long Frame,
    int Sequence,
    int PlayerEntityId,
    BombUsageKind Kind = BombUsageKind.Manual) : IGameplayEvent;

public sealed record BossPhaseEndedEvent(
    long Frame,
    int Sequence,
    string BossDefinitionId,
    string PhaseId,
    bool TimedOut,
    int BossEntityId = 0) : IGameplayEvent;

public sealed record BossPhaseStartedEvent(
    long Frame,
    int Sequence,
    int BossEntityId,
    string BossDefinitionId,
    string PhaseId,
    int PhaseIndex,
    string? CheckpointId) : IGameplayEvent;

public sealed record BossPhaseBonusEvent(
    long Frame,
    int Sequence,
    int BossEntityId,
    string BossDefinitionId,
    string PhaseId,
    long BaseBonus,
    long TimeBonus,
    long NoMissBonus,
    long NoBombBonus) : IGameplayEvent;

public sealed record BossCompletedEvent(
    long Frame,
    int Sequence,
    int BossEntityId,
    string BossDefinitionId) : IGameplayEvent;

public sealed record StageClearedEvent(
    long Frame,
    int Sequence,
    string StageId,
    int StageNumber) : IGameplayEvent;

public sealed record AllClearedEvent(
    long Frame,
    int Sequence,
    string StageId,
    int RemainingLives,
    int RemainingBombs) : IGameplayEvent;

public sealed record ScoreAwardedEvent(
    long Frame,
    int Sequence,
    string Reason,
    long BaseAmount,
    double Multiplier,
    long FinalAmount,
    string Source,
    string Category) : IGameplayEvent;

/// <summary>Collects ordered gameplay facts for the current simulation tick only.</summary>
public sealed class GameEventBuffer
{
    private readonly List<IGameplayEvent> _events = new();
    private bool _tickStarted;

    public long Frame { get; private set; }
    public IReadOnlyList<IGameplayEvent> Events => _events;

    public void BeginTick(long frame)
    {
        if (frame < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frame));
        }

        Frame = frame;
        _events.Clear();
        _tickStarted = true;
    }

    public T Publish<T>(Func<long, int, T> createEvent)
        where T : IGameplayEvent
    {
        ArgumentNullException.ThrowIfNull(createEvent);
        if (!_tickStarted)
        {
            throw new InvalidOperationException("BeginTick must be called before publishing gameplay events.");
        }

        var sequence = _events.Count;
        var gameplayEvent = createEvent(Frame, sequence);
        if (gameplayEvent is null)
        {
            throw new InvalidOperationException("The gameplay event factory returned null.");
        }

        if (gameplayEvent.Frame != Frame || gameplayEvent.Sequence != sequence)
        {
            throw new InvalidOperationException(
                $"Gameplay event stamp must match frame {Frame} and sequence {sequence}.");
        }

        _events.Add(gameplayEvent);
        return gameplayEvent;
    }
}
