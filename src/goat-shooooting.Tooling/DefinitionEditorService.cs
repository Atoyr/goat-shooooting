using System.Text.Json;
using System.Text.Json.Nodes;
using GoatShooooting.Definitions;
using GoatShooooting.Framework;
using GoatShooooting.Runtime;

namespace GoatShooooting.Tooling;

public sealed record EditorValidationResult(bool Success, string Message);

public sealed record EditorPreviewRequest(
    string Path,
    string Content,
    int TargetFrame = 0,
    long Seed = 0,
    string? DifficultyId = null);

public sealed record EditorPreviewItem(
    int EntityId,
    string Kind,
    float X,
    float Y,
    float Radius,
    string? VisualId);

public sealed record EditorScoreTrace(
    long Frame,
    string Reason,
    long BaseAmount,
    double Multiplier,
    long FinalAmount,
    string Category);

public sealed record EditorPreviewResult(
    bool Success,
    string Message,
    long Frame,
    int ActiveProjectiles,
    int CumulativeProjectiles,
    long TheoreticalSpawnBudget,
    long Score,
    IReadOnlyDictionary<string, long> ScoreBreakdown,
    IReadOnlyList<EditorScoreTrace> ScoreTrace,
    IReadOnlyList<EditorPreviewItem> Items,
    ulong StateHash);

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
        if (string.Equals(fileName, "assets.json", StringComparison.OrdinalIgnoreCase)) return "assets";
        if (string.Equals(fileName, "player.json", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("players/", StringComparison.OrdinalIgnoreCase)) return "player";
        if (normalized.StartsWith("enemies/", StringComparison.OrdinalIgnoreCase)) return "enemy";
        if (normalized.StartsWith("bullets/", StringComparison.OrdinalIgnoreCase)) return "bullet";
        if (normalized.StartsWith("weapons/", StringComparison.OrdinalIgnoreCase)) return "weapon";
        if (normalized.StartsWith("stages/", StringComparison.OrdinalIgnoreCase)) return "stage";
        if (normalized.StartsWith("ships/", StringComparison.OrdinalIgnoreCase)) return "ship";
        if (normalized.StartsWith("projectiles/", StringComparison.OrdinalIgnoreCase)) return "projectile";
        if (normalized.StartsWith("items/", StringComparison.OrdinalIgnoreCase)) return "item";
        if (normalized.StartsWith("patterns/", StringComparison.OrdinalIgnoreCase)) return "pattern";
        if (normalized.StartsWith("bosses/", StringComparison.OrdinalIgnoreCase)) return "boss";
        if (normalized.StartsWith("rulesets/", StringComparison.OrdinalIgnoreCase)) return "ruleset";
        if (normalized.StartsWith("difficulties/", StringComparison.OrdinalIgnoreCase)) return "difficulty";
        if (normalized.StartsWith("visuals/", StringComparison.OrdinalIgnoreCase)) return "visual";
        if (normalized.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return "audio";
        throw new ArgumentException($"Cannot select a schema for '{relativePath}'.", nameof(relativePath));
    }

    public string GetImageAssetPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("A relative PNG path is required.", nameof(relativePath));
        }

        var fullPath = Path.GetFullPath(Path.Combine(_rootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = _rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(fullPath), ".png", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(fullPath))
        {
            throw new ArgumentException($"Image path '{relativePath}' is outside the definition directory or unavailable.", nameof(relativePath));
        }

        return fullPath;
    }

    public EditorValidationResult Validate(string relativePath, string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        try
        {
            using var candidate = CreateCandidate(relativePath, content);
            var catalog = LoadAndValidate(candidate.Path);
            return new EditorValidationResult(
                true,
                $"Valid: {catalog.Enemies.Count} enemies, {catalog.Weapons.Count} weapons, " +
                $"{catalog.Patterns.Count} patterns, {catalog.Bosses.Count} bosses, {catalog.Stages.Count} stages.");
        }
        catch (Exception exception) when (IsContentException(exception))
        {
            return new EditorValidationResult(false, SanitizeTemporaryPath(exception.Message));
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

    public EditorValidationResult Duplicate(string sourcePath, string targetPath, string newId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newId);
        var source = JsonNode.Parse(Read(sourcePath))?.AsObject()
            ?? throw new InvalidDataException($"Definition '{sourcePath}' must contain a JSON object.");
        source["id"] = newId;
        return Save(targetPath, source.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public EditorValidationResult Delete(string relativePath)
    {
        var targetPath = ResolvePath(relativePath);
        if (!File.Exists(targetPath))
        {
            return new EditorValidationResult(false, $"Definition '{relativePath}' was not found.");
        }

        try
        {
            using var candidate = CreateCandidate(relativePath, content: null, omitTarget: true);
            _ = LoadAndValidate(candidate.Path);
            File.Delete(targetPath);
            return new EditorValidationResult(true, $"Deleted {relativePath}.");
        }
        catch (Exception exception) when (IsContentException(exception))
        {
            return new EditorValidationResult(false, SanitizeTemporaryPath(exception.Message));
        }
    }

    public EditorPreviewResult Preview(EditorPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TargetFrame is < 0 or > 36_000)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Target frame must be between 0 and 36000.");
        }

        try
        {
            using var candidate = CreateCandidate(request.Path, request.Content);
            var catalog = LoadAndValidate(candidate.Path);
            var gameId = string.IsNullOrWhiteSpace(catalog.Game.Id) ? "editor-preview" : catalog.Game.Id;
            var configuration = new RunConfiguration(
                gameId,
                request.Seed,
                difficultyId: request.DifficultyId,
                isPractice: true,
                initialInvincibilitySeconds: 600);
            var simulation = new ShootingSimulation(
                new MemoryDefinitionRepository(catalog),
                new MutableInputState(),
                configuration);
            var scoreTrace = new List<EditorScoreTrace>();
            for (var frame = 0; frame < request.TargetFrame; frame++)
            {
                var moveX = (sbyte)(((frame / 120) & 1) == 0 ? 48 : -48);
                simulation.Tick(new InputFrame(moveX, 0, InputButtons.Fire));
                foreach (var awarded in simulation.Events.Events.OfType<ScoreAwardedEvent>())
                {
                    if (scoreTrace.Count >= 4_096) break;
                    scoreTrace.Add(new EditorScoreTrace(
                        awarded.Frame,
                        awarded.Reason,
                        awarded.BaseAmount,
                        awarded.Multiplier,
                        awarded.FinalAmount,
                        awarded.Category));
                }
            }

            var snapshot = simulation.CaptureFrame(new RenderSystem());
            var items = snapshot.Items.Take(20_000).Select(static item => new EditorPreviewItem(
                item.EntityId,
                item.Kind.ToString(),
                item.Position.X,
                item.Position.Y,
                item.Radius,
                item.VisualId)).ToArray();
            return new EditorPreviewResult(
                true,
                "Preview generated by the production Runtime.",
                snapshot.Frame,
                simulation.Projectiles.ActiveCount,
                simulation.Telemetry.BulletsSpawned,
                CalculateTheoreticalSpawnBudget(catalog),
                snapshot.Score,
                new Dictionary<string, long>(simulation.RunState.ScoreBreakdown, StringComparer.Ordinal),
                scoreTrace,
                items,
                simulation.ComputeCanonicalStateHash());
        }
        catch (Exception exception) when (IsContentException(exception) || exception is InvalidOperationException)
        {
            return new EditorPreviewResult(
                false,
                SanitizeTemporaryPath(exception.Message),
                0,
                0,
                0,
                0,
                0,
                new Dictionary<string, long>(),
                Array.Empty<EditorScoreTrace>(),
                Array.Empty<EditorPreviewItem>(),
                0);
        }
    }

    public HeadlessBenchmarkReport Benchmark(string relativePath, string content)
    {
        using var candidate = CreateCandidate(relativePath, content);
        var catalog = LoadAndValidate(candidate.Path);
        return HeadlessBenchmarkRunner.Run(catalog);
    }

    private DefinitionCatalog LoadAndValidate(string rootDirectory)
    {
        var catalog = new JsonDefinitionRepository(rootDirectory).Load();
        new CapabilityValidator().Validate(catalog, RuntimeCapabilityRegistry.CreateBuiltIn());
        _ = VisualAssetManifestLoader.LoadOptional(rootDirectory, catalog);
        _ = AudioAssetResolver.Resolve(rootDirectory, catalog);
        if (Directory.Exists(Path.Combine(rootDirectory, "strings")))
        {
            _ = JsonStringCatalogLoader.Load(rootDirectory, "en");
            var japanese = JsonStringCatalogLoader.Load(rootDirectory, "ja");
            if (japanese.MissingKeys.Count > 0)
            {
                throw new InvalidDataException(
                    $"Japanese string catalog is missing: {string.Join(", ", japanese.MissingKeys)}.");
            }
        }

        return catalog;
    }

    private CandidateDirectory CreateCandidate(string relativePath, string? content, bool omitTarget = false)
    {
        var targetPath = ResolvePath(relativePath);
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"goat-shooooting-editor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            foreach (var sourcePath in Directory.EnumerateFiles(_rootDirectory, "*", SearchOption.AllDirectories))
            {
                if (omitTarget && string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relativeSource = Path.GetRelativePath(_rootDirectory, sourcePath);
                var destinationPath = Path.Combine(temporaryRoot, relativeSource);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath);
            }

            if (!omitTarget)
            {
                var relativeTarget = Path.GetRelativePath(_rootDirectory, targetPath);
                var temporaryTarget = Path.Combine(temporaryRoot, relativeTarget);
                Directory.CreateDirectory(Path.GetDirectoryName(temporaryTarget)!);
                File.WriteAllText(temporaryTarget, content ?? throw new ArgumentNullException(nameof(content)));
            }

            return new CandidateDirectory(temporaryRoot);
        }
        catch
        {
            Directory.Delete(temporaryRoot, recursive: true);
            throw;
        }
    }

    private static long CalculateTheoreticalSpawnBudget(DefinitionCatalog catalog)
    {
        var totals = new Dictionary<string, long>(StringComparer.Ordinal);
        long Calculate(PatternDefinition pattern)
        {
            if (totals.TryGetValue(pattern.Id, out var cached)) return cached;
            var total = pattern.Commands.Aggregate(0L, (current, command) =>
            {
                var nested = command.PatternId is null ? 0 : Calculate(catalog.GetPattern(command.PatternId));
                return checked(current + (command.RepeatCount * (command.MaximumSpawnCount + nested)));
            });
            totals.Add(pattern.Id, total);
            return total;
        }

        return catalog.Patterns.Values.Select(Calculate).DefaultIfEmpty(0).Max();
    }

    private static bool IsContentException(Exception exception) => exception is
        DefinitionValidationException or
        JsonException or
        InvalidDataException or
        IOException or
        UnauthorizedAccessException or
        ArgumentException;

    private static string SanitizeTemporaryPath(string message)
    {
        var marker = "goat-shooooting-editor-";
        var markerIndex = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0) return message;
        var prefixStart = message.LastIndexOfAny(new[] { '\'', '"', ' ' }, markerIndex);
        var suffixEnd = message.IndexOfAny(new[] { '\'', '"', ' ' }, markerIndex);
        return suffixEnd > markerIndex
            ? string.Concat(message.AsSpan(0, Math.Max(0, prefixStart + 1)), "<preview>", message.AsSpan(suffixEnd))
            : message[..Math.Max(0, prefixStart + 1)] + "<preview>";
    }

    private sealed class CandidateDirectory(string path) : IDisposable
    {
        public string Path { get; } = path;

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
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
