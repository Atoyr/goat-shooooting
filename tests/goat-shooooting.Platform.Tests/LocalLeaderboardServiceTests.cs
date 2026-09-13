using System.Text;
using GoatShooooting.Platform;
using Xunit;

namespace GoatShooooting.Platform.Tests;

public sealed class LocalLeaderboardServiceTests
{
    private static readonly ScoreCategoryKey Arcade = new("sample", "normal", "arcade", "swift");

    [Fact]
    public void EntriesAreCategorySeparatedAndUseFirstAchievedTiePolicy()
    {
        using var directory = new TemporaryDirectory();
        var service = new LocalLeaderboardService(directory.Path, maximumEntriesPerCategory: 2);
        var later = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var earlier = later.AddDays(-1);

        Assert.Equal(LeaderboardSubmitStatus.Added, service.Submit(Entry("later", Arcade, 1000, later)));
        Assert.Equal(LeaderboardSubmitStatus.Added, service.Submit(Entry("earlier", Arcade, 1000, earlier)));
        Assert.Equal(LeaderboardSubmitStatus.BelowCutoff, service.Submit(Entry("low", Arcade, 900, earlier)));
        Assert.Equal(LeaderboardSubmitStatus.DuplicateRun, service.Submit(Entry("later", Arcade, 9999, earlier)));
        Assert.Equal(LeaderboardSubmitStatus.Added, service.Submit(Entry(
            "expert", Arcade with { DifficultyId = "expert" }, 500, earlier)));

        var entries = new LocalLeaderboardService(directory.Path, 2).GetEntries(Arcade);
        Assert.Equal(new[] { "earlier", "later" }, entries.Select(static entry => entry.RunId));
        Assert.Single(service.GetEntries(Arcade with { DifficultyId = "expert" }));
        Assert.Equal(10, entries[0].ScoreBreakdown["enemy"]);
        Assert.True(entries[0].Cleared);
        Assert.True(entries[0].Continued);
        Assert.Null(entries[0].ReplayPath);
    }

    [Fact]
    public void CorruptBoardIsQuarantinedAndRecoversWithAnAtomicSave()
    {
        using var directory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(directory.Path, "leaderboard.json");
        File.WriteAllText(path, "{ broken");
        var now = new DateTimeOffset(2026, 9, 13, 1, 2, 3, TimeSpan.Zero);
        var service = new LocalLeaderboardService(
            directory.Path,
            10,
            new AtomicFileWriter(),
            () => now);

        Assert.Empty(service.GetEntries(Arcade));
        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(directory.Path, "leaderboard.json.invalid-*"));
        Assert.Equal(LeaderboardSubmitStatus.Added, service.Submit(Entry("new", Arcade, 42, now)));
        Assert.Single(service.GetEntries(Arcade));
    }

    [Fact]
    public void InterruptedSaveLeavesPreviousLeaderboardUntouched()
    {
        using var directory = new TemporaryDirectory();
        var service = new LocalLeaderboardService(directory.Path);
        service.Submit(Entry("first", Arcade, 100, DateTimeOffset.UnixEpoch));
        var path = System.IO.Path.Combine(directory.Path, "leaderboard.json");
        var original = File.ReadAllText(path);
        var failing = new LocalLeaderboardService(
            directory.Path,
            10,
            new ThrowingWriter(),
            static () => DateTimeOffset.UtcNow);

        Assert.Equal(
            LeaderboardSubmitStatus.SaveFailed,
            failing.Submit(Entry("second", Arcade, 200, DateTimeOffset.UtcNow)));
        Assert.Equal(original, File.ReadAllText(path));
    }

    private static CompletedRunRecord Entry(
        string runId,
        ScoreCategoryKey category,
        long score,
        DateTimeOffset timestamp) => new()
        {
            RunId = runId,
            Category = category,
            Score = score,
            Cleared = true,
            Continued = true,
            BestStage = 3,
            MaximumChain = 20,
            Grazes = 12,
            Misses = 1,
            Bombs = 2,
            Continues = 1,
            PlayTimeFrames = 600,
            Timestamp = timestamp,
            ScoreBreakdown = new Dictionary<string, long> { ["enemy"] = 10 }
        };

    private sealed class ThrowingWriter : IAtomicFileWriter
    {
        public void Write(string targetPath, ReadOnlySpan<byte> content) =>
            throw new IOException(Encoding.UTF8.GetString(content));
    }
}
