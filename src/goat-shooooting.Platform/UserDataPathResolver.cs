namespace GoatShooooting.Platform;

public static class UserDataPathResolver
{
    public const string ProductDirectoryName = "GoatShooooting";

    public static string GetDefaultDirectory()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException("The operating system did not provide a LocalApplicationData directory.");
        }

        return FromLocalApplicationData(localApplicationData);
    }

    public static string FromLocalApplicationData(string localApplicationData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationData);
        return Path.Combine(Path.GetFullPath(localApplicationData), ProductDirectoryName);
    }
}
