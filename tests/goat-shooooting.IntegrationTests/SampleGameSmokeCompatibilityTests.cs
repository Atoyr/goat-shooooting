using Xunit;

namespace GoatShooooting.IntegrationTests;

public sealed class SampleGameSmokeCompatibilityTests
{
    [Theory]
    [InlineData("sample")]
    [InlineData("gauntlet")]
    [InlineData("ember-bloom")]
    public void ContentPackCompletesTheCommandLineSmokePath(string gameId)
    {
        var exitCode = GoatShooooting.SampleGame.Program.Main(["--game", gameId, "--smoke-test"]);

        Assert.Equal(0, exitCode);
    }
}
