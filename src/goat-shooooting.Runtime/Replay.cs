namespace GoatShooooting.Runtime;

public static class ReplayFormat
{
    public const int CurrentVersion = 1;
    public const string EngineVersion = "goat-shooooting/1";
    public const int DefaultHashInterval = 300;
    public const long MaximumFrames = 60L * 60 * 24;
    public const int MaximumInputRuns = 1_000_000;
    public const int MaximumCheckpoints = 100_000;
}

public sealed record ReplayHeader
{
    public int ReplayVersion { get; init; } = ReplayFormat.CurrentVersion;
    public string EngineVersion { get; init; } = ReplayFormat.EngineVersion;
    public string ContentHash { get; init; } = string.Empty;
    public string? CompiledContentHash { get; init; }
    public RunConfiguration Configuration { get; init; } = new("invalid", 0);
    public long Seed { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record ReplayInputRun
{
    public long StartFrame { get; init; }
    public int Length { get; init; }
    public InputFrame Input { get; init; }
}

public sealed record ReplayCheckpoint
{
    public long Frame { get; init; }
    public ulong StateHash { get; init; }
    public double Rank { get; init; }
    public int Gauge { get; init; }
}

public sealed record ReplayFinalResult
{
    public SimulationStatus Status { get; init; }
    public long Score { get; init; }
    public IReadOnlyDictionary<string, long> ScoreBreakdown { get; init; } =
        new Dictionary<string, long>(StringComparer.Ordinal);
    public bool Cleared { get; init; }
    public long EndFrame { get; init; }
    public ulong FinalStateHash { get; init; }
}

public sealed record ReplayDocument
{
    public ReplayHeader Header { get; init; } = new();
    public IReadOnlyList<ReplayInputRun> Inputs { get; init; } = Array.Empty<ReplayInputRun>();
    public IReadOnlyList<ReplayCheckpoint> Checkpoints { get; init; } = Array.Empty<ReplayCheckpoint>();
    public ReplayFinalResult Result { get; init; } = new();
}

public enum ReplayErrorCode
{
    UnsupportedVersion,
    EngineMismatch,
    ContentMismatch,
    InvalidStructure,
    TooLarge,
    InvalidInput,
    Truncated,
    Desync,
    ChecksumMismatch,
    InvalidPath,
    ReadFailed
}

public sealed class ReplayException : Exception
{
    public ReplayException(ReplayErrorCode code, string message) : base(message) => Code = code;
    public ReplayException(ReplayErrorCode code, string message, Exception innerException)
        : base(message, innerException) => Code = code;

    public ReplayErrorCode Code { get; }
}

public sealed class ReplayRecorder
{
    private readonly ReplayHeader _header;
    private readonly int _hashInterval;
    private readonly List<ReplayInputRun> _inputs = new();
    private readonly List<ReplayCheckpoint> _checkpoints = new();
    private long _recordedFrames;
    private bool _completed;

    public ReplayRecorder(
        RunConfiguration configuration,
        string contentHash,
        DateTimeOffset createdAt,
        int hashInterval = ReplayFormat.DefaultHashInterval,
        string? compiledContentHash = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        if (hashInterval <= 0) throw new ArgumentOutOfRangeException(nameof(hashInterval));
        _hashInterval = hashInterval;
        _header = new ReplayHeader
        {
            Configuration = configuration,
            Seed = configuration.Seed,
            ContentHash = contentHash,
            CompiledContentHash = compiledContentHash,
            CreatedAt = createdAt
        };
    }

    public void Record(InputFrame input, ShootingSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (_completed) throw new InvalidOperationException("The replay is already complete.");
        if (_recordedFrames >= ReplayFormat.MaximumFrames)
            throw new ReplayException(ReplayErrorCode.TooLarge, "Replay exceeds the maximum frame count.");
        if (simulation.RunState.Frame != _recordedFrames + 1)
            throw new InvalidOperationException(
                $"Replay expected completed frame {_recordedFrames + 1}, got {simulation.RunState.Frame}.");
        if (_inputs.Count > 0 && _inputs[^1].Input == input)
        {
            var previous = _inputs[^1];
            _inputs[^1] = previous with { Length = checked(previous.Length + 1) };
        }
        else
        {
            if (_inputs.Count >= ReplayFormat.MaximumInputRuns)
                throw new ReplayException(ReplayErrorCode.TooLarge, "Replay exceeds the maximum input change count.");
            _inputs.Add(new ReplayInputRun { StartFrame = _recordedFrames, Length = 1, Input = input });
        }
        _recordedFrames++;
        if (_recordedFrames % _hashInterval == 0 || simulation.Status != SimulationStatus.Running)
        {
            _checkpoints.Add(new ReplayCheckpoint
            {
                Frame = _recordedFrames,
                StateHash = simulation.ComputeCanonicalStateHash(),
                Rank = simulation.RunState.Rank,
                Gauge = simulation.RunState.Gauge
            });
        }
    }

    public ReplayDocument Complete(ShootingSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (_completed) throw new InvalidOperationException("The replay is already complete.");
        if (simulation.Status == SimulationStatus.Running)
            throw new InvalidOperationException("A running simulation cannot produce a final replay result.");
        if (simulation.RunState.Frame != _recordedFrames)
            throw new InvalidOperationException("Replay input count does not match the final simulation frame.");
        _completed = true;
        var finalHash = simulation.ComputeCanonicalStateHash();
        if (_checkpoints.Count == 0 || _checkpoints[^1].Frame != _recordedFrames)
        {
            _checkpoints.Add(new ReplayCheckpoint
            {
                Frame = _recordedFrames,
                StateHash = finalHash,
                Rank = simulation.RunState.Rank,
                Gauge = simulation.RunState.Gauge
            });
        }
        return new ReplayDocument
        {
            Header = _header,
            Inputs = _inputs.ToArray(),
            Checkpoints = _checkpoints.ToArray(),
            Result = new ReplayFinalResult
            {
                Status = simulation.Status,
                Score = simulation.RunState.Score,
                ScoreBreakdown = new Dictionary<string, long>(simulation.RunState.ScoreBreakdown, StringComparer.Ordinal),
                Cleared = simulation.Status == SimulationStatus.StageClear,
                EndFrame = simulation.RunState.Frame,
                FinalStateHash = finalHash
            }
        };
    }
}

public sealed class ReplayInputProvider
{
    private readonly ReplayDocument _replay;
    private int _runIndex;

    public ReplayInputProvider(ReplayDocument replay) =>
        _replay = replay ?? throw new ArgumentNullException(nameof(replay));

    public bool TryGet(long frame, out InputFrame input)
    {
        while (_runIndex < _replay.Inputs.Count &&
            frame >= _replay.Inputs[_runIndex].StartFrame + _replay.Inputs[_runIndex].Length)
            _runIndex++;
        if (_runIndex >= _replay.Inputs.Count || frame < _replay.Inputs[_runIndex].StartFrame)
        {
            input = default;
            return false;
        }
        input = _replay.Inputs[_runIndex].Input;
        return true;
    }

    public void Reset() => _runIndex = 0;
}

public sealed class ReplayPlaybackSession
{
    private readonly ReplayDocument _replay;
    private readonly ReplayInputProvider _inputs;
    private int _checkpointIndex;

    public ReplayPlaybackSession(ReplayDocument replay)
    {
        _replay = replay ?? throw new ArgumentNullException(nameof(replay));
        _inputs = new ReplayInputProvider(replay);
    }

    public bool IsComplete { get; private set; }

    public void Step(ShootingSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (IsComplete) return;
        var frame = simulation.RunState.Frame;
        if (!_inputs.TryGet(frame, out var input))
            throw new ReplayException(ReplayErrorCode.Truncated, $"Replay ended before input frame {frame}.");
        simulation.Tick(input);
        if (simulation.Status != SimulationStatus.Running &&
            simulation.RunState.Frame < _replay.Result.EndFrame)
            throw new ReplayException(
                ReplayErrorCode.Desync,
                $"Replay run ended early at frame {simulation.RunState.Frame}.");
        while (_checkpointIndex < _replay.Checkpoints.Count &&
            _replay.Checkpoints[_checkpointIndex].Frame == simulation.RunState.Frame)
        {
            var expected = _replay.Checkpoints[_checkpointIndex++];
            var actual = simulation.ComputeCanonicalStateHash();
            if (actual != expected.StateHash)
                throw new ReplayException(
                    ReplayErrorCode.Desync,
                    $"Replay desynchronized at frame {expected.Frame}: expected {expected.StateHash:X16}, got {actual:X16}.");
        }
        if (simulation.RunState.Frame < _replay.Result.EndFrame) return;
        var finalHash = simulation.ComputeCanonicalStateHash();
        if (simulation.Status == SimulationStatus.Running)
            throw new ReplayException(ReplayErrorCode.Truncated, "Replay reached its end while the run was still active.");
        if (finalHash != _replay.Result.FinalStateHash || simulation.RunState.Score != _replay.Result.Score ||
            simulation.Status != _replay.Result.Status ||
            !simulation.RunState.ScoreBreakdown.OrderBy(static item => item.Key, StringComparer.Ordinal)
                .SequenceEqual(_replay.Result.ScoreBreakdown.OrderBy(static item => item.Key, StringComparer.Ordinal)))
            throw new ReplayException(ReplayErrorCode.Desync, "Replay final result does not match the recorded run.");
        IsComplete = true;
    }
}

public sealed class ReplayPlaybackController
{
    private static readonly double[] Speeds = [0.25, 0.5, 1, 2, 4];
    private int _speedIndex = 2;

    public double Speed => Speeds[_speedIndex];
    public bool ShowHitboxes { get; private set; }
    public bool IsPaused { get; private set; }
    public void Faster() => _speedIndex = Math.Min(Speeds.Length - 1, _speedIndex + 1);
    public void Slower() => _speedIndex = Math.Max(0, _speedIndex - 1);
    public void TogglePause() => IsPaused = !IsPaused;
    public void ToggleHitboxes() => ShowHitboxes = !ShowHitboxes;
}

public static class ReplayValidator
{
    private const InputButtons KnownButtons = InputButtons.Fire | InputButtons.Bomb | InputButtons.Retry |
        InputButtons.Pause | InputButtons.Focus | InputButtons.Special | InputButtons.Continue;

    public static void Validate(
        ReplayDocument replay,
        string expectedContentHash,
        string expectedEngineVersion = ReplayFormat.EngineVersion,
        string? expectedCompiledContentHash = null)
    {
        ArgumentNullException.ThrowIfNull(replay);
        if (replay.Header is null || replay.Header.Configuration is null || replay.Inputs is null ||
            replay.Checkpoints is null || replay.Result is null)
            throw new ReplayException(ReplayErrorCode.InvalidStructure, "Replay is missing required data.");
        if (replay.Header.ReplayVersion != ReplayFormat.CurrentVersion)
            throw new ReplayException(ReplayErrorCode.UnsupportedVersion,
                $"Replay version {replay.Header.ReplayVersion} is not supported.");
        if (!string.Equals(replay.Header.EngineVersion, expectedEngineVersion, StringComparison.Ordinal))
            throw new ReplayException(ReplayErrorCode.EngineMismatch,
                $"Replay engine '{replay.Header.EngineVersion}' does not match '{expectedEngineVersion}'.");
        if (!string.Equals(replay.Header.ContentHash, expectedContentHash, StringComparison.Ordinal))
            throw new ReplayException(ReplayErrorCode.ContentMismatch, "Replay content does not match the installed game data.");
        if (!string.IsNullOrWhiteSpace(replay.Header.CompiledContentHash) &&
            !string.Equals(replay.Header.CompiledContentHash, expectedCompiledContentHash, StringComparison.Ordinal))
            throw new ReplayException(
                ReplayErrorCode.ContentMismatch,
                "Replay compiled content does not match the installed compiler or module set.");
        if (replay.Header.Seed != replay.Header.Configuration.Seed)
            throw new ReplayException(ReplayErrorCode.InvalidStructure, "Replay seed does not match RunConfiguration.");
        if (replay.Result.EndFrame < 0 || replay.Result.EndFrame > ReplayFormat.MaximumFrames ||
            replay.Inputs.Count > ReplayFormat.MaximumInputRuns || replay.Checkpoints.Count > ReplayFormat.MaximumCheckpoints)
            throw new ReplayException(ReplayErrorCode.TooLarge, "Replay exceeds a configured size or frame limit.");
        long expectedStart = 0;
        foreach (var run in replay.Inputs)
        {
            if (run.StartFrame != expectedStart || run.Length <= 0 ||
                (run.Input.Buttons & ~KnownButtons) != 0)
                throw new ReplayException(ReplayErrorCode.InvalidInput,
                    $"Replay input run at frame {run.StartFrame} is invalid.");
            if (run.Length > ReplayFormat.MaximumFrames - expectedStart)
                throw new ReplayException(ReplayErrorCode.TooLarge, "Replay input duration exceeds the frame limit.");
            expectedStart += run.Length;
            if (expectedStart > ReplayFormat.MaximumFrames)
                throw new ReplayException(ReplayErrorCode.TooLarge, "Replay input duration exceeds the frame limit.");
        }
        if (expectedStart != replay.Result.EndFrame)
            throw new ReplayException(ReplayErrorCode.Truncated, "Replay input duration does not match endFrame.");
        long previousCheckpoint = -1;
        foreach (var checkpoint in replay.Checkpoints)
        {
            if (checkpoint.Frame <= previousCheckpoint || checkpoint.Frame <= 0 ||
                checkpoint.Frame > replay.Result.EndFrame || !double.IsFinite(checkpoint.Rank) || checkpoint.Gauge < 0)
                throw new ReplayException(ReplayErrorCode.InvalidStructure, "Replay checkpoints are invalid.");
            previousCheckpoint = checkpoint.Frame;
        }
        if (replay.Result.EndFrame > 0 &&
            (replay.Checkpoints.Count == 0 || replay.Checkpoints[^1].Frame != replay.Result.EndFrame ||
             replay.Checkpoints[^1].StateHash != replay.Result.FinalStateHash))
            throw new ReplayException(ReplayErrorCode.InvalidStructure, "Replay is missing its final checkpoint.");
        if (!Enum.IsDefined(replay.Result.Status) ||
            replay.Result.Cleared != (replay.Result.Status == SimulationStatus.StageClear) ||
            replay.Result.Score < 0 || replay.Result.ScoreBreakdown is null ||
            replay.Result.ScoreBreakdown.Any(static entry => string.IsNullOrWhiteSpace(entry.Key) || entry.Value < 0))
            throw new ReplayException(ReplayErrorCode.InvalidStructure, "Replay result is invalid.");
    }
}
