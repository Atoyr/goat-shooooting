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
        string? checkpointId = null)
    {
        GameId = RequireId(gameId, nameof(gameId));
        Seed = seed;
        RuleSetId = OptionalId(ruleSetId, nameof(ruleSetId));
        DifficultyId = OptionalId(difficultyId, nameof(difficultyId));
        ShipId = OptionalId(shipId, nameof(shipId));
        StartStageId = OptionalId(startStageId, nameof(startStageId));
        CheckpointId = OptionalId(checkpointId, nameof(checkpointId));
    }

    public string GameId { get; }
    public long Seed { get; }
    public string? RuleSetId { get; }
    public string? DifficultyId { get; }
    public string? ShipId { get; }
    public string? StartStageId { get; }
    public string? CheckpointId { get; }

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
    public long Frame { get; internal set; }
    public long Score { get; internal set; }

    internal void Reset()
    {
        Frame = 0;
        Score = 0;
    }
}

[Flags]
public enum InputButtons : ushort
{
    None = 0,
    Fire = 1 << 0,
    Bomb = 1 << 1,
    Retry = 1 << 2,
    Pause = 1 << 3,
    Focus = 1 << 4,
    Special = 1 << 5
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
