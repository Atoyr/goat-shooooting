using System.Security.Cryptography;
using System.Text;

namespace GoatShooooting.Definitions;

/// <summary>Polls a JSON tree by content fingerprint and reports safe reload attempts.</summary>
public sealed class ReloadableJsonDefinitionRepository : IReloadableDefinitionRepository
{
    private readonly string _rootDirectory;
    private readonly JsonDefinitionRepository _inner;
    private string? _observedFingerprint;

    public ReloadableJsonDefinitionRepository(string rootDirectory)
    {
        _rootDirectory = Path.GetFullPath(rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory)));
        _inner = new JsonDefinitionRepository(_rootDirectory);
    }

    public DefinitionCatalog Load()
    {
        var catalog = _inner.Load();
        _observedFingerprint = CreateFingerprint();
        return catalog;
    }

    public DefinitionReloadResult? PollChanges()
    {
        string fingerprint;
        try
        {
            fingerprint = CreateFingerprint();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new DefinitionReloadResult(null, $"Could not inspect definition changes: {exception.Message}");
        }

        if (string.Equals(fingerprint, _observedFingerprint, StringComparison.Ordinal))
        {
            return null;
        }

        _observedFingerprint = fingerprint;
        try
        {
            return new DefinitionReloadResult(_inner.Load(), null);
        }
        catch (Exception exception) when (exception is DefinitionValidationException or IOException or UnauthorizedAccessException)
        {
            return new DefinitionReloadResult(null, exception.Message);
        }
    }

    private string CreateFingerprint()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in Directory.EnumerateFiles(_rootDirectory, "*.json", SearchOption.AllDirectories)
                     .Where(static path => !string.Equals(
                         Path.GetFileName(path),
                         VisualAssetManifestLoader.FileName,
                         StringComparison.OrdinalIgnoreCase))
                     .OrderBy(static path => path, StringComparer.Ordinal))
        {
            var relativePath = Path.GetRelativePath(_rootDirectory, path);
            hash.AppendData(Encoding.UTF8.GetBytes(relativePath));
            hash.AppendData(File.ReadAllBytes(path));
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
