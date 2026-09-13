using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Framework.Tests;

public sealed class JsonReplayStoreTests
{
    [Fact]
    public void SaveAndLoadRoundTripsValidatedReplayAtomically()
    {
        using var directory = new ReplayTemporaryDirectory();
        var store = new JsonReplayStore(directory.Path);

        var reference = store.Save("run-1", ValidReplay());
        var loaded = store.Load(reference, "content");

        Assert.True(loaded.Success, loaded.Error);
        Assert.Equal("run-1.replay.json", reference);
        Assert.Equal("game", loaded.Replay!.Header.Configuration.GameId);
        Assert.Equal(1, loaded.Replay.Result.EndFrame);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(directory.Path, "replays"), "*.tmp-*"));
    }

    [Fact]
    public void LoadRejectsTamperingPathTraversalAndContentMismatch()
    {
        using var directory = new ReplayTemporaryDirectory();
        var store = new JsonReplayStore(directory.Path);
        var reference = store.Save("run", ValidReplay());

        Assert.Equal(ReplayErrorCode.InvalidPath, store.Load("../run.replay.json", "content").ErrorCode);
        Assert.Equal(ReplayErrorCode.ContentMismatch, store.Load(reference, "other").ErrorCode);

        var path = Path.Combine(directory.Path, "replays", reference);
        var text = File.ReadAllText(path);
        File.WriteAllText(path, text.Replace("\"score\":0", "\"score\":1", StringComparison.Ordinal));
        Assert.Equal(ReplayErrorCode.ChecksumMismatch, store.Load(reference, "content").ErrorCode);
    }

    [Fact]
    public void LoadReturnsReadableErrorForCorruptJson()
    {
        using var directory = new ReplayTemporaryDirectory();
        var replayDirectory = Path.Combine(directory.Path, "replays");
        Directory.CreateDirectory(replayDirectory);
        File.WriteAllText(Path.Combine(replayDirectory, "bad.replay.json"), "{ truncated");

        var loaded = new JsonReplayStore(directory.Path).Load("bad.replay.json", "content");

        Assert.False(loaded.Success);
        Assert.Equal(ReplayErrorCode.ReadFailed, loaded.ErrorCode);
        Assert.Contains("could not be read", loaded.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static ReplayDocument ValidReplay() => new()
    {
        Header = new ReplayHeader
        {
            ContentHash = "content",
            Configuration = new RunConfiguration("game", 11, isPractice: false),
            Seed = 11,
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

    private sealed class ReplayTemporaryDirectory : IDisposable
    {
        public ReplayTemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"goat-shooooting-replay-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
