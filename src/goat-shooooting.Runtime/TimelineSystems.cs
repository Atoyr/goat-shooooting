using System.Numerics;
using System.Text.Json;
using GoatShooooting.Core;
using GoatShooooting.Definitions;

namespace GoatShooooting.Runtime;

public sealed class MotionTimelineComponent(
    string patternId,
    IReadOnlyList<TimelineCommandDefinition>? compiledCommands = null)
{
    public string PatternId { get; } = patternId;
    public IReadOnlyList<TimelineCommandDefinition>? CompiledCommands { get; } = compiledCommands;
    public int CommandIndex { get; set; }
    public float Elapsed { get; set; }
    public Vector2 Start { get; set; }
    public Vector2 PlayerSnapshot { get; set; }
    public bool CommandStarted { get; set; }
}

public sealed class AttackTimelineComponent(
    IEnumerable<string> patternIds,
    IReadOnlyList<CompiledPatternDefinition>? compiledPatterns = null)
{
    public IReadOnlyList<string> PatternIds { get; } = patternIds.ToArray();
    public IReadOnlyList<CompiledPatternDefinition>? CompiledPatterns { get; } = compiledPatterns;
    internal List<AttackTimelineTrack> Tracks { get; } = new();
    internal bool Initialized { get; set; }
}

internal sealed class AttackTimelineTrack
{
    public AttackTimelineTrack(string patternId, IReadOnlyList<TimelineCommandDefinition> commands)
    {
        PatternId = patternId;
        Commands = commands;
    }

    public AttackTimelineTrack(CompiledPatternDefinition pattern)
    {
        PatternId = pattern.Definition.Id;
        Commands = pattern.Commands;
        RuntimeCommands = pattern.RuntimeCommands;
    }

    public string PatternId { get; }
    public IReadOnlyList<TimelineCommandDefinition> Commands { get; }
    public IReadOnlyList<CompiledTimelineCommandDefinition>? RuntimeCommands { get; }
    public int CommandIndex { get; set; }
    public float WaitRemaining { get; set; }
}

internal static class TimelineCompiler
{
    public static IReadOnlyList<TimelineCommandDefinition> Compile(DefinitionCatalog definitions, string patternId)
    {
        var result = new List<TimelineCommandDefinition>();
        Expand(definitions, definitions.GetPattern(patternId), result);
        return result;
    }

    private static void Expand(
        DefinitionCatalog definitions,
        PatternDefinition pattern,
        List<TimelineCommandDefinition> result)
    {
        foreach (var command in pattern.Commands)
        {
            if (command.Type is "include" or "repeat")
            {
                for (var count = 0; count < command.RepeatCount; count++)
                {
                    Expand(definitions, definitions.GetPattern(command.PatternId!), result);
                }
            }
            else
            {
                for (var count = 0; count < command.RepeatCount; count++) result.Add(command);
            }
        }
    }
}

public sealed class MotionTimelineSystem
{
    public void Update(
        World world,
        CompiledCatalog definitions,
        string? difficultyId,
        float deltaTime,
        SimulationTelemetry? telemetry = null,
        IReadOnlyList<string>? patternTags = null) =>
        UpdateCore(world, definitions.Source, difficultyId, deltaTime, telemetry, patternTags);

    public void Update(
        World world,
        DefinitionCatalog definitions,
        string? difficultyId,
        float deltaTime,
        SimulationTelemetry? telemetry = null,
        IReadOnlyList<string>? patternTags = null)
        => UpdateCore(world, definitions, difficultyId, deltaTime, telemetry, patternTags);

    private static void UpdateCore(
        World world,
        DefinitionCatalog definitions,
        string? difficultyId,
        float deltaTime,
        SimulationTelemetry? telemetry,
        IReadOnlyList<string>? patternTags)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(definitions);
        foreach (var entity in world.Query<MotionTimelineComponent, TransformComponent, VelocityComponent>().ToArray())
        {
            if (entity.Has<PendingDestroyComponent>()) continue;
            var state = entity.Get<MotionTimelineComponent>();
            var commands = state.CompiledCommands ?? TimelineCompiler.Compile(definitions, state.PatternId);
            while (state.CommandIndex < commands.Count && !IsEnabled(commands[state.CommandIndex], difficultyId, patternTags))
            {
                state.CommandIndex++;
            }

            if (state.CommandIndex >= commands.Count)
            {
                entity.Get<VelocityComponent>().Value = Vector2.Zero;
                entity.Remove<MotionTimelineComponent>();
                continue;
            }

            var command = commands[state.CommandIndex];
            var transform = entity.Get<TransformComponent>();
            if (!state.CommandStarted)
            {
                state.Start = transform.Position;
                state.PlayerSnapshot = FindPlayerPosition(world) ?? Vector2.Zero;
                state.Elapsed = 0;
                state.CommandStarted = true;
            }

            var previous = transform.Position;
            state.Elapsed = Math.Min(GetFloat(command, "duration"), state.Elapsed + deltaTime);
            var duration = GetFloat(command, "duration");
            var progress = duration == 0 ? 1 : Math.Clamp(state.Elapsed / duration, 0, 1);
            var eased = Ease(progress, GetString(command, "easing", "linear"));
            transform.Position = command.Type switch
            {
                "wait" => state.Start,
                "enter" or "move-to" or "move-by" or "leave" =>
                    Vector2.Lerp(state.Start, ResolveTarget(command, state, entity), eased),
                "follow-path" => ResolvePath(command, state, eased),
                "orbit" => ResolveOrbit(command, state, eased),
                _ => throw new InvalidOperationException($"Unsupported motion command '{command.Type}'.")
            };
            var displacement = transform.Position - previous;
            entity.Get<VelocityComponent>().Value = deltaTime > 0 ? displacement / deltaTime : Vector2.Zero;
            if (telemetry is not null && displacement != Vector2.Zero) telemetry.EnemyMovementFrames++;

            if (state.Elapsed < duration) continue;
            if (command.Type == "leave") entity.Add(new PendingDestroyComponent());
            state.CommandIndex++;
            state.CommandStarted = false;
        }
    }

    private static Vector2 ResolveTarget(
        TimelineCommandDefinition command,
        MotionTimelineComponent state,
        Entity entity)
    {
        var value = new Vector2(GetFloat(command, "x", 0), GetFloat(command, "y", 0));
        if (GetString(command, "reference", "none") == "player-snapshot") return state.PlayerSnapshot + value;
        if (command.Type == "move-by")
        {
            if (GetString(command, "space", "world") == "local" &&
                entity.Get<VelocityComponent>().Value is { } heading && heading != Vector2.Zero)
            {
                var forward = Vector2.Normalize(heading);
                var right = new Vector2(forward.Y, -forward.X);
                return state.Start + (right * value.X) + (forward * value.Y);
            }

            return state.Start + value;
        }

        return GetString(command, "space", "world") == "local" ? state.Start + value : value;
    }

    private static Vector2 ResolvePath(
        TimelineCommandDefinition command,
        MotionTimelineComponent state,
        float progress)
    {
        if (!command.Parameters.TryGetValue("points", out var element) || element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("follow-path requires a points array.");
        }

        var points = element.EnumerateArray().Select(point => new Vector2(
            point.GetProperty("x").GetSingle(), point.GetProperty("y").GetSingle())).ToList();
        if (GetString(command, "space", "world") == "local")
        {
            for (var index = 0; index < points.Count; index++) points[index] += state.Start;
        }
        else if (GetString(command, "reference", "none") == "player-snapshot")
        {
            for (var index = 0; index < points.Count; index++) points[index] += state.PlayerSnapshot;
        }

        points.Insert(0, state.Start);
        var scaled = progress * (points.Count - 1);
        var segment = Math.Min(points.Count - 2, (int)scaled);
        return Vector2.Lerp(points[segment], points[segment + 1], scaled - segment);
    }

    private static Vector2 ResolveOrbit(
        TimelineCommandDefinition command,
        MotionTimelineComponent state,
        float progress)
    {
        var center = new Vector2(GetFloat(command, "x", 0), GetFloat(command, "y", 0));
        if (GetString(command, "reference", "none") == "player-snapshot") center += state.PlayerSnapshot;
        else if (GetString(command, "space", "world") == "local") center += state.Start;
        var offset = state.Start - center;
        var radius = GetFloat(command, "radius", offset.Length());
        var startAngle = GetFloat(command, "startAngleDegrees", MathF.Atan2(offset.Y, offset.X) * 180 / MathF.PI);
        var degrees = startAngle + (360 * GetFloat(command, "revolutions", 1) * progress);
        var radians = degrees * MathF.PI / 180;
        return center + new Vector2(MathF.Cos(radians), MathF.Sin(radians)) * radius;
    }

    private static Vector2? FindPlayerPosition(World world) => world.Query<PlayerComponent, TransformComponent>()
        .Where(static entity => !entity.Has<PendingDestroyComponent>())
        .OrderBy(static entity => entity.Id)
        .Select(static entity => (Vector2?)entity.Get<TransformComponent>().Position)
        .FirstOrDefault();

    private static float Ease(float value, string easing) => easing switch
    {
        "linear" => value,
        "ease-in" => value * value,
        "ease-out" => 1 - ((1 - value) * (1 - value)),
        "ease-in-out" => value < 0.5f ? 2 * value * value : 1 - (MathF.Pow(-2 * value + 2, 2) / 2),
        _ => throw new InvalidOperationException($"Unsupported easing '{easing}'.")
    };

    internal static bool IsEnabled(
        TimelineCommandDefinition command,
        string? difficultyId,
        IReadOnlyList<string>? patternTags = null) =>
        command.DifficultyTags.Count == 0 ||
        difficultyId is not null && command.DifficultyTags.Contains(difficultyId, StringComparer.Ordinal) ||
        patternTags is not null && command.DifficultyTags.Any(tag => patternTags.Contains(tag, StringComparer.Ordinal));

    internal static float GetFloat(TimelineCommandDefinition command, string name, float defaultValue = 0) =>
        command.Parameters.TryGetValue(name, out var value) && value.TryGetSingle(out var result) ? result : defaultValue;

    internal static string GetString(TimelineCommandDefinition command, string name, string defaultValue) =>
        command.Parameters.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? defaultValue
            : defaultValue;
}

public sealed class AttackTimelineSystem
{
    private readonly AdvancedWeaponSystem _weapons;

    public AttackTimelineSystem(AdvancedWeaponSystem weapons) =>
        _weapons = weapons ?? throw new ArgumentNullException(nameof(weapons));

    public void Update(
        World world,
        CompiledCatalog definitions,
        string? difficultyId,
        float deltaTime,
        SimulationTelemetry telemetry,
        ProjectileStore projectiles,
        IReadOnlyList<string>? patternTags = null) =>
        UpdateCore(world, definitions.Source, definitions, difficultyId, deltaTime, telemetry, projectiles, patternTags);

    public void Update(
        World world,
        DefinitionCatalog definitions,
        string? difficultyId,
        float deltaTime,
        SimulationTelemetry telemetry,
        ProjectileStore projectiles,
        IReadOnlyList<string>? patternTags = null)
        => UpdateCore(world, definitions, null, difficultyId, deltaTime, telemetry, projectiles, patternTags);

    private void UpdateCore(
        World world,
        DefinitionCatalog definitions,
        CompiledCatalog? compiledDefinitions,
        string? difficultyId,
        float deltaTime,
        SimulationTelemetry telemetry,
        ProjectileStore projectiles,
        IReadOnlyList<string>? patternTags)
    {
        foreach (var entity in world.Query<AttackTimelineComponent, TransformComponent>().ToArray())
        {
            if (entity.Has<PendingDestroyComponent>()) continue;
            var runner = entity.Get<AttackTimelineComponent>();
            if (!runner.Initialized)
            {
                if (runner.CompiledPatterns is not null)
                {
                    foreach (var pattern in runner.CompiledPatterns)
                        runner.Tracks.Add(new AttackTimelineTrack(pattern));
                }
                else
                {
                    foreach (var patternId in runner.PatternIds)
                    {
                        if (compiledDefinitions is null)
                            runner.Tracks.Add(new AttackTimelineTrack(
                                patternId, TimelineCompiler.Compile(definitions, patternId)));
                        else
                            runner.Tracks.Add(new AttackTimelineTrack(
                                compiledDefinitions.Get(compiledDefinitions.ResolvePattern(patternId))));
                    }
                }

                runner.Initialized = true;
            }

            var stopped = new HashSet<string>(StringComparer.Ordinal);
            foreach (var track in runner.Tracks.ToArray())
            {
                if (track.WaitRemaining > 0)
                {
                    track.WaitRemaining = Math.Max(0, track.WaitRemaining - deltaTime);
                    if (track.WaitRemaining > 0) continue;
                }

                while (track.CommandIndex < track.Commands.Count)
                {
                    var commandIndex = track.CommandIndex++;
                    var command = track.Commands[commandIndex];
                    var runtimeCommand = track.RuntimeCommands?[commandIndex];
                    if (!MotionTimelineSystem.IsEnabled(command, difficultyId, patternTags)) continue;
                    switch (command.Type)
                    {
                        case "fire":
                            var hasHolder = entity.TryGet<WeaponHolderComponent>(out var holder);
                            var weaponId = MotionTimelineSystem.GetString(
                                command, "weaponId", hasHolder ? holder.WeaponId : string.Empty);
                            if (compiledDefinitions is not null)
                            {
                                var weaponHandle = runtimeCommand?.WeaponHandle ??
                                    (hasHolder && holder.WeaponHandle >= 0
                                        ? new WeaponHandle(holder.WeaponHandle)
                                        : (WeaponHandle?)null);
                                if (weaponHandle is null)
                                    throw new InvalidOperationException(
                                        $"Compiled timeline fire in '{track.PatternId}' for '{weaponId}' requires a weapon handle " +
                                        $"(runtimeCommand={runtimeCommand is not null}, holderHandle={holder?.WeaponHandle ?? -1}).");
                                _weapons.FireOnce(
                                    world, compiledDefinitions, entity, weaponHandle.Value, telemetry, projectiles);
                            }
                            else if (string.IsNullOrWhiteSpace(weaponId))
                            {
                                throw new InvalidOperationException("Timeline fire requires weaponId or WeaponHolderComponent.");
                            }
                            else
                                _weapons.FireOnce(world, definitions, entity, weaponId, telemetry, projectiles);
                            break;
                        case "wait":
                            track.WaitRemaining = MotionTimelineSystem.GetFloat(command, "duration");
                            break;
                        case "parallel":
                        case "start-pattern":
                            if (compiledDefinitions is null)
                            {
                                runner.Tracks.Add(new AttackTimelineTrack(
                                    command.PatternId!, TimelineCompiler.Compile(definitions, command.PatternId!)));
                            }
                            else
                            {
                                var patternHandle = runtimeCommand?.PatternHandle ?? throw new InvalidOperationException(
                                    "Compiled timeline branch requires a pattern handle.");
                                runner.Tracks.Add(new AttackTimelineTrack(compiledDefinitions.Get(patternHandle)));
                            }
                            break;
                        case "stop-pattern":
                            stopped.Add(command.PatternId!);
                            break;
                        default:
                            throw new InvalidOperationException($"Unsupported attack command '{command.Type}'.");
                    }

                    if (track.WaitRemaining > 0) break;
                }
            }

            runner.Tracks.RemoveAll(track =>
                stopped.Contains(track.PatternId) || track.CommandIndex >= track.Commands.Count && track.WaitRemaining <= 0);
            if (runner.Tracks.Count == 0) entity.Remove<AttackTimelineComponent>();
        }
    }
}
