using GoatShooooting.Framework;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Framework.Tests;

public sealed class RunCompletionTrackerTests
{
    [Fact]
    public void CompletionIsReportedOncePerRunAndResetsForRetry()
    {
        var tracker = new RunCompletionTracker();

        Assert.Null(tracker.Observe(SimulationStatus.Running, 10));
        Assert.Equal(new RunCompletion(1200, Cleared: false), tracker.Observe(SimulationStatus.GameOver, 1200));
        Assert.Null(tracker.Observe(SimulationStatus.GameOver, 1200));

        tracker.StartRun();
        Assert.Equal(new RunCompletion(2500, Cleared: true), tracker.Observe(SimulationStatus.StageClear, 2500));
        Assert.Null(tracker.Observe(SimulationStatus.StageClear, 2500));
    }
}
