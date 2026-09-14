using System.Globalization;

namespace GoatShooooting.Definitions;

public sealed record ResolvedAudioAsset(
    AudioDefinition Definition,
    string? ResolvedPath,
    float? ToneFrequency,
    int ToneDurationMilliseconds);

/// <summary>Validates audio asset paths and the repository-owned procedural cue URI.</summary>
public static class AudioAssetResolver
{
    private const long MaximumAudioBytes = 32L * 1024 * 1024;

    public static IReadOnlyDictionary<string, ResolvedAudioAsset> Resolve(
        string gameDirectory,
        DefinitionCatalog definitions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        ArgumentNullException.ThrowIfNull(definitions);
        var root = Path.GetFullPath(gameDirectory);
        var result = new Dictionary<string, ResolvedAudioAsset>(StringComparer.Ordinal);
        foreach (var definition in definitions.Audio.Values)
        {
            var resolved = definition.AssetId.StartsWith("tone://", StringComparison.OrdinalIgnoreCase)
                ? ResolveTone(definition)
                : ResolveFile(root, definition);
            result.Add(definition.Id, resolved);
        }
        return result;
    }

    private static ResolvedAudioAsset ResolveTone(AudioDefinition definition)
    {
        var values = definition.AssetId[7..].Split('/');
        if (values.Length != 2 ||
            !float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var frequency) ||
            !int.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var duration) ||
            !float.IsFinite(frequency) || frequency is < 20 or > 20_000 || duration is < 10 or > 30_000)
            throw new DefinitionValidationException(
                $"Audio '{definition.Id}' tone URI must be tone://<20-20000 Hz>/<10-30000 ms>.");
        return new ResolvedAudioAsset(definition, null, frequency, duration);
    }

    private static ResolvedAudioAsset ResolveFile(string root, AudioDefinition definition)
    {
        if (Path.IsPathRooted(definition.AssetId))
            throw new DefinitionValidationException($"Audio '{definition.Id}' asset path must be relative.");
        var resolved = Path.GetFullPath(Path.Combine(root, definition.AssetId));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new DefinitionValidationException($"Audio '{definition.Id}' asset path escapes the game directory.");
        if (!File.Exists(resolved))
            throw new DefinitionValidationException($"Audio '{definition.Id}' asset '{definition.AssetId}' was not found.");
        if (new FileInfo(resolved).Length > MaximumAudioBytes)
            throw new DefinitionValidationException($"Audio '{definition.Id}' exceeds the 32 MiB file limit.");
        if (!string.Equals(Path.GetExtension(resolved), ".wav", StringComparison.OrdinalIgnoreCase))
            throw new DefinitionValidationException($"Audio '{definition.Id}' must use a .wav asset.");
        return new ResolvedAudioAsset(definition, resolved, null, 0);
    }
}
