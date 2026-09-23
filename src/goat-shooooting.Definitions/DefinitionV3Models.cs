using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoatShooooting.Definitions;

public sealed record ProgramParameterDefinition
{
    public string Type { get; init; } = string.Empty;
    public JsonElement Default { get; init; }
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
}

public sealed record ProgramNodeDefinition
{
    public string NodeId { get; init; } = string.Empty;
    public string Op { get; init; } = string.Empty;

    [JsonExtensionData]
    public IDictionary<string, JsonElement> Arguments { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}

public sealed record ProgramDefinition
{
    public int SchemaVersion { get; init; } = 3;
    public string Id { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, ProgramParameterDefinition> Parameters { get; init; } =
        new Dictionary<string, ProgramParameterDefinition>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, IReadOnlyList<ProgramNodeDefinition>> EntryPoints { get; init; } =
        new Dictionary<string, IReadOnlyList<ProgramNodeDefinition>>(StringComparer.Ordinal);
}

public sealed record ParameterSetDefinition
{
    public int SchemaVersion { get; init; } = 3;
    public string Id { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, JsonElement> Values { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}

public sealed record VariantBindingDefinition
{
    public string SlotId { get; init; } = string.Empty;
    public string ProgramId { get; init; } = string.Empty;
    public string? ParameterSetId { get; init; }
}

public sealed record VariantRuleBindingDefinition
{
    public string SlotId { get; init; } = string.Empty;
    public string EventRuleId { get; init; } = string.Empty;
}

public sealed record VariantDefinition
{
    public int SchemaVersion { get; init; } = 3;
    public string Id { get; init; } = string.Empty;
    public IReadOnlyList<VariantBindingDefinition> Bindings { get; init; } =
        Array.Empty<VariantBindingDefinition>();
    public IReadOnlyList<VariantRuleBindingDefinition> RuleBindings { get; init; } =
        Array.Empty<VariantRuleBindingDefinition>();
}

/// <summary>A semantic authoring reference embedded by Actor, Stage, or Rule definitions.</summary>
public sealed record SemanticProgramSlotDefinition
{
    public string SlotId { get; init; } = string.Empty;
    public string DefaultProgramId { get; init; } = string.Empty;
    public string? DefaultParameterSetId { get; init; }
}

public sealed record ModifierDefinition
{
    public string StatKey { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public JsonElement Value { get; init; }
    public int Priority { get; init; }
}

public sealed record InteractionFilterDefinition
{
    public string Team { get; init; } = "any";
    public IReadOnlyList<string> RequiredTags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExcludedTags { get; init; } = Array.Empty<string>();
    public int MinimumPower { get; init; }
    public int MaximumResistance { get; init; } = int.MaxValue;
}

public sealed record InteractionProfileDefinition
{
    public int SchemaVersion { get; init; } = 3;
    public string Id { get; init; } = string.Empty;
    public InteractionFilterDefinition Source { get; init; } = new();
    public InteractionFilterDefinition Target { get; init; } = new();
    public string ShapeTest { get; init; } = "swept";
    public int Priority { get; init; }
    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();
    public string? ConvertProjectileId { get; init; }
}

public sealed record ResourceHudDefinition
{
    public string Format { get; init; } = "number";
    public int Segments { get; init; }
}

public sealed record ResourceDefinition
{
    public int SchemaVersion { get; init; } = 3;
    public string Id { get; init; } = string.Empty;
    public string Scope { get; init; } = "run";
    public string ValueType { get; init; } = "number";
    public double Initial { get; init; }
    public double Minimum { get; init; }
    public double Maximum { get; init; } = double.MaxValue;
    public string ResetPolicy { get; init; } = "on-run-start";
    public string? Adapter { get; init; }
    public ResourceHudDefinition? Hud { get; init; }
}

public sealed record RuleActionDefinition
{
    public string Op { get; init; } = string.Empty;

    [JsonExtensionData]
    public IDictionary<string, JsonElement> Arguments { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}

public sealed record EventRuleDefinition
{
    public int SchemaVersion { get; init; } = 3;
    public string Id { get; init; } = string.Empty;
    public string Phase { get; init; } = "post-interaction";
    public string On { get; init; } = string.Empty;
    public int Priority { get; init; }
    public JsonElement? When { get; init; }
    public IReadOnlyList<RuleActionDefinition> Actions { get; init; } = Array.Empty<RuleActionDefinition>();
}

public sealed record StateTransitionDefinition
{
    public string Trigger { get; init; } = string.Empty;
    public string TargetStateId { get; init; } = string.Empty;
    public int Priority { get; init; }
    public JsonElement? When { get; init; }
    public JsonElement? ResourceCost { get; init; }
}

public sealed record StateDefinition
{
    public string Id { get; init; } = string.Empty;
    public int? DurationFrames { get; init; }
    public JsonElement? DrainPerTick { get; init; }
    public IReadOnlyList<ModifierDefinition> Modifiers { get; init; } = Array.Empty<ModifierDefinition>();
    public IReadOnlyList<string> AllowedActions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<RuleActionDefinition> EnterActions { get; init; } = Array.Empty<RuleActionDefinition>();
    public IReadOnlyList<RuleActionDefinition> TickActions { get; init; } = Array.Empty<RuleActionDefinition>();
    public IReadOnlyList<RuleActionDefinition> ExitActions { get; init; } = Array.Empty<RuleActionDefinition>();
    public IReadOnlyList<StateTransitionDefinition> Transitions { get; init; } = Array.Empty<StateTransitionDefinition>();
    public string? VisualCue { get; init; }
    public string? AudioCue { get; init; }
}

public sealed record StateMachineDefinition
{
    public int SchemaVersion { get; init; } = 3;
    public string Id { get; init; } = string.Empty;
    public string Scope { get; init; } = "player";
    public string ResourceId { get; init; } = string.Empty;
    public string InitialStateId { get; init; } = string.Empty;
    public string ActivationAction { get; init; } = "special";
    public IReadOnlyList<StateDefinition> States { get; init; } = Array.Empty<StateDefinition>();
}

public sealed record ActorDefinition
{
    public int SchemaVersion { get; init; } = 3;
    public string Id { get; init; } = string.Empty;
    public string EnemyId { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ActorPartDefinition> Parts { get; init; } = Array.Empty<ActorPartDefinition>();
}

public sealed record ActorPartDefinition
{
    public string Id { get; init; } = string.Empty;
    public string? ParentPartId { get; init; }
    public float OffsetX { get; init; }
    public float OffsetY { get; init; }
    public float RotationDegrees { get; init; }
    public string HealthPolicy { get; init; } = "shared";
    public int? MaximumHealth { get; init; }
    public float DamageForwardingRatio { get; init; } = 1;
    public bool Targetable { get; init; } = true;
    public int LockCapacity { get; init; } = 1;
    public bool Enabled { get; init; } = true;
    public string InteractionClass { get; init; } = "enemy";
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<HurtboxDefinition> Hurtboxes { get; init; } = Array.Empty<HurtboxDefinition>();
    public IReadOnlyList<HardpointDefinition> Hardpoints { get; init; } = Array.Empty<HardpointDefinition>();
    public string? DestroySignal { get; init; }
    public string? DetachSignal { get; init; }
    public string? VisualId { get; init; }
    public string? AnimationId { get; init; }
    public string? AnimationStateId { get; init; }
    public int RenderLayer { get; init; }
}

public sealed record HurtboxDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Shape { get; init; } = "circle";
    public float OffsetX { get; init; }
    public float OffsetY { get; init; }
    public float Radius { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }
    public float Length { get; init; }
    public float RotationDegrees { get; init; }
}

public sealed record HardpointDefinition
{
    public string Id { get; init; } = string.Empty;
    public float OffsetX { get; init; }
    public float OffsetY { get; init; }
    public float RotationDegrees { get; init; }
    public string? WeaponId { get; init; }
    public SemanticProgramSlotDefinition? ProgramSlot { get; init; }
}

public sealed record PartSignalDefinition
{
    public string? PartId { get; init; }
    public string? Tag { get; init; }
    public string Operation { get; init; } = "enable";
}

public sealed record StageProgramDefinition
{
    public int SchemaVersion { get; init; } = 3;
    public string Id { get; init; } = string.Empty;
    public long? EndFrame { get; init; }
    public IReadOnlyList<StageTrackDefinition> Tracks { get; init; } = Array.Empty<StageTrackDefinition>();
}

public sealed record StageTrackDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Clock { get; init; } = "run-frame";
    public IReadOnlyList<StageProgramEventDefinition> Events { get; init; } = Array.Empty<StageProgramEventDefinition>();
}

public sealed record StageProgramEventDefinition
{
    public string NodeId { get; init; } = string.Empty;
    public long Frame { get; init; }
    public string Op { get; init; } = string.Empty;
    public int DurationFrames { get; init; }
    public int TimeoutFrames { get; init; }
    public string? SignalId { get; init; }
    public string? WaitForSignalId { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement> Arguments { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}

public sealed record EffectRecipeDefinition
{
    public int SchemaVersion { get; init; } = 3;
    public string Id { get; init; } = string.Empty;
    public string On { get; init; } = string.Empty;
    public int Priority { get; init; }
    public JsonElement? When { get; init; }
    public IReadOnlyList<EffectRecipeActionDefinition> Actions { get; init; } =
        Array.Empty<EffectRecipeActionDefinition>();
}

public sealed record EffectRecipeActionDefinition
{
    public string Type { get; init; } = string.Empty;
    public string Kind { get; init; } = "hit";
    public int Count { get; init; } = 1;
    public float Radius { get; init; } = 4;
    public float Duration { get; init; } = 0.2f;
    public float Intensity { get; init; } = 1;
    public uint Tint { get; init; } = uint.MaxValue;
    public string Blend { get; init; } = "additive";
    public string? CueId { get; init; }
    public string? Pass { get; init; }
}

public sealed record AnimationStateDefinition
{
    public int SchemaVersion { get; init; } = 3;
    public string Id { get; init; } = string.Empty;
    public string DefaultState { get; init; } = "idle";
    public IReadOnlyDictionary<string, string> States { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
