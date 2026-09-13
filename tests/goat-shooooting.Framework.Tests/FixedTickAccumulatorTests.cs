using GoatShooooting.Framework;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Framework.Tests;

public sealed class FixedTickAccumulatorTests
{
    [Fact]
    public void PartialElapsedTimeIsRetainedForInterpolationAndNextUpdate()
    {
        var accumulator = new FixedTickAccumulator();
        var captures = 0;
        var ticks = 0;

        var first = accumulator.Advance(
            SimulationTiming.ExactTickDurationSeconds * 1.5,
            () =>
            {
                captures++;
                return default;
            },
            _ => ticks++);

        Assert.Equal(1, first);
        Assert.Equal(1, captures);
        Assert.Equal(1, ticks);
        Assert.Equal(0.5f, accumulator.InterpolationAlpha, 0.001f);

        var second = accumulator.Advance(
            SimulationTiming.ExactTickDurationSeconds * 0.5,
            () =>
            {
                captures++;
                return default;
            },
            _ => ticks++);

        Assert.Equal(1, second);
        Assert.Equal(2, captures);
        Assert.Equal(2, ticks);
        Assert.Equal(0, accumulator.RemainderSeconds);
    }

    [Fact]
    public void StallRunsAtMostConfiguredTicksAndDropsExcessTime()
    {
        var accumulator = new FixedTickAccumulator(maximumCatchUpTicks: 4);
        var ticks = 0;

        var completed = accumulator.Advance(1, () => default, _ => ticks++);

        Assert.Equal(4, completed);
        Assert.Equal(4, ticks);
        Assert.Equal(56d / 60d, accumulator.DroppedSeconds, 10);
        Assert.Equal(0, accumulator.RemainderSeconds);
    }

    [Fact]
    public void ResetClearsRemainderAndDroppedTime()
    {
        var accumulator = new FixedTickAccumulator(maximumCatchUpTicks: 1);
        accumulator.Advance(1, () => default, _ => { });

        accumulator.Reset();

        Assert.Equal(0, accumulator.RemainderSeconds);
        Assert.Equal(0, accumulator.DroppedSeconds);
        Assert.Equal(0, accumulator.InterpolationAlpha);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidElapsedTimeIsRejected(double elapsed)
    {
        var accumulator = new FixedTickAccumulator();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            accumulator.Advance(elapsed, () => default, _ => { }));
    }
}
