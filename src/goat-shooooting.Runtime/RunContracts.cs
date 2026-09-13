namespace GoatShooooting.Runtime;

/// <summary>Stable timing constants shared by simulation, replay, and tooling.</summary>
public static class SimulationTiming
{
    public const int TicksPerSecond = 60;
    public const float TickDurationSeconds = 1f / TicksPerSecond;
    public const double ExactTickDurationSeconds = 1d / TicksPerSecond;
}

/// <summary>Immutable choices that identify and reproduce one run.</summary>
public sealed record RunConfiguration
{
    public RunConfiguration(
        string gameId,
        long seed,
        string? ruleSetId = null,
        string? difficultyId = null,
        string? shipId = null,
        string? startStageId = null,
        string? checkpointId = null,
        bool isPractice = false,
        int? initialPower = null,
        int? initialLives = null,
        int? initialBombs = null,
        double? initialRank = null,
        int? initialGauge = null,
        float? initialInvincibilitySeconds = null,
        bool slowPractice = false,
        bool showHitboxes = false)
    {
        GameId = RequireId(gameId, nameof(gameId));
        Seed = seed;
        RuleSetId = OptionalId(ruleSetId, nameof(ruleSetId));
        DifficultyId = OptionalId(difficultyId, nameof(difficultyId));
        ShipId = OptionalId(shipId, nameof(shipId));
        StartStageId = OptionalId(startStageId, nameof(startStageId));
        CheckpointId = OptionalId(checkpointId, nameof(checkpointId));
        if (initialPower < 0) throw new ArgumentOutOfRangeException(nameof(initialPower));
        if (initialLives <= 0) throw new ArgumentOutOfRangeException(nameof(initialLives));
        if (initialBombs < 0) throw new ArgumentOutOfRangeException(nameof(initialBombs));
        if (initialRank is { } rank && (!double.IsFinite(rank) || rank < 0))
            throw new ArgumentOutOfRangeException(nameof(initialRank));
        if (initialGauge < 0) throw new ArgumentOutOfRangeException(nameof(initialGauge));
        if (initialInvincibilitySeconds is { } invincibility &&
            (!float.IsFinite(invincibility) || invincibility < 0))
            throw new ArgumentOutOfRangeException(nameof(initialInvincibilitySeconds));
        IsPractice = isPractice;
        InitialPower = initialPower;
        InitialLives = initialLives;
        InitialBombs = initialBombs;
        InitialRank = initialRank;
        InitialGauge = initialGauge;
        InitialInvincibilitySeconds = initialInvincibilitySeconds;
        SlowPractice = slowPractice;
        ShowHitboxes = showHitboxes;
    }

    public string GameId { get; }
    public long Seed { get; }
    public string? RuleSetId { get; }
    public string? DifficultyId { get; }
    public string? ShipId { get; }
    public string? StartStageId { get; }
    public string? CheckpointId { get; }
    public bool IsPractice { get; }
    public int? InitialPower { get; }
    public int? InitialLives { get; }
    public int? InitialBombs { get; }
    public double? InitialRank { get; }
    public int? InitialGauge { get; }
    public float? InitialInvincibilitySeconds { get; }
    public bool SlowPractice { get; }
    public bool ShowHitboxes { get; }

    private static string RequireId(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }

    private static string? OptionalId(string? value, string parameterName)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An optional identifier must be null or non-whitespace.", parameterName);
        }

        return value;
    }
}

/// <summary>Mutable, authoritative values accumulated while a run is in progress.</summary>
public sealed class RunState
{
    private readonly HashSet<long> _claimedExtendThresholds = new();
    private readonly Dictionary<string, long> _scoreBreakdown = new(StringComparer.Ordinal);

    public long Frame { get; internal set; }
    public long Score { get; internal set; }
    public int Power { get; internal set; }
    public int Gauge { get; internal set; }
    public double Rank { get; internal set; }
    public SpecialGaugePhase SpecialPhase { get; internal set; }
    public int SpecialLevel { get; internal set; }
    public double SpecialTimeRemaining { get; internal set; }
    public double SpecialCooldownRemaining { get; internal set; }
    public double SpecialScoreMultiplier { get; internal set; } = 1;
    internal double SpecialGaugeValue { get; set; }
    public int CreditsRemaining { get; internal set; }
    public int ContinuesUsed { get; internal set; }
    public bool Continued { get; internal set; }
    public int Chain { get; internal set; }
    public int MaximumChain { get; internal set; }
    public int HitCombo { get; internal set; }
    public int ConsecutiveItems { get; internal set; }
    public double Multiplier { get; internal set; } = 1;
    public long LastKillFrame { get; internal set; } = -1;
    public long LastHitFrame { get; internal set; } = -1;
    public long LastItemFrame { get; internal set; } = -1;
    public IReadOnlyDictionary<string, long> ScoreBreakdown => _scoreBreakdown;
    public IReadOnlyCollection<long> ClaimedExtendThresholds => _claimedExtendThresholds;

    internal void Reset(int initialPower = 0, int initialCredits = 0)
    {
        Frame = 0;
        Score = 0;
        Power = initialPower;
        Gauge = 0;
        Rank = 0;
        SpecialPhase = SpecialGaugePhase.Inactive;
        SpecialLevel = 0;
        SpecialTimeRemaining = 0;
        SpecialCooldownRemaining = 0;
        SpecialScoreMultiplier = 1;
        SpecialGaugeValue = 0;
        CreditsRemaining = initialCredits;
        ContinuesUsed = 0;
        Continued = false;
        Chain = 0;
        MaximumChain = 0;
        HitCombo = 0;
        ConsecutiveItems = 0;
        Multiplier = 1;
        LastKillFrame = -1;
        LastHitFrame = -1;
        LastItemFrame = -1;
        _scoreBreakdown.Clear();
        _claimedExtendThresholds.Clear();
    }

    internal bool ClaimExtend(long threshold) => _claimedExtendThresholds.Add(threshold);

    internal long AwardScore(string category, long amount)
    {
        if (amount <= 0) return 0;
        var available = long.MaxValue - Score;
        var awarded = Math.Min(available, amount);
        Score += awarded;
        _scoreBreakdown.TryGetValue(category, out var current);
        _scoreBreakdown[category] = current > long.MaxValue - awarded ? long.MaxValue : current + awarded;
        return awarded;
    }
}

public sealed record RunResult(
    SimulationStatus Status,
    long Score,
    long Frames,
    bool Continued,
    int ContinuesUsed,
    int CreditsRemaining);

/// <summary>Deterministic values a replay header/checkpoint can persist without inspecting systems.</summary>
public sealed record RunMetadataSnapshot(
    RunConfiguration Configuration,
    long Frame,
    double Rank,
    int Gauge,
    SpecialGaugePhase SpecialPhase,
    int SpecialLevel);

[Flags]
public enum InputButtons : ushort
{
    None = 0,
    Fire = 1 << 0,
    Bomb = 1 << 1,
    Retry = 1 << 2,
    Pause = 1 << 3,
    Focus = 1 << 4,
    Special = 1 << 5,
    Continue = 1 << 6
}

/// <summary>Replay-safe input sample with signed, eight-bit movement axes.</summary>
public readonly record struct InputFrame
{
    public const int AxisMaximum = 127;

    public InputFrame(sbyte moveX, sbyte moveY, InputButtons buttons = InputButtons.None)
    {
        if (moveX < -AxisMaximum)
        {
            throw new ArgumentOutOfRangeException(nameof(moveX));
        }

        if (moveY < -AxisMaximum)
        {
            throw new ArgumentOutOfRangeException(nameof(moveY));
        }

        MoveX = moveX;
        MoveY = moveY;
        Buttons = buttons;
    }

    public sbyte MoveX { get; }
    public sbyte MoveY { get; }
    public InputButtons Buttons { get; }
    public float NormalizedMoveX => MoveX / (float)AxisMaximum;
    public float NormalizedMoveY => MoveY / (float)AxisMaximum;

    public bool IsPressed(InputButtons button) => (Buttons & button) == button;

    public static InputFrame Capture(IInputState input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var buttons = InputButtons.None;
        if (input.Fire) buttons |= InputButtons.Fire;
        if (input.Focus) buttons |= InputButtons.Focus;
        if (input.Special) buttons |= InputButtons.Special;
        if (input.Continue) buttons |= InputButtons.Continue;
        if (input.Bomb) buttons |= InputButtons.Bomb;
        if (input.Retry) buttons |= InputButtons.Retry;
        if (input.Pause) buttons |= InputButtons.Pause;
        return new InputFrame(QuantizeAxis(input.MoveX), QuantizeAxis(input.MoveY), buttons);
    }

    public static sbyte QuantizeAxis(float value)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Input axis must be finite.");
        }

        var clamped = Math.Clamp(value, -1, 1);
        return checked((sbyte)MathF.Round(clamped * AxisMaximum, MidpointRounding.AwayFromZero));
    }
}
