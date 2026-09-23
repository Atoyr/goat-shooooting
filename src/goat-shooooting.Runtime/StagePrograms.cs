using System.Numerics;
using System.Text.Json;
using GoatShooooting.Core;
using GoatShooooting.Definitions;

namespace GoatShooooting.Runtime;

public readonly record struct StageProgramHandle(int Value) : IDefinitionHandle;
public readonly record struct EffectRecipeHandle(int Value) : IDefinitionHandle;
public readonly record struct AnimationStateHandle(int Value) : IDefinitionHandle;

public enum StageTrackKind { Spawn, Environment, Camera, Audio, Ui, Route }
public enum StageClockDomain { RunFrame, WorldTime }

public sealed record CompiledStageProgramEvent(
    StageProgramEventDefinition Definition,
    ActorHandle? ActorHandle,
    BossHandle? BossHandle,
    string? CueId,
    float X,
    float Y,
    int Count,
    float SpacingX,
    float SpacingY,
    float Value);

public sealed record CompiledStageTrack(
    int Handle,
    StageTrackDefinition Definition,
    StageTrackKind Kind,
    StageClockDomain Clock,
    IReadOnlyList<CompiledStageProgramEvent> Events);

public sealed record CompiledStageProgram(
    StageProgramHandle Handle,
    StageProgramDefinition Definition,
    IReadOnlyList<CompiledStageTrack> Tracks);

public sealed record CompiledEffectRecipe(
    EffectRecipeHandle Handle,
    EffectRecipeDefinition Definition,
    EventFactDescriptor Event,
    CompiledExpression? Condition);

public sealed class FixedWorldClock
{
    public const int One = 1 << 16;
    public const int MaximumScale = 4 * One;

    public int ScaleQ16 { get; private set; } = One;
    public long TimeQ16 { get; private set; }
    public long Frame => TimeQ16 >> 16;
    public float DeltaSeconds => ScaleQ16 / (float)One / SimulationTiming.TicksPerSecond;

    public void Advance() => TimeQ16 = checked(TimeQ16 + ScaleQ16);

    public void SetScale(float value)
    {
        if (!float.IsFinite(value) || value < 0 || value > 4) throw new ArgumentOutOfRangeException(nameof(value));
        ScaleQ16 = Math.Clamp((int)MathF.Round(value * One), 0, MaximumScale);
    }

    public void Reset()
    {
        ScaleQ16 = One;
        TimeQ16 = 0;
    }

    internal void Restore(int scaleQ16, long timeQ16)
    {
        if (scaleQ16 is < 0 or > MaximumScale || timeQ16 < 0) throw new ArgumentOutOfRangeException(nameof(scaleQ16));
        ScaleQ16 = scaleQ16;
        TimeQ16 = timeQ16;
    }
}

public sealed class StagePresentationState
{
    public string? BackgroundId { get; internal set; }
    public float ScrollX { get; internal set; }
    public float ScrollY { get; internal set; }
    public Vector2 CameraOffset { get; internal set; }
    public float CameraZoom { get; internal set; } = 1;
    public float CameraShake { get; internal set; }
    public string? UiCueId { get; internal set; }
    public string? WeatherCueId { get; internal set; }
    public string? GroundLayerCueId { get; internal set; }
    public string? MusicCueId { get; internal set; }
}

public sealed record StageProgramRuntimeSnapshot(
    IReadOnlyList<(int Track, int Event, long WaitStarted)> Tracks,
    IReadOnlyList<string> Signals,
    bool ForceClear);

internal sealed record StageProgramCheckpoint(
    StageProgramRuntimeSnapshot Runtime,
    StagePresentationCheckpoint Presentation,
    long CameraShakeEndFrame);

internal sealed record StagePresentationCheckpoint(
    string? BackgroundId,
    float ScrollX,
    float ScrollY,
    Vector2 CameraOffset,
    float CameraZoom,
    float CameraShake,
    string? UiCueId,
    string? WeatherCueId,
    string? GroundLayerCueId,
    string? MusicCueId);

public sealed record StageSignalEvent(
    long Frame,
    int Sequence,
    string StageProgramId,
    string SignalId,
    string NodeId) : IGameplayEvent;

public sealed record StagePresentationCueEvent(
    long Frame,
    int Sequence,
    string Kind,
    string CueId,
    float X = 0,
    float Y = 0,
    float Value = 0,
    int DurationFrames = 0) : IGameplayEvent;

public sealed record WorldTimeScaleChangedEvent(
    long Frame,
    int Sequence,
    float Scale,
    string NodeId) : IGameplayEvent;

public sealed record StageAudioCueEvent(
    long Frame,
    int Sequence,
    string Kind,
    string? CueId,
    int DurationFrames = 0) : IGameplayEvent;

internal static class StageProgramCompiler
{
    public static CompiledStageProgram Compile(
        StageProgramDefinition definition,
        StageProgramHandle handle,
        IReadOnlyDictionary<string, ActorHandle> actorHandles,
        IReadOnlyDictionary<string, BossHandle> bossHandles)
    {
        var tracks = definition.Tracks
            .OrderBy(static track => ParseKind(track.Kind))
            .ThenBy(static track => track.Id, StringComparer.Ordinal)
            .Select((track, trackIndex) => new CompiledStageTrack(
                trackIndex,
                track,
                ParseKind(track.Kind),
                track.Clock == "world-time" ? StageClockDomain.WorldTime : StageClockDomain.RunFrame,
                Array.AsReadOnly(track.Events
                    .OrderBy(static stageEvent => stageEvent.Frame)
                    .ThenBy(static stageEvent => stageEvent.NodeId, StringComparer.Ordinal)
                    .Select(stageEvent => CompileEvent(stageEvent, actorHandles, bossHandles))
                    .ToArray())))
            .ToArray();
        return new CompiledStageProgram(handle, definition, Array.AsReadOnly(tracks));
    }

    private static CompiledStageProgramEvent CompileEvent(
        StageProgramEventDefinition definition,
        IReadOnlyDictionary<string, ActorHandle> actorHandles,
        IReadOnlyDictionary<string, BossHandle> bossHandles)
    {
        var actorId = String(definition, "actorId");
        var bossId = String(definition, "bossId");
        return new CompiledStageProgramEvent(
            definition,
            actorId is null ? null : actorHandles[actorId],
            bossId is null ? null : bossHandles[bossId],
            String(definition, "cueId"),
            Number(definition, "x"),
            Number(definition, "y"),
            Math.Max(1, Integer(definition, "count", 1)),
            Number(definition, "spacingX"),
            Number(definition, "spacingY"),
            Number(definition, definition.Op == "set-world-time-scale" ? "scale" : "value"));
    }

    private static StageTrackKind ParseKind(string value) => value switch
    {
        "spawn" => StageTrackKind.Spawn,
        "environment" => StageTrackKind.Environment,
        "camera" => StageTrackKind.Camera,
        "audio" => StageTrackKind.Audio,
        "ui" => StageTrackKind.Ui,
        "route" => StageTrackKind.Route,
        _ => throw new DefinitionValidationException($"Unknown stage track kind '{value}'.")
    };

    private static string? String(StageProgramEventDefinition value, string name) =>
        value.Arguments.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() : null;
    private static float Number(StageProgramEventDefinition value, string name) =>
        value.Arguments.TryGetValue(name, out var element) && element.TryGetSingle(out var result) ? result : 0;
    private static int Integer(StageProgramEventDefinition value, string name, int fallback) =>
        value.Arguments.TryGetValue(name, out var element) && element.TryGetInt32(out var result) ? result : fallback;
}

internal static class EffectRecipeCompiler
{
    public static CompiledEffectRecipe Compile(EffectRecipeDefinition definition, EffectRecipeHandle handle)
    {
        var descriptor = EventFactRegistry.Resolve(definition.On, $"effects/{definition.Id}.json $.on");
        var bindings = EventRuleCompiler.CreateBindings(
            new Dictionary<string, ExpressionValueType>(StringComparer.Ordinal), descriptor.Fields);
        bindings.AddContext("particleDensity", ExpressionValueType.Number)
            .AddContext("flashIntensity", ExpressionValueType.Number)
            .AddContext("shakeIntensity", ExpressionValueType.Number)
            .AddContext("trailsEnabled", ExpressionValueType.Boolean);
        var condition = definition.When is { } when
            ? ExpressionCompiler.Compile(
                when, ExpressionValueType.Boolean, CompiledParameterSchema.Empty, bindings,
                $"effects/{definition.Id}.json $.when")
            : null;
        return new CompiledEffectRecipe(handle, definition, descriptor, condition);
    }
}

public sealed class StageProgramRuntime
{
    private readonly CompiledStageProgram _program;
    private readonly TrackState[] _tracks;
    private readonly HashSet<string> _signals = new(StringComparer.Ordinal);
    private readonly EnemyFactory _enemyFactory;
    private long _cameraShakeEndFrame = -1;

    public StageProgramRuntime(CompiledStageProgram program, EnemyFactory enemyFactory, string? initialBackgroundId)
    {
        _program = program;
        _enemyFactory = enemyFactory;
        _tracks = program.Tracks.Select(static _ => new TrackState()).ToArray();
        Presentation = new StagePresentationState { BackgroundId = initialBackgroundId };
    }

    public StagePresentationState Presentation { get; }
    public bool ForceClear { get; private set; }
    public bool IsComplete => ForceClear || _tracks.All(static track => track.Complete);
    public int BossCount => _program.Tracks.SelectMany(static track => track.Events)
        .Where(static stageEvent => stageEvent.Definition.Op == "spawn-boss")
        .Sum(static stageEvent => stageEvent.Count);

    public StageProgramRuntimeSnapshot CaptureSnapshot() => new(
        Array.AsReadOnly(_tracks.Select((track, index) =>
            (index, track.EventIndex, track.WaitStarted)).ToArray()),
        Array.AsReadOnly(_signals.Order(StringComparer.Ordinal).ToArray()),
        ForceClear);

    internal StageProgramCheckpoint CaptureCheckpoint() => new(
        CaptureSnapshot(),
        new StagePresentationCheckpoint(
            Presentation.BackgroundId, Presentation.ScrollX, Presentation.ScrollY,
            Presentation.CameraOffset, Presentation.CameraZoom, Presentation.CameraShake,
            Presentation.UiCueId, Presentation.WeatherCueId, Presentation.GroundLayerCueId,
            Presentation.MusicCueId),
        _cameraShakeEndFrame);

    internal void RestoreCheckpoint(StageProgramCheckpoint checkpoint)
    {
        if (checkpoint.Runtime.Tracks.Count != _tracks.Length)
            throw new InvalidOperationException("Stage program checkpoint does not match the compiled program.");
        foreach (var (track, eventIndex, waitStarted) in checkpoint.Runtime.Tracks)
        {
            var state = _tracks[track];
            state.EventIndex = eventIndex;
            state.WaitStarted = waitStarted;
            state.Complete = eventIndex >= _program.Tracks[track].Events.Count;
        }
        _signals.Clear();
        foreach (var signal in checkpoint.Runtime.Signals) _signals.Add(signal);
        ForceClear = checkpoint.Runtime.ForceClear;
        var presentation = checkpoint.Presentation;
        Presentation.BackgroundId = presentation.BackgroundId;
        Presentation.ScrollX = presentation.ScrollX;
        Presentation.ScrollY = presentation.ScrollY;
        Presentation.CameraOffset = presentation.CameraOffset;
        Presentation.CameraZoom = presentation.CameraZoom;
        Presentation.CameraShake = presentation.CameraShake;
        Presentation.UiCueId = presentation.UiCueId;
        Presentation.WeatherCueId = presentation.WeatherCueId;
        Presentation.GroundLayerCueId = presentation.GroundLayerCueId;
        Presentation.MusicCueId = presentation.MusicCueId;
        _cameraShakeEndFrame = checkpoint.CameraShakeEndFrame;
    }

    public void Update(
        World world,
        CompiledCatalog definitions,
        long runFrame,
        long worldFrame,
        FixedWorldClock worldClock,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        if (_cameraShakeEndFrame >= 0 && runFrame >= _cameraShakeEndFrame)
        {
            Presentation.CameraShake = 0;
            _cameraShakeEndFrame = -1;
        }
        for (var trackIndex = 0; trackIndex < _program.Tracks.Count; trackIndex++)
        {
            var track = _program.Tracks[trackIndex];
            var state = _tracks[trackIndex];
            var clock = track.Clock == StageClockDomain.RunFrame ? runFrame : worldFrame;
            while (state.EventIndex < track.Events.Count)
            {
                var command = track.Events[state.EventIndex];
                if (command.Definition.Frame > clock) break;
                if (!CanPassWait(world, command.Definition, clock, state)) break;
                Execute(world, definitions, command, runFrame, worldClock, telemetry, events);
                state.EventIndex++;
                state.WaitStarted = -1;
            }
            state.Complete = state.EventIndex >= track.Events.Count ||
                _program.Definition.EndFrame is { } end && runFrame >= end;
        }
    }

    private bool CanPassWait(World world, StageProgramEventDefinition command, long clock, TrackState state)
    {
        if (command.Op is not ("wait-signal" or "wait-until-clear")) return true;
        state.WaitStarted = state.WaitStarted < 0 ? clock : state.WaitStarted;
        var satisfied = command.Op == "wait-signal"
            ? _signals.Contains(command.WaitForSignalId!)
            : !world.Query<EnemyComponent>().Any(static entity => !entity.Has<PendingDestroyComponent>());
        return satisfied || command.TimeoutFrames > 0 && clock - state.WaitStarted >= command.TimeoutFrames;
    }

    private void Execute(
        World world,
        CompiledCatalog definitions,
        CompiledStageProgramEvent command,
        long runFrame,
        FixedWorldClock worldClock,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        var definition = command.Definition;
        switch (definition.Op)
        {
            case "spawn-actor":
            case "spawn-hazard":
            case "spawn-formation":
                for (var index = 0; index < command.Count; index++)
                {
                    _enemyFactory.CreateActor(
                        world,
                        definitions,
                        command.ActorHandle!.Value,
                        new Vector2(command.X + (command.SpacingX * index), command.Y + (command.SpacingY * index)));
                    telemetry.EnemiesSpawned++;
                }
                break;
            case "spawn-boss":
                var boss = definitions.GetCompiled(command.BossHandle!.Value);
                for (var index = 0; index < command.Count; index++)
                {
                    _enemyFactory.CreateActor(
                        world,
                        definitions,
                        boss.ActorHandle,
                        new Vector2(
                            command.X + (command.SpacingX * index),
                            command.Y + (command.SpacingY * index)),
                        isBoss: true,
                        command.BossHandle);
                    telemetry.EnemiesSpawned++;
                }
                break;
            case "set-background":
                Presentation.BackgroundId = command.CueId;
                PublishCue(events, definition, command.CueId!);
                break;
            case "set-scroll":
                Presentation.ScrollX = command.X;
                Presentation.ScrollY = command.Y;
                PublishCue(events, definition, "scroll", command.X, command.Y);
                break;
            case "set-weather":
                Presentation.WeatherCueId = command.CueId;
                PublishCue(events, definition, command.CueId!);
                break;
            case "set-ground-layer":
                Presentation.GroundLayerCueId = command.CueId;
                PublishCue(events, definition, command.CueId!);
                break;
            case "camera-pan":
                Presentation.CameraOffset = new Vector2(command.X, command.Y);
                PublishCue(events, definition, "camera-pan", command.X, command.Y);
                break;
            case "camera-zoom":
                Presentation.CameraZoom = command.Value == 0 ? 1 : command.Value;
                PublishCue(events, definition, "camera-zoom", value: Presentation.CameraZoom);
                break;
            case "camera-shake":
                Presentation.CameraShake = command.Value;
                _cameraShakeEndFrame = runFrame + Math.Max(1, definition.DurationFrames);
                PublishCue(events, definition, "camera-shake", value: command.Value);
                break;
            case "set-world-time-scale":
                worldClock.SetScale(command.Value);
                events.Publish((frame, sequence) => new WorldTimeScaleChangedEvent(
                    frame, sequence, command.Value, definition.NodeId));
                break;
            case "set-bgm":
                Presentation.MusicCueId = command.CueId;
                events.Publish((frame, sequence) => new StageAudioCueEvent(
                    frame, sequence, definition.Op, command.CueId, definition.DurationFrames));
                break;
            case "stinger":
                events.Publish((frame, sequence) => new StageAudioCueEvent(
                    frame, sequence, definition.Op, command.CueId, definition.DurationFrames));
                break;
            case "duck":
                events.Publish((frame, sequence) => new StageAudioCueEvent(
                    frame, sequence, definition.Op, null, definition.DurationFrames));
                PublishCue(events, definition, "duck", value: command.Value);
                break;
            case "warning":
            case "message":
            case "boss-title":
                Presentation.UiCueId = command.CueId;
                PublishCue(events, definition, command.CueId!);
                break;
            case "checkpoint":
                PublishCue(events, definition, command.CueId!);
                break;
            case "clear-stage":
                ForceClear = true;
                break;
            case "emit-signal":
                _signals.Add(definition.SignalId!);
                events.Publish((frame, sequence) => new StageSignalEvent(
                    frame, sequence, _program.Definition.Id, definition.SignalId!, definition.NodeId));
                break;
        }
    }

    private static void PublishCue(
        GameEventBuffer events,
        StageProgramEventDefinition definition,
        string cueId,
        float x = 0,
        float y = 0,
        float value = 0) => events.Publish((frame, sequence) => new StagePresentationCueEvent(
            frame, sequence, definition.Op, cueId, x, y, value, definition.DurationFrames));

    private sealed class TrackState
    {
        public int EventIndex { get; set; }
        public long WaitStarted { get; set; } = -1;
        public bool Complete { get; set; }
    }
}
