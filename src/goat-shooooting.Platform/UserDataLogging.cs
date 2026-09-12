using System.Globalization;

namespace GoatShooooting.Platform;

public enum UserDataLogLevel
{
    Information,
    Warning,
    Error
}

public interface IUserDataLogger
{
    void Log(UserDataLogLevel level, string message, Exception? exception = null);
}

public sealed class FileUserDataLogger : IUserDataLogger
{
    private readonly string _logPath;

    public FileUserDataLogger(string userDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataDirectory);
        _logPath = Path.Combine(Path.GetFullPath(userDataDirectory), "logs", "latest.log");
    }

    public void Log(UserDataLogLevel level, string message, Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            var detail = exception is null ? string.Empty : $" | {exception.GetType().Name}: {exception.Message}";
            var line = $"{DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)} [{level}] {message}{detail}{Environment.NewLine}";
            File.AppendAllText(_logPath, line);
        }
        catch (Exception logException) when (IsFileFailure(logException))
        {
            // Logging must never prevent startup or saving.
        }
    }

    private static bool IsFileFailure(Exception exception) => exception is
        IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException;
}
