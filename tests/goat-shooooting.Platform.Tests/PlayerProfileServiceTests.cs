using GoatShooooting.Platform;
using Xunit;

namespace GoatShooooting.Platform.Tests;

public sealed class PlayerProfileServiceTests
{
    [Fact]
    public void CompletedRunsKeepPerGameHighScoresAndCountOnlyClears()
    {
        var service = new PlayerProfileService();
        var profile = service.RecordCompletedRun(new PlayerProfile(), "sample", 1000, cleared: false);
        profile = service.RecordCompletedRun(profile, "sample", 900, cleared: true);
        profile = service.RecordCompletedRun(profile, "gauntlet", 1500, cleared: true);

        Assert.Equal(1000, profile.HighScores["sample"]);
        Assert.Equal(1500, profile.HighScores["gauntlet"]);
        Assert.Equal(1, profile.ClearCounts["sample"]);
        Assert.Equal(1, profile.ClearCounts["gauntlet"]);
        Assert.Equal("gauntlet", profile.LastGameId);
    }

    [Fact]
    public void SelectGameOnlyChangesLastGameId()
    {
        var profile = new PlayerProfile { HighScores = new Dictionary<string, int> { ["sample"] = 42 } };

        var updated = new PlayerProfileService().SelectGame(profile, "gauntlet");

        Assert.Equal("gauntlet", updated.LastGameId);
        Assert.Equal(42, updated.HighScores["sample"]);
    }

    [Fact]
    public void DetailedRunsAreSeparatedByCategoryAndIgnoreDuplicateRunIds()
    {
        var service = new PlayerProfileService();
        var arcade = new ScoreCategoryKey("sample", "normal", "arcade", "swift");
        var expert = arcade with { DifficultyId = "expert" };
        var profile = service.RecordCompletedRun(new PlayerProfile(), Run("run-1", arcade, 1000, true, 2, 600));
        profile = service.RecordCompletedRun(profile, Run("run-2", arcade, 900, false, 3, 300));
        profile = service.RecordCompletedRun(profile, Run("run-3", expert, 1500, false, 1, 120));
        profile = service.RecordCompletedRun(profile, Run("run-3", expert, 9999, true, 9, 999));

        var arcadeStats = profile.GetStats(arcade)!;
        Assert.Equal(1000, arcadeStats.BestScore);
        Assert.Equal(1, arcadeStats.ClearCount);
        Assert.Equal(3, arcadeStats.BestStage);
        Assert.Equal(2, arcadeStats.PlayCount);
        Assert.Equal(900, arcadeStats.PlayTimeFrames);
        var expertStats = profile.GetStats(expert)!;
        Assert.Equal(1500, expertStats.BestScore);
        Assert.Equal(1, expertStats.PlayCount);
        Assert.Equal("normal", profile.LastRuleSetIds["sample"]);
        Assert.Equal("expert", profile.LastDifficultyIds["sample"]);
        Assert.Equal("swift", profile.LastShipIds["sample"]);
    }

    private static CompletedRunRecord Run(
        string id,
        ScoreCategoryKey category,
        long score,
        bool clear,
        int stage,
        long frames) => new()
        {
            RunId = id,
            Category = category,
            Score = score,
            Cleared = clear,
            BestStage = stage,
            PlayTimeFrames = frames
        };
}
