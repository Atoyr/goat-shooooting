using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using GoatShooooting.Definitions;
using GoatShooooting.Framework;
using GoatShooooting.Runtime;

namespace GoatShooooting.Tooling;

public sealed record EditorValidationResult(bool Success, string Message)
{
    public IReadOnlyList<EditorDiagnostic> Diagnostics { get; init; } = Array.Empty<EditorDiagnostic>();
}

public sealed record EditorDiagnostic(
    string File,
    string JsonPath,
    string? NodeId,
    string Domain,
    string ErrorCode,
    IReadOnlyList<string> ReferenceChain,
    IReadOnlyDictionary<string, string> ResolvedParameters,
    IReadOnlyList<string> ModifierProvenance,
    long EstimatedInstructionBudget,
    long EstimatedSpawnBudget,
    string Message);

public sealed record EditorPreviewRequest(
    string Path,
    string Content,
    int TargetFrame = 0,
    long Seed = 0,
    string? DifficultyId = null,
    string? RuleSetId = null,
    string? VariantId = null,
    string? ShipId = null,
    string? StartStageId = null,
    string? CheckpointId = null,
    int? InitialPower = null,
    int? InitialLives = null,
    int? InitialBombs = null,
    int? InitialGauge = null,
    double? InitialRank = null,
    IReadOnlyDictionary<string, double>? InitialResources = null,
    IReadOnlyList<InputFrame>? RecordedInputs = null,
    IReadOnlyList<EditorInputScriptStep>? InputScript = null,
    IReadOnlyList<EditorEventInjection>? EventInjections = null,
    bool PlayerInvincible = true,
    float WorldTimeScale = 1,
    string PreviewKind = "stage",
    string? TargetDefinitionId = null);

public sealed record EditorInputScriptStep(
    int StartFrame,
    int EndFrame,
    sbyte MoveX,
    sbyte MoveY,
    InputButtons Buttons);

public sealed record EditorEventInjection(int Frame, string Type, string? Id = null);

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
    ulong StateHash)
{
    public long RestoredCheckpointFrame { get; init; }
    public int SimulatedFrames { get; init; }
    public IReadOnlyDictionary<string, double> LiveResources { get; init; } =
        new Dictionary<string, double>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, string> LiveStates { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyList<EditorProjectileTrace> ProjectileTrace { get; init; } = Array.Empty<EditorProjectileTrace>();
    public IReadOnlyList<EditorRuleTrace> RuleTrace { get; init; } = Array.Empty<EditorRuleTrace>();
    public IReadOnlyList<string> ModifierProvenance { get; init; } = Array.Empty<string>();
    public IReadOnlyList<EditorHeatmapCell> Heatmap { get; init; } = Array.Empty<EditorHeatmapCell>();
}

public sealed record EditorHeatmapCell(int X, int Y, int ProjectileCount);
public sealed record EditorProjectileTrace(
    int ProjectileId,
    string DefinitionId,
    string? ProgramId,
    string? SourceNodeId,
    int ProgramCounter,
    long WakeFrame,
    int SpawnLineageId);
public sealed record EditorRuleTrace(
    long Frame,
    string SourceDefinitionId,
    string NodeId,
    string Command,
    string? TargetId,
    string? ResourceId,
    double Value);

/// <summary>Safe filesystem boundary shared by the validation API and browser editor.</summary>
public sealed class DefinitionEditorService
{
    private const int PreviewCheckpointInterval = 300;
    private readonly string _rootDirectory;
    private readonly object _previewGate = new();
    private readonly Dictionary<string, PreviewSession> _previewSessions = new(StringComparer.Ordinal);

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
        if (normalized.StartsWith("stage-programs/", StringComparison.OrdinalIgnoreCase)) return "stage-program";
        if (normalized.StartsWith("effects/", StringComparison.OrdinalIgnoreCase)) return "effect";
        if (normalized.StartsWith("animation-states/", StringComparison.OrdinalIgnoreCase)) return "animation-state";
        if (normalized.StartsWith("programs/", StringComparison.OrdinalIgnoreCase)) return "program";
        if (normalized.StartsWith("variants/", StringComparison.OrdinalIgnoreCase)) return "variant";
        if (normalized.StartsWith("parameter-sets/", StringComparison.OrdinalIgnoreCase)) return "parameter-set";
        if (normalized.StartsWith("interactions/", StringComparison.OrdinalIgnoreCase)) return "interaction";
        if (normalized.StartsWith("resources/", StringComparison.OrdinalIgnoreCase)) return "resource";
        if (normalized.StartsWith("rules/", StringComparison.OrdinalIgnoreCase)) return "rule";
        if (normalized.StartsWith("state-machines/", StringComparison.OrdinalIgnoreCase)) return "state-machine";
        if (normalized.StartsWith("actors/", StringComparison.OrdinalIgnoreCase)) return "actor";
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
            var compiled = new DefinitionCompiler().Compile(catalog, RuntimeCapabilityRegistry.CreateBuiltIn());
            return new EditorValidationResult(
                true,
                $"Valid: {catalog.Enemies.Count} enemies, {catalog.Weapons.Count} weapons, " +
                $"{catalog.Patterns.Count} patterns, {catalog.Bosses.Count} bosses, {catalog.Stages.Count} stages, " +
                $"{catalog.StagePrograms.Count} stage programs, {catalog.EffectRecipes.Count} effect recipes.")
            {
                Diagnostics = compiled.Diagnostics.Entries.Select(static entry => new EditorDiagnostic(
                    entry.File,
                    entry.JsonPath,
                    entry.NodeId,
                    entry.Domain,
                    entry.ErrorCode,
                    entry.ReferenceChain,
                    entry.ResolvedParameters,
                    entry.ModifierProvenance.Select(static value =>
                        $"{value.StatKey}:{value.SourceDefinitionId}:{value.Before}->{value.After}").ToArray(),
                    entry.EstimatedInstructionBudget,
                    entry.EstimatedSpawnBudget,
                    $"Compiled {entry.DefinitionKind} '{entry.DefinitionId}'.")).ToArray()
            };
        }
        catch (Exception exception) when (IsContentException(exception))
        {
            var message = SanitizeTemporaryPath(exception.Message);
            string domain;
            try { domain = GetSchemaName(relativePath); }
            catch (ArgumentException) { domain = "definition"; }
            return new EditorValidationResult(false, message)
            {
                Diagnostics =
                [
                    new EditorDiagnostic(
                        relativePath.Replace('\\', '/'), "$", null, domain,
                        exception is JsonException ? "json.syntax" : "definition.validation",
                        Array.Empty<string>(), new Dictionary<string, string>(StringComparer.Ordinal),
                        Array.Empty<string>(), 0, 0, message)
                ]
            };
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

    public EditorValidationResult Rename(string relativePath, string newId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newId);
        var source = JsonNode.Parse(Read(relativePath))?.AsObject()
            ?? throw new InvalidDataException($"Definition '{relativePath}' must contain a JSON object.");
        var oldId = source["id"]?.GetValue<string>()
            ?? throw new InvalidDataException($"Definition '{relativePath}' has no ID.");
        if (oldId == newId) return new EditorValidationResult(true, $"ID is already '{newId}'.");
        var referenceFields = ReferenceFieldsFor(relativePath);
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"goat-shooooting-rename-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            foreach (var path in Directory.EnumerateFiles(_rootDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(_rootDirectory, path);
                var destination = Path.Combine(temporaryRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(path, destination);
            }
            foreach (var path in Directory.EnumerateFiles(temporaryRoot, "*.json", SearchOption.AllDirectories))
            {
                var document = JsonNode.Parse(File.ReadAllText(path));
                if (document is null) continue;
                ReplaceReferences(document, oldId, newId, referenceFields, fieldName: null);
                if (string.Equals(
                    Path.GetRelativePath(temporaryRoot, path).Replace('\\', '/'),
                    relativePath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
                    document["id"] = newId;
                File.WriteAllText(path, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            _ = LoadAndValidate(temporaryRoot);
            foreach (var path in Directory.EnumerateFiles(temporaryRoot, "*.json", SearchOption.AllDirectories))
            {
                var target = Path.Combine(_rootDirectory, Path.GetRelativePath(temporaryRoot, path));
                File.Copy(path, target, overwrite: true);
            }
            return new EditorValidationResult(true, $"Renamed '{oldId}' to '{newId}' and updated references.");
        }
        catch (Exception exception) when (IsContentException(exception))
        {
            return new EditorValidationResult(false, SanitizeTemporaryPath(exception.Message));
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static HashSet<string> ReferenceFieldsFor(string path)
    {
        var kind = path.Replace('\\', '/').Split('/')[0];
        return kind switch
        {
            "programs" => Fields("programId", "defaultProgramId"),
            "parameter-sets" => Fields("parameterSetId", "defaultParameterSetId"),
            "actors" => Fields("actorId"),
            "resources" => Fields("resourceId", "resourceIds", "bombResourceId"),
            "rules" => Fields("eventRuleId", "eventRuleIds"),
            "state-machines" => Fields("stateMachineId", "stateMachineIds"),
            "stage-programs" => Fields("stageProgramId"),
            "enemies" => Fields("enemyId"),
            "weapons" => Fields("weaponId", "weaponIds", "normalWeaponIds", "focusWeaponIds", "bombWeaponId", "specialWeaponId"),
            "bullets" or "projectiles" => Fields("bulletId", "projectileId", "convertProjectileId", "revengeProjectileId"),
            "patterns" => Fields("patternId", "patternIds", "motionPatternId", "attackPatternIds"),
            "bosses" => Fields("bossId"),
            "stages" => Fields("stageId", "stageIds", "startStageId", "nextStageId"),
            "ships" or "players" => Fields("shipId", "shipIds", "playerId"),
            "visuals" => Fields("visualId"),
            "audio" => Fields("audioId", "bgmAudioId"),
            "variants" => Fields("variantId", "defaultVariantId"),
            _ => new(StringComparer.Ordinal)
        };

        static HashSet<string> Fields(params string[] values) => new(values, StringComparer.Ordinal);
    }

    private static void ReplaceReferences(
        JsonNode node,
        string oldId,
        string newId,
        IReadOnlySet<string> referenceFields,
        string? fieldName)
    {
        if (node is JsonObject objectNode)
        {
            foreach (var pair in objectNode.ToArray())
            {
                if (pair.Value is null) continue;
                if (referenceFields.Contains(pair.Key) && pair.Value is JsonValue value &&
                    value.TryGetValue<string>(out var text) && text == oldId)
                    objectNode[pair.Key] = newId;
                else ReplaceReferences(pair.Value, oldId, newId, referenceFields, pair.Key);
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                if (fieldName is not null && referenceFields.Contains(fieldName) &&
                    array[index] is JsonValue value && value.TryGetValue<string>(out var text) && text == oldId)
                    array[index] = newId;
                else if (array[index] is { } item)
                    ReplaceReferences(item, oldId, newId, referenceFields, fieldName: null);
            }
        }
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
            ValidatePreviewRequest(request);
            lock (_previewGate)
            {
                var key = CreatePreviewKey(request);
                if (!_previewSessions.TryGetValue(key, out var session))
                {
                    session = CreatePreviewSession(catalog, request);
                    _previewSessions.Add(key, session);
                }
                var cached = session.Checkpoints.Last(pair => pair.Key <= request.TargetFrame).Value;
                session.Simulation.RestoreCheckpoint(cached.Checkpoint);
                var scoreTrace = new List<EditorScoreTrace>(cached.ScoreTrace);
                var ruleTrace = new List<EditorRuleTrace>(cached.RuleTrace);
                var simulatedFrames = 0;
                for (var frame = checked((int)cached.Checkpoint.Frame); frame < request.TargetFrame; frame++)
                {
                    foreach (var injection in request.EventInjections ?? Array.Empty<EditorEventInjection>())
                        if (injection.Frame == frame && injection.Type == "signal")
                            session.Simulation.InjectSignal(injection.Id!);
                    session.Simulation.Tick(ResolvePreviewInput(request, frame));
                    simulatedFrames++;
                    foreach (var awarded in session.Simulation.Events.Events.OfType<ScoreAwardedEvent>())
                    {
                        if (scoreTrace.Count >= 4_096) break;
                        scoreTrace.Add(new EditorScoreTrace(
                            awarded.Frame, awarded.Reason, awarded.BaseAmount, awarded.Multiplier,
                            awarded.FinalAmount, awarded.Category));
                    }
                    foreach (var applied in session.Simulation.Events.Events.OfType<RuleCommandAppliedEvent>())
                    {
                        if (ruleTrace.Count >= 4_096) break;
                        ruleTrace.Add(new EditorRuleTrace(
                            applied.Frame, applied.SourceDefinitionId, $"action-{applied.ActionIndex}",
                            applied.Command, applied.TargetId, applied.ResourceId, applied.Value));
                    }
                    if (session.Simulation.RunState.Frame % PreviewCheckpointInterval == 0)
                        session.Checkpoints[session.Simulation.RunState.Frame] = new CachedPreviewCheckpoint(
                            session.Simulation.CreateCheckpoint(), scoreTrace.ToArray(), ruleTrace.ToArray());
                }

                var simulation = session.Simulation;
                var snapshot = simulation.CaptureFrame(new RenderSystem());
                var items = snapshot.Items.Take(20_000).Select(static item => new EditorPreviewItem(
                    item.EntityId, item.Kind.ToString(), item.Position.X, item.Position.Y, item.Radius,
                    item.VisualId)).ToArray();
                var liveResources = simulation.Resources.Definitions.ToDictionary(
                    static definition => definition.Definition.Id,
                    definition => simulation.Resources.Get(definition.Handle),
                    StringComparer.Ordinal);
                var liveStates = simulation.StateMachines.CaptureCanonicalSnapshot().ToDictionary(
                    value => $"{simulation.CompiledDefinitions.Get(value.Machine).Definition.Id}@{value.ScopeKey}",
                    value => simulation.CompiledDefinitions.Get(value.Machine).States[value.State.Value].Definition.Id,
                    StringComparer.Ordinal);
                var projectileTrace = Enumerable.Range(0, simulation.Projectiles.ActiveCount)
                    .Select(simulation.Projectiles.GetSnapshot)
                    .Where(static value => value.ProgramHandle is not null || value.SourceNodeId is not null)
                    .Take(4_096)
                    .Select(value => new EditorProjectileTrace(
                        value.Id,
                        value.DefinitionId,
                        value.SourceProgramId ?? (value.ProgramHandle is { } handle
                            ? simulation.CompiledDefinitions.Get(handle).Definition.Id : null),
                        value.SourceNodeId,
                        value.ProgramCounter,
                        value.WakeFrame,
                        value.SpawnLineageId))
                    .ToArray();
                return new EditorPreviewResult(
                    true,
                    "Preview generated by the production Runtime checkpoint sandbox.",
                    snapshot.Frame,
                    simulation.Projectiles.ActiveCount,
                    simulation.Telemetry.BulletsSpawned,
                    CalculateTheoreticalSpawnBudget(catalog),
                    snapshot.Score,
                    new Dictionary<string, long>(simulation.RunState.ScoreBreakdown, StringComparer.Ordinal),
                    scoreTrace,
                    items,
                    simulation.ComputeCanonicalStateHash())
                {
                    RestoredCheckpointFrame = cached.Checkpoint.Frame,
                    SimulatedFrames = simulatedFrames,
                    LiveResources = liveResources,
                    LiveStates = liveStates,
                    ProjectileTrace = projectileTrace,
                    RuleTrace = ruleTrace,
                    ModifierProvenance = simulation.DebugSnapshot.ModifierProvenance.Select(static value =>
                        $"{value.StatKey} <- {value.SourceDefinitionId}" +
                        (value.SourceState is null ? string.Empty : $"/{value.SourceState}") +
                        $" [{value.Before} {value.Operation} {value.Operand} = {value.After}]").ToArray(),
                    Heatmap = CreateHeatmap(items)
                };
            }
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

    private static void ValidatePreviewRequest(EditorPreviewRequest request)
    {
        if (!float.IsFinite(request.WorldTimeScale) || request.WorldTimeScale is < 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(request), "World time scale must be between 0 and 4.");
        if (request.PreviewKind is not ("pattern" or "actor" or "boss-phase" or "stage" or "full-run"))
            throw new ArgumentException($"Unsupported preview kind '{request.PreviewKind}'.", nameof(request));
        foreach (var step in request.InputScript ?? Array.Empty<EditorInputScriptStep>())
            if (step.StartFrame < 0 || step.EndFrame < step.StartFrame)
                throw new ArgumentException("Input script ranges must be ordered non-negative frames.", nameof(request));
        foreach (var injection in request.EventInjections ?? Array.Empty<EditorEventInjection>())
            if (injection.Frame < 0 || injection.Type != "signal" || string.IsNullOrWhiteSpace(injection.Id))
                throw new ArgumentException("Event injections currently require a frame and semantic signal ID.", nameof(request));
    }

    private static PreviewSession CreatePreviewSession(DefinitionCatalog catalog, EditorPreviewRequest request)
    {
        var gameId = string.IsNullOrWhiteSpace(catalog.Game.Id) ? "editor-preview" : catalog.Game.Id;
        var configuration = new RunConfiguration(
            gameId, request.Seed, request.RuleSetId, request.DifficultyId, request.ShipId,
            request.StartStageId, request.CheckpointId, isPractice: request.PreviewKind != "full-run",
            request.InitialPower, request.InitialLives, request.InitialBombs, request.InitialRank,
            request.InitialGauge, request.PlayerInvincible ? 600 : null, variantId: request.VariantId);
        var simulation = new ShootingSimulation(
            new MemoryDefinitionRepository(catalog), new MutableInputState(), configuration);
        foreach (var pair in request.InitialResources ?? new Dictionary<string, double>())
            simulation.Resources.Set(simulation.CompiledDefinitions.ResolveResource(pair.Key), pair.Value);
        simulation.Resources.SynchronizeToLegacy(simulation.RunState, simulation.Player);
        simulation.SetWorldTimeScale(request.WorldTimeScale);
        BootstrapPreviewTarget(simulation, request);
        var checkpoint = simulation.CreateCheckpoint();
        return new PreviewSession(simulation, new SortedDictionary<long, CachedPreviewCheckpoint>
        {
            [checkpoint.Frame] = new CachedPreviewCheckpoint(
                checkpoint, Array.Empty<EditorScoreTrace>(), Array.Empty<EditorRuleTrace>())
        });
    }

    private static void BootstrapPreviewTarget(ShootingSimulation simulation, EditorPreviewRequest request)
    {
        if (request.PreviewKind is "stage" or "full-run") return;
        var id = request.TargetDefinitionId;
        if (string.IsNullOrWhiteSpace(id))
        {
            using var document = JsonDocument.Parse(request.Content);
            id = document.RootElement.TryGetProperty("id", out var value) ? value.GetString() : null;
        }
        if (string.IsNullOrWhiteSpace(id))
            throw new DefinitionValidationException("An isolated preview target requires a definition ID.");
        var path = request.Path.Replace('\\', '/');
        var kind = request.PreviewKind switch
        {
            "pattern" when path.StartsWith("programs/", StringComparison.OrdinalIgnoreCase) =>
                SimulationSandboxTargetKind.Program,
            "pattern" => SimulationSandboxTargetKind.Pattern,
            "actor" => SimulationSandboxTargetKind.Actor,
            "boss-phase" => SimulationSandboxTargetKind.BossPhase,
            _ => throw new ArgumentException($"Unsupported isolated preview kind '{request.PreviewKind}'.", nameof(request))
        };
        simulation.BootstrapSandboxTarget(kind, id, request.CheckpointId);
    }

    private static InputFrame ResolvePreviewInput(EditorPreviewRequest request, int frame)
    {
        if (request.RecordedInputs is { } recorded && frame < recorded.Count) return recorded[frame];
        var scripted = request.InputScript?.LastOrDefault(step => step.StartFrame <= frame && frame <= step.EndFrame);
        if (scripted is not null) return new InputFrame(scripted.MoveX, scripted.MoveY, scripted.Buttons);
        var moveX = (sbyte)(((frame / 120) & 1) == 0 ? 48 : -48);
        return new InputFrame(moveX, 0, InputButtons.Fire);
    }

    private static string CreatePreviewKey(EditorPreviewRequest request)
    {
        var payload = JsonSerializer.Serialize(request with { TargetFrame = 0 });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static IReadOnlyList<EditorHeatmapCell> CreateHeatmap(IReadOnlyList<EditorPreviewItem> items) =>
        items.Where(static item => item.Kind is "PlayerBullet" or "EnemyBullet")
            .GroupBy(static item => ((int)MathF.Floor(item.X / 32), (int)MathF.Floor(item.Y / 32)))
            .OrderBy(static group => group.Key.Item2).ThenBy(static group => group.Key.Item1)
            .Select(static group => new EditorHeatmapCell(group.Key.Item1, group.Key.Item2, group.Count()))
            .ToArray();

    private sealed record CachedPreviewCheckpoint(
        SimulationCheckpoint Checkpoint,
        IReadOnlyList<EditorScoreTrace> ScoreTrace,
        IReadOnlyList<EditorRuleTrace> RuleTrace);

    private sealed record PreviewSession(
        ShootingSimulation Simulation,
        SortedDictionary<long, CachedPreviewCheckpoint> Checkpoints);

    public HeadlessBenchmarkReport Benchmark(string relativePath, string content)
    {
        using var candidate = CreateCandidate(relativePath, content);
        var catalog = LoadAndValidate(candidate.Path);
        return HeadlessBenchmarkRunner.Run(catalog);
    }

    private DefinitionCatalog LoadAndValidate(string rootDirectory)
    {
        var catalog = new JsonDefinitionRepository(rootDirectory).Load();
        _ = new DefinitionCompiler().Compile(catalog, RuntimeCapabilityRegistry.CreateBuiltIn());
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
