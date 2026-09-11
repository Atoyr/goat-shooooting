using GoatShooooting.Definitions;

namespace GoatShooooting.Tooling;

public sealed record EditorValidationResult(bool Success, string Message);

/// <summary>Safe filesystem boundary shared by the validation API and browser editor.</summary>
public sealed class DefinitionEditorService
{
    private readonly string _rootDirectory;

    public DefinitionEditorService(string rootDirectory)
    {
        _rootDirectory = Path.GetFullPath(rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory)));
        if (!Directory.Exists(_rootDirectory))
        {
            throw new DirectoryNotFoundException($"Definition directory '{_rootDirectory}' was not found.");
        }
    }

    public IReadOnlyList<string> ListFiles() => Directory
        .EnumerateFiles(_rootDirectory, "*.json", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(_rootDirectory, path).Replace('\\', '/'))
        .OrderBy(static path => path, StringComparer.Ordinal)
        .ToArray();

    public string Read(string relativePath) => File.ReadAllText(ResolvePath(relativePath));

    public string GetSchemaName(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);
        if (string.Equals(fileName, "game.json", StringComparison.OrdinalIgnoreCase)) return "game";
        if (string.Equals(fileName, "player.json", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("players/", StringComparison.OrdinalIgnoreCase)) return "player";
        if (normalized.StartsWith("enemies/", StringComparison.OrdinalIgnoreCase)) return "enemy";
        if (normalized.StartsWith("bullets/", StringComparison.OrdinalIgnoreCase)) return "bullet";
        if (normalized.StartsWith("weapons/", StringComparison.OrdinalIgnoreCase)) return "weapon";
        if (normalized.StartsWith("stages/", StringComparison.OrdinalIgnoreCase)) return "stage";
        throw new ArgumentException($"Cannot select a schema for '{relativePath}'.", nameof(relativePath));
    }

    public EditorValidationResult Validate(string relativePath, string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var targetPath = ResolvePath(relativePath);
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"goat-shooooting-editor-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(temporaryRoot);
            foreach (var sourcePath in Directory.EnumerateFiles(_rootDirectory, "*.json", SearchOption.AllDirectories))
            {
                var relativeSource = Path.GetRelativePath(_rootDirectory, sourcePath);
                var destinationPath = Path.Combine(temporaryRoot, relativeSource);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath);
            }

            var relativeTarget = Path.GetRelativePath(_rootDirectory, targetPath);
            var temporaryTarget = Path.Combine(temporaryRoot, relativeTarget);
            Directory.CreateDirectory(Path.GetDirectoryName(temporaryTarget)!);
            File.WriteAllText(temporaryTarget, content);
            var catalog = new JsonDefinitionRepository(temporaryRoot).Load();
            return new EditorValidationResult(
                true,
                $"Valid: {catalog.Enemies.Count} enemies, {catalog.Weapons.Count} weapons, {catalog.Stages.Count} stages.");
        }
        catch (Exception exception) when (exception is DefinitionValidationException or IOException or UnauthorizedAccessException)
        {
            return new EditorValidationResult(false, exception.Message.Replace(temporaryRoot, "<preview>", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, recursive: true);
            }
        }
    }

    public EditorValidationResult Save(string relativePath, string content)
    {
        var validation = Validate(relativePath, content);
        if (!validation.Success)
        {
            return validation;
        }

        var targetPath = ResolvePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var temporaryPath = $"{targetPath}.tmp-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(temporaryPath, content);
            File.Move(temporaryPath, targetPath, overwrite: true);
            return new EditorValidationResult(true, $"Saved {relativePath}.");
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private string ResolvePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("A relative JSON path is required.", nameof(relativePath));
        }

        var fullPath = Path.GetFullPath(Path.Combine(_rootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = _rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Path '{relativePath}' is outside the definition directory or is not JSON.", nameof(relativePath));
        }

        return fullPath;
    }
}
