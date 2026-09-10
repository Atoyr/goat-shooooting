using System.Text.Json;

namespace GoatShooooting.Definitions;

/// <summary>Loads a game definition tree from JSON files on disk.</summary>
public sealed class JsonDefinitionRepository(string rootDirectory) : IDefinitionRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private readonly string _rootDirectory = Path.GetFullPath(rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory)));

    public DefinitionCatalog Load()
    {
        if (!Directory.Exists(_rootDirectory))
        {
            throw new DirectoryNotFoundException($"Definition directory '{_rootDirectory}' was not found.");
        }

        return new DefinitionCatalog(
            Read<GameDefinition>(Path.Combine(_rootDirectory, "game.json")),
            ReadAll<PlayerDefinition>(Path.Combine(_rootDirectory, "players"), allowSingleFile: Path.Combine(_rootDirectory, "player.json")),
            ReadAll<EnemyDefinition>(Path.Combine(_rootDirectory, "enemies")),
            ReadAll<BulletDefinition>(Path.Combine(_rootDirectory, "bullets")),
            ReadAll<WeaponDefinition>(Path.Combine(_rootDirectory, "weapons")),
            ReadAll<StageDefinition>(Path.Combine(_rootDirectory, "stages")));
    }

    private static IReadOnlyList<T> ReadAll<T>(string directory, string? allowSingleFile = null)
    {
        var result = new List<T>();
        if (allowSingleFile is not null && File.Exists(allowSingleFile))
        {
            result.Add(Read<T>(allowSingleFile));
        }

        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderBy(static path => path, StringComparer.Ordinal))
            {
                result.Add(Read<T>(file));
            }
        }

        return result;
    }

    private static T Read<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Required definition file '{path}' was not found.", path);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
                ?? throw new DefinitionValidationException($"Definition file '{path}' contains null.");
        }
        catch (JsonException exception)
        {
            throw new DefinitionValidationException($"Definition file '{path}' is invalid JSON: {exception.Message}");
        }
    }
}
