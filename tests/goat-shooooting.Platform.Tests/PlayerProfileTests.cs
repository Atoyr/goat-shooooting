using GoatShooooting.Platform;
using Xunit;

namespace GoatShooooting.Platform.Tests;

public sealed class PlayerProfileTests
{
    [Fact]
    public void WithHighScoreKeepsOnlyTheHighestScore()
    {
        var profile = new PlayerProfile().WithHighScore("sample", 1000);

        profile = profile.WithHighScore("sample", 900);
        Assert.Equal(1000, profile.HighScores["sample"]);

        profile = profile.WithHighScore("sample", 1200);
        Assert.Equal(1200, profile.HighScores["sample"]);
    }

    [Fact]
    public void CategoryKeyIsStableAndDoesNotCollideWithEscapedSeparators()
    {
        var first = new ScoreCategoryKey("game|one", "mode", "hard", "ship");
        var second = new ScoreCategoryKey("game", "one|mode", "hard", "ship");

        Assert.NotEqual(first.StableId, second.StableId);
        Assert.Equal(first.StableId, first.Normalize().StableId);
    }
}
