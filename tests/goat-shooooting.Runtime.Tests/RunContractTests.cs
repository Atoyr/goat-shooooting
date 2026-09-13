using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class RunContractTests
{
    [Fact]
    public void RunConfigurationKeepsImmutableRunChoices()
    {
        var configuration = new RunConfiguration(
            "sample",
            42,
            "signature",
            "arcade",
            "type-a",
            "stage-02",
            "boss-phase-1");

        Assert.Equal("sample", configuration.GameId);
        Assert.Equal(42, configuration.Seed);
        Assert.Equal("signature", configuration.RuleSetId);
        Assert.Equal("arcade", configuration.DifficultyId);
        Assert.Equal("type-a", configuration.ShipId);
        Assert.Equal("stage-02", configuration.StartStageId);
        Assert.Equal("boss-phase-1", configuration.CheckpointId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RunConfigurationRejectsMissingGameId(string gameId)
    {
        Assert.Throws<ArgumentException>(() => new RunConfiguration(gameId, 0));
    }

    [Fact]
    public void RunConfigurationRejectsWhitespaceOptionalId()
    {
        Assert.Throws<ArgumentException>(() => new RunConfiguration("sample", 0, difficultyId: " "));
    }

    [Fact]
    public void InputFrameCaptureQuantizesAndClampsAxesAndButtons()
    {
        var input = new MutableInputState
        {
            MoveX = 0.5f,
            MoveY = -2,
            Fire = true,
            Focus = true,
            Special = true,
            Continue = true,
            Pause = true
        };

        var frame = InputFrame.Capture(input);

        Assert.Equal(64, frame.MoveX);
        Assert.Equal(-InputFrame.AxisMaximum, frame.MoveY);
        Assert.Equal(64f / InputFrame.AxisMaximum, frame.NormalizedMoveX);
        Assert.True(frame.IsPressed(InputButtons.Fire));
        Assert.True(frame.IsPressed(InputButtons.Pause));
        Assert.False(frame.IsPressed(InputButtons.Bomb));
        Assert.True(frame.IsPressed(InputButtons.Focus));
        Assert.True(frame.IsPressed(InputButtons.Special));
        Assert.True(frame.IsPressed(InputButtons.Continue));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void InputFrameRejectsNonFiniteAxes(float value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InputFrame.QuantizeAxis(value));
    }

    [Fact]
    public void InputFrameRejectsUnrepresentableNegativeAxis()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InputFrame(sbyte.MinValue, 0));
    }

    [Fact]
    public void SimulationTimingDefinesSixtyHertzTick()
    {
        Assert.Equal(60, SimulationTiming.TicksPerSecond);
        Assert.Equal(1f / 60f, SimulationTiming.TickDurationSeconds);
    }

    [Fact]
    public void NewRunStateStartsAtFrameZeroWithNoScore()
    {
        var state = new RunState();

        Assert.Equal(0, state.Frame);
        Assert.Equal(0, state.Score);
    }
}
