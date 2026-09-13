using GoatShooooting.Runtime;

namespace GoatShooooting.Framework;

/// <summary>Converts presentation elapsed time into a bounded number of 60Hz simulation ticks.</summary>
public sealed class FixedTickAccumulator
{
    public const int DefaultMaximumCatchUpTicks = 8;
    private double _accumulatedSeconds;

    public FixedTickAccumulator(int maximumCatchUpTicks = DefaultMaximumCatchUpTicks)
    {
        if (maximumCatchUpTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCatchUpTicks));
        }

        MaximumCatchUpTicks = maximumCatchUpTicks;
    }

    public int MaximumCatchUpTicks { get; }
    public double RemainderSeconds => _accumulatedSeconds;
    public double DroppedSeconds { get; private set; }
    public float InterpolationAlpha => (float)Math.Clamp(
        _accumulatedSeconds / SimulationTiming.ExactTickDurationSeconds,
        0,
        1);

    public int Advance(
        double elapsedSeconds,
        Func<InputFrame> captureInput,
        Action<InputFrame> tick)
    {
        if (elapsedSeconds < 0 || !double.IsFinite(elapsedSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        }

        ArgumentNullException.ThrowIfNull(captureInput);
        ArgumentNullException.ThrowIfNull(tick);
        var maximumAccumulated = MaximumCatchUpTicks * SimulationTiming.ExactTickDurationSeconds;
        _accumulatedSeconds += elapsedSeconds;
        if (_accumulatedSeconds > maximumAccumulated)
        {
            DroppedSeconds += _accumulatedSeconds - maximumAccumulated;
            _accumulatedSeconds = maximumAccumulated;
        }

        var completed = 0;
        while (completed < MaximumCatchUpTicks &&
               _accumulatedSeconds + 1e-12 >= SimulationTiming.ExactTickDurationSeconds)
        {
            var input = captureInput();
            tick(input);
            _accumulatedSeconds -= SimulationTiming.ExactTickDurationSeconds;
            completed++;
        }

        if (_accumulatedSeconds < 1e-12)
        {
            _accumulatedSeconds = 0;
        }

        return completed;
    }

    public void Reset()
    {
        _accumulatedSeconds = 0;
        DroppedSeconds = 0;
    }
}
