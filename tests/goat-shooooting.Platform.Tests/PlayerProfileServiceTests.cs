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
}
