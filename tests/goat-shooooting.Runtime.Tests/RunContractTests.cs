using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class RunContractTests
{
    [Theory]
    [InlineData("   ")]
    public void RunConfigurationRejectsMissingGameId(string gameId)
    {
        Assert.Throws<ArgumentException>(() => new RunConfiguration(gameId, 0));
    }

    [Fact]
    public void RunConfigurationRejectsInvalidOptionalValues()
    {
        Assert.Throws<ArgumentException>(() => new RunConfiguration("sample", 0, difficultyId: " "));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RunConfiguration("sample", 0, isPractice: true, initialLives: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RunConfiguration("sample", 0, isPractice: true, initialRank: double.NaN));
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
    public void InputFrameRejectsNonFiniteAxes(float value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InputFrame.QuantizeAxis(value));
    }

    [Fact]
    public void InputFrameRejectsUnrepresentableNegativeAxis()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InputFrame(sbyte.MinValue, 0));
    }

}
