using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class ReplayTests
{
    [Fact]
    public void InputProviderReadsRunLengthEncodedFramesAndViewerControlsStayExternal()
    {
        var replay = ValidReplay() with
        {
            Inputs =
            [
                new ReplayInputRun { StartFrame = 0, Length = 2, Input = new InputFrame(1, 2, InputButtons.Fire) },
                new ReplayInputRun { StartFrame = 2, Length = 1, Input = new InputFrame(-1, 0, InputButtons.Focus) }
            ],
            Checkpoints = [new ReplayCheckpoint { Frame = 3, StateHash = 7 }],
            Result = ValidReplay().Result with { EndFrame = 3 }
        };
        var provider = new ReplayInputProvider(replay);
        var controller = new ReplayPlaybackController();

        Assert.True(provider.TryGet(0, out var first));
        Assert.Equal(first, provider.TryGet(1, out var repeated) ? repeated : default);
        Assert.True(provider.TryGet(2, out var final));
        Assert.NotEqual(first, final);
        Assert.False(provider.TryGet(3, out _));
        controller.Faster();
        controller.TogglePause();
        controller.ToggleHitboxes();
        Assert.Equal(2, controller.Speed);
        Assert.True(controller.IsPaused);
        Assert.True(controller.ShowHitboxes);
        Assert.Equal(0, replay.Header.Configuration.Seed);
    }

    [Theory]
    [InlineData("version", ReplayErrorCode.UnsupportedVersion)]
    [InlineData("engine", ReplayErrorCode.EngineMismatch)]
    [InlineData("content", ReplayErrorCode.ContentMismatch)]
    [InlineData("enum", ReplayErrorCode.InvalidInput)]
    [InlineData("truncated", ReplayErrorCode.Truncated)]
    [InlineData("overflow", ReplayErrorCode.TooLarge)]
    public void ValidatorRejectsUntrustedReplayStructuresWithSpecificErrors(
        string mutation,
        ReplayErrorCode expected)
    {
        var replay = ValidReplay();
        replay = mutation switch
        {
            "version" => replay with { Header = replay.Header with { ReplayVersion = 99 } },
            "engine" => replay with { Header = replay.Header with { EngineVersion = "other" } },
            "content" => replay,
            "enum" => replay with
            {
                Inputs = [replay.Inputs[0] with { Input = new InputFrame(0, 0, (InputButtons)0x8000) }]
            },
            "truncated" => replay with { Result = replay.Result with { EndFrame = 2 } },
            "overflow" => replay with
            {
                Inputs = [new ReplayInputRun { StartFrame = 0, Length = int.MaxValue }],
                Result = replay.Result with { EndFrame = ReplayFormat.MaximumFrames }
            },
            _ => throw new InvalidOperationException()
        };

        var exception = Assert.Throws<ReplayException>(() => ReplayValidator.Validate(
            replay,
            mutation == "content" ? "different" : "content"));

        Assert.Equal(expected, exception.Code);
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
    }

    [Fact]
    public void ValidatorPreservesLegacyReplayAndChecksCompiledIdentityWhenPresent()
    {
        ReplayValidator.Validate(ValidReplay(), "content");
        var compiled = ValidReplay() with
        {
            Header = ValidReplay().Header with { CompiledContentHash = "compiled" }
        };

        ReplayValidator.Validate(compiled, "content", expectedCompiledContentHash: "compiled");
        var exception = Assert.Throws<ReplayException>(() =>
            ReplayValidator.Validate(compiled, "content", expectedCompiledContentHash: "different"));

        Assert.Equal(ReplayErrorCode.ContentMismatch, exception.Code);
    }

    private static ReplayDocument ValidReplay() => new()
    {
        Header = new ReplayHeader
        {
            ContentHash = "content",
            Configuration = new RunConfiguration("game", 0),
            Seed = 0,
            CreatedAt = DateTimeOffset.UnixEpoch
        },
        Inputs = [new ReplayInputRun { StartFrame = 0, Length = 1 }],
        Checkpoints = [new ReplayCheckpoint { Frame = 1, StateHash = 7 }],
        Result = new ReplayFinalResult
        {
            Status = SimulationStatus.StageClear,
            Cleared = true,
            EndFrame = 1,
            FinalStateHash = 7
        }
    };
}
