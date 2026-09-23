using System.Collections;
using System.Reflection;
using GoatShooooting.Core;

namespace GoatShooooting.Runtime;

/// <summary>An immutable, content-bound snapshot used by replay and editor seek.</summary>
public sealed class SimulationCheckpoint
{
    internal SimulationCheckpoint(CheckpointState state, long frame, ulong stateHash, string compiledContentHash)
    {
        State = state;
        Frame = frame;
        StateHash = stateHash;
        CompiledContentHash = compiledContentHash;
    }

    internal CheckpointState State { get; }
    public long Frame { get; }
    public ulong StateHash { get; }
    public string CompiledContentHash { get; }
}

internal sealed record CheckpointState(
    World World,
    int PlayerId,
    ProjectileStore Projectiles,
    RunStateCheckpoint RunState,
    SimulationTelemetry Telemetry,
    IReadOnlyList<ResourceValueSnapshot> Resources,
    IReadOnlyList<StateMachineCheckpoint> StateMachines,
    StageSystemCheckpoint Stage,
    StageHandle CurrentStageHandle,
    int StageNumber,
    StagePhase Phase,
    double PhaseElapsed,
    long PhaseTicks,
    double LegacyAccumulator,
    long StageStartScore,
    long LastStageScore,
    int RestartGeneration,
    System.Numerics.Vector2 PlayerStartPosition,
    SimulationStatus Status,
    bool IsPaused,
    bool PauseWasPressed,
    bool RetryWasPressed,
    bool BombWasPressed,
    SimulationFeedback Feedback,
    RunResult? Result,
    int WorldScaleQ16,
    long WorldTimeQ16,
    ulong RandomState,
    long EventFrame,
    IReadOnlyList<IGameplayEvent> Events,
    IReadOnlyList<IGameplayEvent> PreviousTickFacts,
    bool NeedsPreviousTickFacts,
    int DefinitionReloadCount,
    string? DefinitionReloadError,
    string? DefinitionContentError,
    IReadOnlyList<string> PendingExternalSignals);

public sealed partial class ShootingSimulation
{
    public SimulationCheckpoint CreateCheckpoint()
    {
        var hash = ComputeCanonicalStateHash();
        var state = new CheckpointState(
            World.Clone(CheckpointObjectCloner.Clone),
            Player.Id,
            Projectiles.CloneForCheckpoint(),
            RunState.CaptureCheckpoint(),
            Telemetry.CloneForCheckpoint(),
            Resources.CaptureCanonicalSnapshot().ToArray(),
            StateMachines.CaptureCheckpoint().ToArray(),
            _stageSystem.CaptureCheckpoint(),
            _currentStageHandle,
            StageNumber,
            Phase,
            _phaseElapsed,
            _phaseTicks,
            _legacyAccumulator,
            _stageStartScore,
            LastStageScore,
            _restartGeneration,
            _playerStartPosition,
            Status,
            IsPaused,
            _pauseWasPressed,
            _retryWasPressed,
            _bombSystem.CaptureCheckpoint(),
            Feedback,
            Result,
            _worldClock.ScaleQ16,
            _worldClock.TimeQ16,
            _randomSource.State,
            Events.Frame,
            Events.Events.ToArray(),
            _previousTickFacts.ToArray(),
            _needsPreviousTickFacts,
            DefinitionReloadCount,
            DefinitionReloadError,
            DefinitionContentError,
            _pendingExternalSignals.ToArray());
        return new SimulationCheckpoint(state, RunState.Frame, hash, CompiledContentHash);
    }

    public void RestoreCheckpoint(SimulationCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (!string.Equals(checkpoint.CompiledContentHash, CompiledContentHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Checkpoint compiled content hash does not match this simulation.");

        var state = checkpoint.State;
        World = state.World.Clone(CheckpointObjectCloner.Clone);
        Player = World.Entities.Single(entity => entity.Id == state.PlayerId);
        Projectiles = state.Projectiles.CloneForCheckpoint();
        RunState.RestoreCheckpoint(state.RunState);
        Telemetry = state.Telemetry.CloneForCheckpoint();
        Resources.RestoreCheckpoint(state.Resources);
        StateMachines.RestoreCheckpoint(state.StateMachines);
        _currentStageHandle = state.CurrentStageHandle;
        CurrentStage = CompiledDefinitions.Get(_currentStageHandle).Definition;
        _stageSystem = state.Stage.CompiledDefinition is { } compiled
            ? new StageSystem(compiled, _enemyFactory, state.Stage.BossesKilledAtStart)
            : new StageSystem(state.Stage.Definition, _enemyFactory, state.Stage.BossesKilledAtStart, _capabilities);
        _stageSystem.RestoreCheckpoint(state.Stage);
        StageNumber = state.StageNumber;
        Phase = state.Phase;
        _phaseElapsed = state.PhaseElapsed;
        _phaseTicks = state.PhaseTicks;
        _legacyAccumulator = state.LegacyAccumulator;
        _stageStartScore = state.StageStartScore;
        LastStageScore = state.LastStageScore;
        _restartGeneration = state.RestartGeneration;
        _playerStartPosition = state.PlayerStartPosition;
        Status = state.Status;
        IsPaused = state.IsPaused;
        _pauseWasPressed = state.PauseWasPressed;
        _retryWasPressed = state.RetryWasPressed;
        _bombSystem.RestoreCheckpoint(state.BombWasPressed);
        Feedback = state.Feedback;
        Result = state.Result;
        _worldClock.Restore(state.WorldScaleQ16, state.WorldTimeQ16);
        _randomSource.Reset(unchecked((long)state.RandomState));
        Events.RestoreCheckpoint(state.EventFrame, state.Events);
        _previousTickFacts = state.PreviousTickFacts.ToArray();
        _needsPreviousTickFacts = state.NeedsPreviousTickFacts;
        DefinitionReloadCount = state.DefinitionReloadCount;
        DefinitionReloadError = state.DefinitionReloadError;
        DefinitionContentError = state.DefinitionContentError;
        _pendingExternalSignals.Clear();
        _pendingExternalSignals.AddRange(state.PendingExternalSignals);

        var restoredHash = ComputeCanonicalStateHash();
        if (restoredHash != checkpoint.StateHash)
            throw new InvalidOperationException(
                $"Checkpoint restore hash mismatch: expected {checkpoint.StateHash:X16}, got {restoredHash:X16}.");
    }
}

internal static class CheckpointObjectCloner
{
    private static readonly MethodInfo MemberwiseCloneMethod = typeof(object).GetMethod(
        "MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static object Clone(object source) => Clone(source, new Dictionary<object, object>(ReferenceEqualityComparer.Instance));

    private static object Clone(object source, IDictionary<object, object> visited)
    {
        var type = source.GetType();
        if (type.IsValueType || source is string || source is Type || source is Delegate ||
            type.Namespace?.StartsWith("GoatShooooting.Definitions", StringComparison.Ordinal) == true ||
            type.Name.StartsWith("Compiled", StringComparison.Ordinal)) return source;
        if (visited.TryGetValue(source, out var existing)) return existing;

        if (source is Array array)
        {
            var copy = (Array)array.Clone();
            visited.Add(source, copy);
            if (!type.GetElementType()!.IsValueType && type.GetElementType() != typeof(string))
                for (var index = 0; index < copy.Length; index++)
                    if (copy.GetValue(index) is { } value) copy.SetValue(Clone(value, visited), index);
            return copy;
        }
        if (source is IDictionary dictionary)
        {
            var copy = (IDictionary)Activator.CreateInstance(type)!;
            visited.Add(source, copy);
            foreach (DictionaryEntry entry in dictionary)
                copy.Add(Clone(entry.Key, visited), entry.Value is null ? null : Clone(entry.Value, visited));
            return copy;
        }
        if (source is IList list && type.GetConstructor(Type.EmptyTypes) is not null)
        {
            var copy = (IList)Activator.CreateInstance(type)!;
            visited.Add(source, copy);
            foreach (var value in list) copy.Add(value is null ? null : Clone(value, visited));
            return copy;
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>))
        {
            var copy = Activator.CreateInstance(type)!;
            visited.Add(source, copy);
            var add = type.GetMethod("Add")!;
            foreach (var value in (IEnumerable)source) _ = add.Invoke(copy, new[] { Clone(value!, visited) });
            return copy;
        }
        if (type.Assembly != typeof(Entity).Assembly && type.Assembly != typeof(ShootingSimulation).Assembly)
            return source;

        var clone = MemberwiseCloneMethod.Invoke(source, null)!;
        visited.Add(source, clone);
        for (var cursor = type; cursor is not null; cursor = cursor.BaseType)
        {
            foreach (var field in cursor.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.DeclaredOnly))
            {
                if (field.GetValue(source) is not { } value) continue;
                var copied = Clone(value, visited);
                if (!ReferenceEquals(value, copied)) field.SetValue(clone, copied);
            }
        }
        return clone;
    }
}
