namespace GoatShooooting.Platform;

internal interface IAtomicFileWriter
{
    void Write(string targetPath, ReadOnlySpan<byte> content);
}

internal sealed class AtomicFileWriter : IAtomicFileWriter
{
    public void Write(string targetPath, ReadOnlySpan<byte> content)
    {
        var directory = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"{Path.GetFileName(targetPath)}.tmp-{Guid.NewGuid():N}");

        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(targetPath))
            {
                File.Replace(temporaryPath, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, targetPath);
            }
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A stale temporary file is preferable to losing the previous valid save.
        }
    }
}
