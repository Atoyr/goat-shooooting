using GoatShooooting.Runtime;

namespace GoatShooooting.Framework;

public readonly record struct RunCompletion(long Score, bool Cleared);

public sealed class RunCompletionTracker
{
    private SimulationStatus _previousStatus = SimulationStatus.Running;
    private bool _recordedCurrentRun;

    public RunCompletion? Observe(SimulationStatus status, long score)
    {
        if (status == SimulationStatus.Running)
        {
            _recordedCurrentRun = false;
            _previousStatus = status;
            return null;
        }

        var transitionedFromRunning = _previousStatus == SimulationStatus.Running;
        _previousStatus = status;
        if (!transitionedFromRunning || _recordedCurrentRun)
        {
            return null;
        }

        _recordedCurrentRun = true;
        return new RunCompletion(Math.Max(0, score), status == SimulationStatus.StageClear);
    }

    public void StartRun()
    {
        _previousStatus = SimulationStatus.Running;
        _recordedCurrentRun = false;
    }
}
