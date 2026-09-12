using GoatShooooting.Platform;
using Xunit;

namespace GoatShooooting.Platform.Tests;

public sealed class UserDataInfrastructureTests
{
    [Fact]
    public void DefaultPathUsesLocalApplicationData()
    {
        var expected = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            UserDataPathResolver.ProductDirectoryName);

        Assert.Equal(expected, UserDataPathResolver.GetDefaultDirectory());
    }

    [Fact]
    public void FileLoggerWritesUnderLogsDirectory()
    {
        using var directory = new TemporaryDirectory();
        var logger = new FileUserDataLogger(directory.Path);

        logger.Log(UserDataLogLevel.Error, "save failed", new IOException("test"));

        var logPath = System.IO.Path.Combine(directory.Path, "logs", "latest.log");
        Assert.True(File.Exists(logPath));
        Assert.Contains("save failed", File.ReadAllText(logPath), StringComparison.Ordinal);
    }
}
