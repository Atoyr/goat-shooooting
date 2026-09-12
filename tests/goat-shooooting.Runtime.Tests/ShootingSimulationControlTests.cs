using GoatShooooting.Definitions;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class ShootingSimulationControlTests
{
    [Fact]
    public void SetPausedAndRestartSupportShellCommands()
    {
        var input = new MutableInputState();
        var simulation = new ShootingSimulation(
            new MemoryDefinitionRepository(TestDefinitions.Create()),
            input);

        simulation.SetPaused(true);
        simulation.Update(1);
        Assert.True(simulation.IsPaused);
        Assert.Equal(0, simulation.Elapsed);

        simulation.Restart();

        Assert.False(simulation.IsPaused);
        Assert.Equal(SimulationStatus.Running, simulation.Status);
        Assert.Equal(0, simulation.Elapsed);
    }
}
