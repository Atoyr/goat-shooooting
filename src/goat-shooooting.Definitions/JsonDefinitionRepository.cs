using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoatShooooting.Definitions;

/// <summary>Loads a game definition tree from JSON files on disk.</summary>
public sealed class JsonDefinitionRepository(string rootDirectory) : IDefinitionRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
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
            ReadAll<StageDefinition>(Path.Combine(_rootDirectory, "stages")),
            ships: ReadAll<ShipDefinition>(Path.Combine(_rootDirectory, "ships"), requiredSchemaVersion: 2),
            projectiles: ReadAll<ProjectileDefinition>(Path.Combine(_rootDirectory, "projectiles"), requiredSchemaVersion: 2),
            items: ReadAll<ItemDefinition>(Path.Combine(_rootDirectory, "items"), requiredSchemaVersion: 2),
            patterns: ReadAll<PatternDefinition>(Path.Combine(_rootDirectory, "patterns"), requiredSchemaVersion: 2),
            bosses: ReadAll<BossDefinition>(Path.Combine(_rootDirectory, "bosses"), requiredSchemaVersion: 2),
            ruleSets: ReadAll<RuleSetDefinition>(Path.Combine(_rootDirectory, "rulesets"), requiredSchemaVersion: 2),
            difficulties: ReadAll<DifficultyDefinition>(Path.Combine(_rootDirectory, "difficulties"), requiredSchemaVersion: 2),
            visuals: ReadAll<VisualDefinition>(Path.Combine(_rootDirectory, "visuals"), requiredSchemaVersion: 2),
            audio: ReadAll<AudioDefinition>(Path.Combine(_rootDirectory, "audio"), requiredSchemaVersion: 2),
            programs: ReadAll<ProgramDefinition>(Path.Combine(_rootDirectory, "programs"), requiredSchemaVersion: 3),
            variants: ReadAll<VariantDefinition>(Path.Combine(_rootDirectory, "variants"), requiredSchemaVersion: 3),
            parameterSets: ReadAll<ParameterSetDefinition>(
                Path.Combine(_rootDirectory, "parameter-sets"), requiredSchemaVersion: 3),
            interactions: ReadAll<InteractionProfileDefinition>(
                Path.Combine(_rootDirectory, "interactions"), requiredSchemaVersion: 3),
            resources: ReadAll<ResourceDefinition>(
                Path.Combine(_rootDirectory, "resources"), requiredSchemaVersion: 3),
            eventRules: ReadAll<EventRuleDefinition>(
                Path.Combine(_rootDirectory, "rules"), requiredSchemaVersion: 3),
            stateMachines: ReadAll<StateMachineDefinition>(
                Path.Combine(_rootDirectory, "state-machines"), requiredSchemaVersion: 3),
            actors: ReadAll<ActorDefinition>(
                Path.Combine(_rootDirectory, "actors"), requiredSchemaVersion: 3),
            stagePrograms: ReadAll<StageProgramDefinition>(
                Path.Combine(_rootDirectory, "stage-programs"), requiredSchemaVersion: 3),
            effectRecipes: ReadAll<EffectRecipeDefinition>(
                Path.Combine(_rootDirectory, "effects"), requiredSchemaVersion: 3),
            animationStates: ReadAll<AnimationStateDefinition>(
                Path.Combine(_rootDirectory, "animation-states"), requiredSchemaVersion: 3));
    }

    private static IReadOnlyList<T> ReadAll<T>(
        string directory,
        string? allowSingleFile = null,
        int? requiredSchemaVersion = null)
    {
        var result = new List<T>();
        if (allowSingleFile is not null && File.Exists(allowSingleFile))
        {
            result.Add(Read<T>(allowSingleFile, requiredSchemaVersion));
        }

        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderBy(static path => path, StringComparer.Ordinal))
            {
                result.Add(Read<T>(file, requiredSchemaVersion));
            }
        }

        return result;
    }

    private static T Read<T>(string path, int? requiredSchemaVersion = null)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Required definition file '{path}' was not found.", path);
        }

        try
        {
            var content = File.ReadAllText(path);
            if (requiredSchemaVersion is not null)
            {
                using var document = JsonDocument.Parse(content);
                if (!document.RootElement.TryGetProperty("schemaVersion", out var schemaVersion) ||
                    schemaVersion.ValueKind != JsonValueKind.Number ||
                    !schemaVersion.TryGetInt32(out var value) || value != requiredSchemaVersion)
                {
                    throw new DefinitionValidationException(
                        $"Definition file '{path}' is invalid at JSON path '$.schemaVersion': expected {requiredSchemaVersion}.");
                }
            }

            return JsonSerializer.Deserialize<T>(content, JsonOptions)
                ?? throw new DefinitionValidationException($"Definition file '{path}' contains null.");
        }
        catch (JsonException exception)
        {
            var line = (exception.LineNumber ?? 0) + 1;
            var position = (exception.BytePositionInLine ?? 0) + 1;
            throw new DefinitionValidationException(
                $"Definition file '{path}' is invalid at JSON path '{exception.Path ?? "$"}', " +
                $"line {line}, byte {position}: {exception.Message}");
        }
    }
}
