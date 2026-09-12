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
}
