using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using GoatShooooting.Core;
using GoatShooooting.Definitions;

namespace GoatShooooting.Runtime;

public enum ResourceScope { Run, Player, Stage, BossPhase }
public enum ResourceValueType { Number, Counter, Timer, Boolean }
public enum ResourceResetPolicy { OnRunStart, OnStageStart, OnBossPhase, Manual }
public enum StandardResourceAdapter { None, Score, Chain, Hit, Rank, Power, Life, Bomb, Gauge }

public sealed record CompiledResourceDefinition(
    ResourceHandle Handle,
    ResourceDefinition Definition,
    ResourceScope Scope,
    ResourceValueType ValueType,
    ResourceResetPolicy ResetPolicy,
    StandardResourceAdapter Adapter);

public readonly record struct ResourceValueSnapshot(ResourceHandle Handle, int ScopeKey, double Value);

/// <summary>Handle-addressed deterministic storage for run, player, stage, and boss-phase values.</summary>
public sealed class ScopedResourceStore
{
    private readonly CompiledResourceDefinition[] _definitions;
    private readonly Dictionary<ResourceKey, double> _values = new();

    public ScopedResourceStore(IEnumerable<CompiledResourceDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _definitions = definitions.OrderBy(static value => value.Handle.Value).ToArray();
        foreach (var definition in _definitions) _values.Add(new ResourceKey(definition.Handle, 0), definition.Definition.Initial);
    }

    public IReadOnlyList<CompiledResourceDefinition> Definitions => _definitions;
    public CompiledResourceDefinition GetDefinition(ResourceHandle handle) => Definition(handle);

    public double Get(ResourceHandle handle, int scopeKey = 0) =>
        _values.TryGetValue(Key(handle, scopeKey), out var value) ? value : Definition(handle).Definition.Initial;

    public bool GetBoolean(ResourceHandle handle, int scopeKey = 0) => Get(handle, scopeKey) != 0;

    public double Set(ResourceHandle handle, double value, int scopeKey = 0)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        var definition = Definition(handle);
        var normalized = definition.ValueType switch
        {
            ResourceValueType.Boolean => value == 0 ? 0 : 1,
            ResourceValueType.Counter => Math.Truncate(value),
            _ => value
        };
        normalized = Math.Clamp(normalized, definition.Definition.Minimum, definition.Definition.Maximum);
        _values[Key(handle, scopeKey)] = normalized;
        return normalized;
    }

    public double Add(ResourceHandle handle, double amount, int scopeKey = 0) =>
        Set(handle, checked(Get(handle, scopeKey) + amount), scopeKey);

    public bool TryConsume(ResourceHandle handle, double amount, int scopeKey = 0)
    {
        if (!double.IsFinite(amount) || amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var current = Get(handle, scopeKey);
        if (current < amount) return false;
        Set(handle, current - amount, scopeKey);
        return true;
    }

    public void Clamp(ResourceHandle handle, double minimum, double maximum, int scopeKey = 0)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum > maximum)
            throw new ArgumentOutOfRangeException(nameof(minimum));
        Set(handle, Math.Clamp(Get(handle, scopeKey), minimum, maximum), scopeKey);
    }

    public void Reset(ResourceResetPolicy policy, int? scopeKey = null)
    {
        foreach (var definition in _definitions.Where(value => value.ResetPolicy == policy))
        {
            if (scopeKey is { } selected)
            {
                _values[new ResourceKey(definition.Handle, selected)] = definition.Definition.Initial;
                continue;
            }
            var keys = _values.Keys.Where(key => key.Handle == definition.Handle).ToArray();
            if (keys.Length == 0) _values.Add(new ResourceKey(definition.Handle, 0), definition.Definition.Initial);
            foreach (var key in keys) _values[key] = definition.Definition.Initial;
        }
    }

    public IReadOnlyList<ResourceValueSnapshot> CaptureCanonicalSnapshot() => Array.AsReadOnly(_values
        .OrderBy(static pair => pair.Key.Handle.Value)
        .ThenBy(static pair => pair.Key.ScopeKey)
        .Select(static pair => new ResourceValueSnapshot(pair.Key.Handle, pair.Key.ScopeKey, pair.Value))
        .ToArray());

    internal void RestoreCheckpoint(IReadOnlyList<ResourceValueSnapshot> snapshot)
    {
        _values.Clear();
        foreach (var value in snapshot) _values.Add(new ResourceKey(value.Handle, value.ScopeKey), value.Value);
    }

    public IReadOnlyDictionary<string, ExpressionValue> CreateExpressionValues(int scopeKey = 0) =>
        new ReadOnlyDictionary<string, ExpressionValue>(_definitions.ToDictionary(
            static value => value.Definition.Id,
            value => value.ValueType == ResourceValueType.Boolean
                ? ExpressionValue.Boolean(Get(value.Handle, scopeKey) != 0)
                : ExpressionValue.Number(Get(value.Handle, scopeKey)),
            StringComparer.Ordinal));

    public void SynchronizeFromLegacy(RunState state, Entity player)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(player);
        foreach (var definition in _definitions)
        {
            var value = definition.Adapter switch
            {
                StandardResourceAdapter.Score => state.Score,
                StandardResourceAdapter.Chain => state.Chain,
                StandardResourceAdapter.Hit => state.HitCombo,
                StandardResourceAdapter.Rank => state.Rank,
                StandardResourceAdapter.Power => player.Get<ShipComponent>().Power,
                StandardResourceAdapter.Life => player.Get<LivesComponent>().Remaining,
                StandardResourceAdapter.Bomb => player.Get<BombComponent>().Remaining,
                StandardResourceAdapter.Gauge => state.SpecialGaugeValue,
                _ => Get(definition.Handle)
            };
            Set(definition.Handle, value);
        }
    }

    public void SynchronizeToLegacy(RunState state, Entity player)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(player);
        foreach (var definition in _definitions)
        {
            var value = Get(definition.Handle);
            switch (definition.Adapter)
            {
                case StandardResourceAdapter.Score:
                    state.SetScoreFromResource(value);
                    break;
                case StandardResourceAdapter.Chain:
                    state.Chain = checked((int)value);
                    break;
                case StandardResourceAdapter.Hit:
                    state.HitCombo = checked((int)value);
                    break;
                case StandardResourceAdapter.Rank:
                    state.Rank = value;
                    break;
                case StandardResourceAdapter.Power:
                    player.Get<ShipComponent>().Power = checked((int)value);
                    state.Power = checked((int)value);
                    break;
                case StandardResourceAdapter.Life:
                    player.Get<LivesComponent>().Remaining = checked((int)value);
                    break;
                case StandardResourceAdapter.Bomb:
                    player.Get<BombComponent>().Remaining = checked((int)value);
                    break;
                case StandardResourceAdapter.Gauge:
                    state.SpecialGaugeValue = value;
                    state.Gauge = Math.Max(0, checked((int)Math.Ceiling(value)));
                    break;
            }
        }
    }

    private ResourceKey Key(ResourceHandle handle, int scopeKey)
    {
        var definition = Definition(handle);
        if (scopeKey < 0) throw new ArgumentOutOfRangeException(nameof(scopeKey));
        return new ResourceKey(handle, definition.Scope == ResourceScope.Player ? scopeKey : 0);
    }

    private CompiledResourceDefinition Definition(ResourceHandle handle) =>
        (uint)handle.Value < (uint)_definitions.Length && _definitions[handle.Value].Handle == handle
            ? _definitions[handle.Value]
            : _definitions.FirstOrDefault(value => value.Handle == handle)
                ?? throw new ArgumentOutOfRangeException(nameof(handle));

    private readonly record struct ResourceKey(ResourceHandle Handle, int ScopeKey);
}

public enum RulePhase { PreInput, PostInteraction }

public enum RuleCommandKind
{
    AddResource,
    SetResource,
    ClampResource,
    ConsumeResource,
    AwardScore,
    SpawnItem,
    SpawnActor,
    SpawnProjectile,
    CancelProjectiles,
    ConvertProjectiles,
    RequestStateTransition,
    AddModifier,
    RemoveModifier,
    EmitGameplaySignal,
    EmitPresentationSignal,
    SetRouteFlag,
    EmitAchievementCandidate
}

public sealed record RuleCommand(
    RuleCommandKind Kind,
    string SourceDefinitionId,
    int ActionIndex,
    ResourceHandle? ResourceHandle,
    double Value,
    double Minimum,
    double Maximum,
    string? TargetId,
    string? SecondaryId,
    IReadOnlyDictionary<string, JsonElement> Arguments);

public sealed record CompiledEventRule(
    EventRuleHandle Handle,
    EventRuleDefinition Definition,
    RulePhase Phase,
    EventFactDescriptor Event,
    CompiledExpression? Condition,
    IReadOnlyList<CompiledRuleAction> Actions);

public sealed class EventRuleReducer
{
    private readonly IReadOnlyList<CompiledEventRule> _rules;

    public EventRuleReducer(IEnumerable<CompiledEventRule> rules) =>
        _rules = Array.AsReadOnly((rules ?? throw new ArgumentNullException(nameof(rules)))
            .OrderBy(static value => value.Definition.Priority)
            .ThenBy(static value => value.Definition.Id, StringComparer.Ordinal)
            .ToArray());

    public bool IsEmpty => _rules.Count == 0;
    public bool HasPreInputRules => _rules.Any(static value => value.Phase == RulePhase.PreInput);

    public IReadOnlyList<RuleCommand> Reduce(
        RulePhase phase,
        IEnumerable<IGameplayEvent> facts,
        ScopedResourceStore resources,
        int scopeKey = 0)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(resources);
        if (_rules.Count == 0) return Array.Empty<RuleCommand>();
        var snapshot = facts.OrderBy(static value => value.Sequence).ToArray();
        var commands = new List<RuleCommand>();
        foreach (var rule in _rules.Where(value => value.Phase == phase))
        {
            foreach (var fact in snapshot)
            {
                if (!rule.Event.EventType.IsInstanceOfType(fact)) continue;
                var context = new ExpressionEvaluationContext
                {
                    Resources = resources.CreateExpressionValues(scopeKey),
                    Events = rule.Event.Read(fact)
                };
                if (rule.Condition is not null && !rule.Condition.Evaluate(context).BooleanValue) continue;
                foreach (var action in rule.Actions) commands.Add(action.Evaluate(context, rule.Definition.Id));
            }
        }
        return Array.AsReadOnly(commands.ToArray());
    }
}

public sealed record EventFactDescriptor(
    string Id,
    Type EventType,
    IReadOnlyDictionary<string, ExpressionValueType> Fields,
    Func<IGameplayEvent, IReadOnlyDictionary<string, ExpressionValue>> Read);

internal static class EventFactRegistry
{
    private static readonly IReadOnlyDictionary<string, EventFactDescriptor> Descriptors = Build();

    public static EventFactDescriptor Resolve(string id, string path) =>
        Descriptors.TryGetValue(id, out var descriptor) ? descriptor :
        throw new DefinitionValidationException($"{path} references unknown gameplay event '{id}'.");

    private static IReadOnlyDictionary<string, EventFactDescriptor> Build()
    {
        var eventType = typeof(IGameplayEvent);
        var values = eventType.Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && eventType.IsAssignableFrom(type))
            .Select(Create)
            .ToDictionary(static value => value.Id, StringComparer.Ordinal);
        return new ReadOnlyDictionary<string, EventFactDescriptor>(values);
    }

    private static EventFactDescriptor Create(Type type)
    {
        var id = Kebab(type.Name.EndsWith("Event", StringComparison.Ordinal) ? type.Name[..^5] : type.Name);
        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static property => property.Name is not (nameof(IGameplayEvent.Frame) or nameof(IGameplayEvent.Sequence)))
            .Select(property => (Property: property, Type: Map(property.PropertyType)))
            .Where(static value => value.Type is not null)
            .ToArray();
        var fields = new ReadOnlyDictionary<string, ExpressionValueType>(properties.ToDictionary(
            static value => Camel(value.Property.Name), static value => value.Type!.Value, StringComparer.Ordinal));
        return new EventFactDescriptor(id, type, fields, value =>
        {
            var result = new Dictionary<string, ExpressionValue>(StringComparer.Ordinal);
            foreach (var property in properties)
            {
                var raw = property.Property.GetValue(value);
                result.Add(Camel(property.Property.Name), Convert(raw, property.Type!.Value));
            }
            return new ReadOnlyDictionary<string, ExpressionValue>(result);
        });
    }

    private static ExpressionValueType? Map(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(bool)) return ExpressionValueType.Boolean;
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return ExpressionValueType.Number;
        if (type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long))
            return ExpressionValueType.Integer;
        if (type == typeof(string) || type.IsEnum) return ExpressionValueType.Id;
        return null;
    }

    private static ExpressionValue Convert(object? value, ExpressionValueType type) => type switch
    {
        ExpressionValueType.Boolean => ExpressionValue.Boolean(value is true),
        ExpressionValueType.Number => ExpressionValue.Number(value is null ? 0 : System.Convert.ToDouble(value)),
        ExpressionValueType.Integer => ExpressionValue.Integer(value is null ? 0 : System.Convert.ToInt64(value)),
        ExpressionValueType.Id => ExpressionValue.Id(
            string.IsNullOrWhiteSpace(value?.ToString()) ? "none" : value.ToString()!),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static string Camel(string value) => char.ToLowerInvariant(value[0]) + value[1..];

    private static string Kebab(string value)
    {
        var result = new System.Text.StringBuilder();
        for (var index = 0; index < value.Length; index++)
        {
            if (index > 0 && char.IsUpper(value[index])) result.Append('-');
            result.Append(char.ToLowerInvariant(value[index]));
        }
        return result.ToString();
    }
}

public sealed class CompiledRuleAction
{
    private readonly CompiledExpression? _value;
    private readonly CompiledExpression? _minimum;
    private readonly CompiledExpression? _maximum;

    internal CompiledRuleAction(
        RuleCommandKind kind,
        int actionIndex,
        ResourceHandle? resourceHandle,
        CompiledExpression? value,
        CompiledExpression? minimum,
        CompiledExpression? maximum,
        string? targetId,
        string? secondaryId,
        IReadOnlyDictionary<string, JsonElement> arguments)
    {
        Kind = kind;
        ActionIndex = actionIndex;
        ResourceHandle = resourceHandle;
        _value = value;
        _minimum = minimum;
        _maximum = maximum;
        TargetId = targetId;
        SecondaryId = secondaryId;
        Arguments = arguments;
    }

    public RuleCommandKind Kind { get; }
    public int ActionIndex { get; }
    public ResourceHandle? ResourceHandle { get; }
    public string? TargetId { get; }
    public string? SecondaryId { get; }
    public IReadOnlyDictionary<string, JsonElement> Arguments { get; }

    public RuleCommand Evaluate(ExpressionEvaluationContext context, string sourceDefinitionId) => new(
        Kind,
        sourceDefinitionId,
        ActionIndex,
        ResourceHandle,
        _value?.Evaluate(context).NumberValue ?? 0,
        _minimum?.Evaluate(context).NumberValue ?? double.MinValue,
        _maximum?.Evaluate(context).NumberValue ?? double.MaxValue,
        TargetId,
        SecondaryId,
        Arguments);
}

internal static class EventRuleCompiler
{
    public static IReadOnlyList<CompiledEventRule> Compile(
        DefinitionCatalog definitions,
        IReadOnlyDictionary<string, ResourceHandle> resourceHandles,
        IReadOnlyDictionary<string, EventRuleHandle> ruleHandles)
    {
        var resourceTypes = definitions.Resources.Values.ToDictionary(
            static value => value.Id,
            static value => value.ValueType == "boolean" ? ExpressionValueType.Boolean : ExpressionValueType.Number,
            StringComparer.Ordinal);
        foreach (var standard in ResourceCompiler.StandardIds) resourceTypes.TryAdd(standard, ExpressionValueType.Number);
        return Array.AsReadOnly(definitions.EventRules.Values
            .OrderBy(static value => value.Id, StringComparer.Ordinal)
            .Select(definition =>
            {
                var descriptor = EventFactRegistry.Resolve(definition.On, $"rules/{definition.Id}.json $.on");
                var bindings = CreateBindings(resourceTypes, descriptor.Fields);
                var condition = definition.When is not { } when ? null : ExpressionCompiler.Compile(
                    when, ExpressionValueType.Boolean, CompiledParameterSchema.Empty, bindings,
                    $"rules/{definition.Id}.json $.when");
                var actions = RuleActionCompiler.Compile(
                    definition.Actions, definitions, resourceHandles, bindings, $"rules/{definition.Id}.json $.actions");
                return new CompiledEventRule(
                    ruleHandles[definition.Id], definition,
                    definition.Phase == "pre-input" ? RulePhase.PreInput : RulePhase.PostInteraction,
                    descriptor, condition, actions);
            }).ToArray());
    }

    internal static ExpressionBindingSchema CreateBindings(
        IReadOnlyDictionary<string, ExpressionValueType> resources,
        IReadOnlyDictionary<string, ExpressionValueType>? events = null)
    {
        var bindings = new ExpressionBindingSchema();
        foreach (var resource in resources) bindings.AddResource(resource.Key, resource.Value);
        if (events is not null) foreach (var field in events) bindings.AddEvent(field.Key, field.Value);
        bindings.AddContext("frame", ExpressionValueType.Integer)
            .AddContext("stateElapsed", ExpressionValueType.Integer);
        return bindings;
    }
}

internal static class RuleActionCompiler
{
    public static IReadOnlyList<CompiledRuleAction> Compile(
        IReadOnlyList<RuleActionDefinition> definitions,
        DefinitionCatalog catalog,
        IReadOnlyDictionary<string, ResourceHandle> resourceHandles,
        ExpressionBindingSchema bindings,
        string path)
    {
        return Array.AsReadOnly(definitions.Select((definition, index) =>
        {
            var actionPath = $"{path}[{index}]";
            var kind = ParseKind(definition.Op);
            ResourceHandle? resource = null;
            if (kind is RuleCommandKind.AddResource or RuleCommandKind.SetResource or
                RuleCommandKind.ClampResource or RuleCommandKind.ConsumeResource)
                resource = resourceHandles[RequiredString(definition, "resourceId", actionPath)];
            var value = kind switch
            {
                RuleCommandKind.AddResource or RuleCommandKind.SetResource or RuleCommandKind.ConsumeResource =>
                    CompileNumber(definition, "value", bindings, actionPath),
                RuleCommandKind.AwardScore => CompileNumber(definition, "base", bindings, actionPath),
                RuleCommandKind.SpawnItem or RuleCommandKind.SpawnActor or RuleCommandKind.SpawnProjectile =>
                    CompileOptionalNumber(definition, "x", bindings, actionPath),
                _ => null
            };
            var minimum = kind == RuleCommandKind.ClampResource
                ? CompileNumber(definition, "minimum", bindings, actionPath)
                : kind == RuleCommandKind.AwardScore
                    ? CompileOptionalNumber(definition, "multiplier", bindings, actionPath)
                    : kind is RuleCommandKind.SpawnItem or RuleCommandKind.SpawnActor or RuleCommandKind.SpawnProjectile
                        ? CompileOptionalNumber(definition, "y", bindings, actionPath) : null;
            var maximum = kind == RuleCommandKind.ClampResource
                ? CompileNumber(definition, "maximum", bindings, actionPath)
                : kind == RuleCommandKind.SpawnProjectile
                    ? CompileOptionalNumber(definition, "angleDegrees", bindings, actionPath) : null;
            var target = kind switch
            {
                RuleCommandKind.RequestStateTransition => RequiredString(definition, "stateMachineId", actionPath),
                RuleCommandKind.SpawnItem => RequiredString(definition, "itemId", actionPath),
                RuleCommandKind.SpawnActor => RequiredString(definition, "actorId", actionPath),
                RuleCommandKind.SpawnProjectile or RuleCommandKind.ConvertProjectiles =>
                    RequiredString(definition, "projectileId", actionPath),
                RuleCommandKind.EmitGameplaySignal or RuleCommandKind.EmitPresentationSignal =>
                    RequiredString(definition, "signalId", actionPath),
                RuleCommandKind.SetRouteFlag => RequiredString(definition, "flagId", actionPath),
                RuleCommandKind.EmitAchievementCandidate => RequiredString(definition, "achievementId", actionPath),
                RuleCommandKind.AddModifier or RuleCommandKind.RemoveModifier =>
                    RequiredString(definition, "modifierId", actionPath),
                _ => null
            };
            var secondary = kind == RuleCommandKind.RequestStateTransition
                ? RequiredString(definition, "targetStateId", actionPath)
                : kind == RuleCommandKind.AwardScore ? OptionalString(definition, "category") ?? "rule" : null;
            return new CompiledRuleAction(kind, index, resource, value, minimum, maximum, target, secondary,
                new ReadOnlyDictionary<string, JsonElement>(new Dictionary<string, JsonElement>(definition.Arguments, StringComparer.Ordinal)));
        }).ToArray());
    }

    private static CompiledExpression CompileNumber(
        RuleActionDefinition definition,
        string name,
        ExpressionBindingSchema bindings,
        string path) => definition.Arguments.TryGetValue(name, out var value)
            ? ExpressionCompiler.Compile(value, ExpressionValueType.Number, CompiledParameterSchema.Empty, bindings, $"{path}.{name}")
            : throw new DefinitionValidationException($"{path} requires '{name}'.");

    private static CompiledExpression? CompileOptionalNumber(
        RuleActionDefinition definition,
        string name,
        ExpressionBindingSchema bindings,
        string path) => definition.Arguments.TryGetValue(name, out var value)
            ? ExpressionCompiler.Compile(value, ExpressionValueType.Number, CompiledParameterSchema.Empty, bindings, $"{path}.{name}")
            : null;

    private static string RequiredString(RuleActionDefinition definition, string name, string path) =>
        OptionalString(definition, name) ?? throw new DefinitionValidationException($"{path} requires string '{name}'.");

    private static string? OptionalString(RuleActionDefinition definition, string name) =>
        definition.Arguments.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : null;

    private static RuleCommandKind ParseKind(string op) => op switch
    {
        "add-resource" => RuleCommandKind.AddResource,
        "set-resource" => RuleCommandKind.SetResource,
        "clamp-resource" => RuleCommandKind.ClampResource,
        "consume-resource" => RuleCommandKind.ConsumeResource,
        "award-score" => RuleCommandKind.AwardScore,
        "spawn-item" => RuleCommandKind.SpawnItem,
        "spawn-actor" => RuleCommandKind.SpawnActor,
        "spawn-projectile" => RuleCommandKind.SpawnProjectile,
        "cancel-projectiles" => RuleCommandKind.CancelProjectiles,
        "convert-projectiles" => RuleCommandKind.ConvertProjectiles,
        "request-state-transition" => RuleCommandKind.RequestStateTransition,
        "add-modifier" => RuleCommandKind.AddModifier,
        "remove-modifier" => RuleCommandKind.RemoveModifier,
        "emit-gameplay-signal" => RuleCommandKind.EmitGameplaySignal,
        "emit-presentation-signal" => RuleCommandKind.EmitPresentationSignal,
        "set-route-flag" => RuleCommandKind.SetRouteFlag,
        "emit-achievement-candidate" => RuleCommandKind.EmitAchievementCandidate,
        _ => throw new DefinitionValidationException($"Unsupported rule action '{op}'.")
    };
}

internal static class ResourceCompiler
{
    public static readonly string[] StandardIds = ["score", "chain", "hit", "rank", "power", "life", "bomb", "gauge"];

    public static IReadOnlyList<ResourceDefinition> AddStandardResources(IEnumerable<ResourceDefinition> authored)
    {
        var result = authored.ToDictionary(static value => value.Id, StringComparer.Ordinal);
        foreach (var id in StandardIds)
        {
            if (result.ContainsKey(id)) continue;
            result.Add(id, new ResourceDefinition
            {
                Id = id,
                Scope = id is "power" or "life" or "bomb" or "gauge" ? "player" : "run",
                ValueType = id is "chain" or "hit" or "power" or "life" or "bomb" or "gauge" ? "counter" : "number",
                Minimum = 0,
                Maximum = id == "score" ? 9_223_372_036_854_775_000d : int.MaxValue,
                Adapter = id
            });
        }
        return Array.AsReadOnly(result.Values.OrderBy(static value => value.Id, StringComparer.Ordinal).ToArray());
    }

    public static CompiledResourceDefinition Compile(ResourceDefinition definition, ResourceHandle handle) => new(
        handle,
        definition,
        definition.Scope switch
        {
            "run" => ResourceScope.Run,
            "player" => ResourceScope.Player,
            "stage" => ResourceScope.Stage,
            "boss-phase" => ResourceScope.BossPhase,
            _ => throw new DefinitionValidationException($"Resource '{definition.Id}' has unknown scope.")
        },
        definition.ValueType switch
        {
            "number" => ResourceValueType.Number,
            "counter" => ResourceValueType.Counter,
            "timer" => ResourceValueType.Timer,
            "boolean" => ResourceValueType.Boolean,
            _ => throw new DefinitionValidationException($"Resource '{definition.Id}' has unknown value type.")
        },
        definition.ResetPolicy switch
        {
            "on-run-start" => ResourceResetPolicy.OnRunStart,
            "on-stage-start" => ResourceResetPolicy.OnStageStart,
            "on-boss-phase" => ResourceResetPolicy.OnBossPhase,
            "manual" => ResourceResetPolicy.Manual,
            _ => throw new DefinitionValidationException($"Resource '{definition.Id}' has unknown reset policy.")
        },
        Enum.TryParse<StandardResourceAdapter>(definition.Adapter ??
            (StandardIds.Contains(definition.Id, StringComparer.Ordinal) ? definition.Id : "None"), true, out var adapter)
            ? adapter : StandardResourceAdapter.None);
}

public sealed record CompiledStateTransition(
    string Trigger,
    StateHandle Target,
    int Priority,
    CompiledExpression? Condition,
    CompiledExpression? ResourceCost);

public sealed record CompiledStateDefinition(
    StateHandle Handle,
    StateDefinition Definition,
    CompiledExpression? DrainPerTick,
    CompiledModifierSet Modifiers,
    IReadOnlyList<CompiledRuleAction> EnterActions,
    IReadOnlyList<CompiledRuleAction> TickActions,
    IReadOnlyList<CompiledRuleAction> ExitActions,
    IReadOnlyList<CompiledStateTransition> Transitions);

public sealed record CompiledStateMachine(
    StateMachineHandle Handle,
    StateMachineDefinition Definition,
    ResourceHandle Resource,
    StateHandle InitialState,
    IReadOnlyList<CompiledStateDefinition> States);

internal static class StateMachineCompiler
{
    public static IReadOnlyList<CompiledStateMachine> Compile(
        DefinitionCatalog definitions,
        IReadOnlyDictionary<string, ResourceHandle> resourceHandles,
        IReadOnlyDictionary<string, StateMachineHandle> machineHandles)
    {
        var resourceTypes = ResourceCompiler.AddStandardResources(definitions.Resources.Values).ToDictionary(
            static value => value.Id,
            static value => value.ValueType == "boolean" ? ExpressionValueType.Boolean : ExpressionValueType.Number,
            StringComparer.Ordinal);
        var bindings = EventRuleCompiler.CreateBindings(resourceTypes);
        return Array.AsReadOnly(definitions.StateMachines.Values.OrderBy(static value => value.Id, StringComparer.Ordinal)
            .Select(machine =>
            {
                var stateHandles = machine.States.OrderBy(static value => value.Id, StringComparer.Ordinal)
                    .Select((value, index) => (value.Id, Handle: new StateHandle(index)))
                    .ToDictionary(static value => value.Id, static value => value.Handle, StringComparer.Ordinal);
                var states = machine.States.OrderBy(static value => value.Id, StringComparer.Ordinal).Select(state =>
                {
                    CompiledExpression? OptionalNumber(JsonElement? value, string path) =>
                        value is not { } present ? null : ExpressionCompiler.Compile(
                            present, ExpressionValueType.Number, CompiledParameterSchema.Empty, bindings, path);
                    var prefix = $"state-machines/{machine.Id}.json $.states.{state.Id}";
                    var transitions = state.Transitions.OrderBy(static value => value.Priority)
                        .ThenBy(static value => value.TargetStateId, StringComparer.Ordinal)
                        .Select(transition => new CompiledStateTransition(
                            transition.Trigger,
                            stateHandles[transition.TargetStateId],
                            transition.Priority,
                            transition.When is not { } when ? null : ExpressionCompiler.Compile(
                                when, ExpressionValueType.Boolean, CompiledParameterSchema.Empty, bindings,
                                $"{prefix}.transitions.when"),
                            OptionalNumber(transition.ResourceCost, $"{prefix}.transitions.resourceCost")))
                        .ToArray();
                    return new CompiledStateDefinition(
                        stateHandles[state.Id], state,
                        OptionalNumber(state.DrainPerTick, $"{prefix}.drainPerTick"),
                        ModifierCompiler.Compile($"{machine.Id}/{state.Id}", state.Modifiers,
                            ModifierSourceTier.State, scope: machine.Scope, sourceState: state.Id,
                            path: $"{prefix}.modifiers"),
                        RuleActionCompiler.Compile(state.EnterActions, definitions, resourceHandles, bindings, $"{prefix}.enterActions"),
                        RuleActionCompiler.Compile(state.TickActions, definitions, resourceHandles, bindings, $"{prefix}.tickActions"),
                        RuleActionCompiler.Compile(state.ExitActions, definitions, resourceHandles, bindings, $"{prefix}.exitActions"),
                        Array.AsReadOnly(transitions));
                }).ToArray();
                return new CompiledStateMachine(machineHandles[machine.Id], machine, resourceHandles[machine.ResourceId],
                    stateHandles[machine.InitialStateId], Array.AsReadOnly(states));
            }).ToArray());
    }
}

public readonly record struct StateMachineSnapshot(
    StateMachineHandle Machine,
    int ScopeKey,
    StateHandle State,
    long EnteredFrame,
    long StateTicks);

internal readonly record struct StateMachineCheckpoint(
    StateMachineHandle Machine,
    int ScopeKey,
    StateHandle State,
    long EnteredFrame,
    long StateTicks,
    bool WasActivationPressed);

/// <summary>Deterministic concurrent state-machine runtime with upgrade and interrupt transitions.</summary>
public sealed class StateMachineSystem
{
    private static readonly IReadOnlyList<CompiledModifierSet> NoModifiers = Array.Empty<CompiledModifierSet>();
    private readonly CompiledStateMachine[] _machines;
    private readonly Dictionary<(StateMachineHandle Machine, int ScopeKey), RuntimeState> _states = new();
    private readonly List<(StateMachineHandle Machine, string TargetStateId, int ScopeKey)> _requests = new();

    public StateMachineSystem(IEnumerable<CompiledStateMachine> machines)
    {
        _machines = (machines ?? throw new ArgumentNullException(nameof(machines)))
            .OrderBy(static value => value.Handle.Value).ToArray();
        foreach (var machine in _machines)
            if (machine.Definition.Scope != "player")
                _states.Add((machine.Handle, 0), new RuntimeState(machine.InitialState, 0, 0));
    }

    public bool IsEmpty => _machines.Length == 0;

    public void Request(StateMachineHandle machine, string targetStateId, int scopeKey = 0) =>
        _requests.Add((machine, targetStateId, scopeKey));

    public void Reset(ResourceScope scope, long frame = 0, int scopeKey = 0)
    {
        foreach (var machine in _machines.Where(value => ParseScope(value.Definition.Scope) == scope))
        {
            var key = (machine.Handle, scope == ResourceScope.Player ? scopeKey : 0);
            _states[key] = new RuntimeState(machine.InitialState, frame, 0);
        }
    }

    public IReadOnlyList<RuleCommand> Update(
        long frame,
        InputFrame input,
        IReadOnlyList<IGameplayEvent> facts,
        ScopedResourceStore resources,
        int scopeKey = 0)
    {
        if (_machines.Length == 0) return Array.Empty<RuleCommand>();
        var output = new List<RuleCommand>();
        foreach (var machine in _machines)
        {
            var key = (machine.Handle, machine.Definition.Scope == "player" ? scopeKey : 0);
            if (!_states.TryGetValue(key, out var runtime))
            {
                runtime = new RuntimeState(machine.InitialState, frame, 0);
                _states.Add(key, runtime);
            }
            var state = machine.States[runtime.State.Value];
            var context = CreateContext(resources, frame, runtime.StateTicks, key.Item2);
            foreach (var action in state.TickActions) output.Add(action.Evaluate(context, $"{machine.Definition.Id}/{state.Definition.Id}"));
            if (state.DrainPerTick is not null)
                resources.Add(machine.Resource, -Math.Max(0, state.DrainPerTick.Evaluate(context).NumberValue), key.Item2);
            var requestedTarget = _requests.FirstOrDefault(value => value.Machine == machine.Handle && value.ScopeKey == key.Item2);
            var activationPressed = machine.Definition.ActivationAction switch
            {
                "special" => input.IsPressed(InputButtons.Special),
                "bomb" => input.IsPressed(InputButtons.Bomb),
                _ => false
            };
            var trigger = Trigger(
                machine, state, runtime, activationPressed && !runtime.WasActivationPressed,
                facts, resources, requestedTarget.TargetStateId, key.Item2);
            var transition = state.Transitions.FirstOrDefault(value =>
                value.Trigger == trigger &&
                (requestedTarget.TargetStateId is null || machine.States[value.Target.Value].Definition.Id == requestedTarget.TargetStateId) &&
                (value.Condition is null || value.Condition.Evaluate(context).BooleanValue));
            if (transition is not null)
            {
                var cost = transition.ResourceCost?.Evaluate(context).NumberValue ?? 0;
                if (cost >= 0 && resources.TryConsume(machine.Resource, cost, key.Item2))
                {
                    foreach (var action in state.ExitActions) output.Add(action.Evaluate(context, $"{machine.Definition.Id}/{state.Definition.Id}"));
                    var target = machine.States[transition.Target.Value];
                    var targetContext = CreateContext(resources, frame, 0, key.Item2);
                    foreach (var action in target.EnterActions) output.Add(action.Evaluate(targetContext, $"{machine.Definition.Id}/{target.Definition.Id}"));
                    _states[key] = new RuntimeState(target.Handle, frame, 0, activationPressed);
                    continue;
                }
            }
            _states[key] = runtime with { StateTicks = runtime.StateTicks + 1, WasActivationPressed = activationPressed };
        }
        _requests.Clear();
        return Array.AsReadOnly(output.ToArray());
    }

    public IReadOnlyList<CompiledModifierSet> ActiveModifiers(int scopeKey = 0) => _machines.Length == 0
        ? NoModifiers
        : Array.AsReadOnly(_machines
        .Select(machine =>
        {
            var key = (machine.Handle, machine.Definition.Scope == "player" ? scopeKey : 0);
            var runtime = GetOrCreate(machine, key, 0);
            return machine.States[runtime.State.Value].Modifiers;
        })
        .Where(static value => value.Modifiers.Count > 0)
        .ToArray());

    public bool IsActionAllowed(string action, int scopeKey = 0) => _machines.All(machine =>
    {
        var key = (machine.Handle, machine.Definition.Scope == "player" ? scopeKey : 0);
        var allowed = machine.States[GetOrCreate(machine, key, 0).State.Value].Definition.AllowedActions;
        return allowed.Count == 0 || allowed.Contains(action, StringComparer.Ordinal);
    });

    public IReadOnlyList<StateMachineSnapshot> CaptureCanonicalSnapshot() => Array.AsReadOnly(_states
        .OrderBy(static value => value.Key.Machine.Value).ThenBy(static value => value.Key.ScopeKey)
        .Select(static value => new StateMachineSnapshot(
            value.Key.Machine, value.Key.ScopeKey, value.Value.State, value.Value.EnteredFrame, value.Value.StateTicks))
        .ToArray());

    internal IReadOnlyList<StateMachineCheckpoint> CaptureCheckpoint() => Array.AsReadOnly(_states
        .OrderBy(static value => value.Key.Machine.Value).ThenBy(static value => value.Key.ScopeKey)
        .Select(static value => new StateMachineCheckpoint(
            value.Key.Machine, value.Key.ScopeKey, value.Value.State, value.Value.EnteredFrame,
            value.Value.StateTicks, value.Value.WasActivationPressed))
        .ToArray());

    internal void RestoreCheckpoint(IReadOnlyList<StateMachineCheckpoint> snapshot)
    {
        _states.Clear();
        _requests.Clear();
        foreach (var value in snapshot)
            _states.Add((value.Machine, value.ScopeKey), new RuntimeState(
                value.State, value.EnteredFrame, value.StateTicks, value.WasActivationPressed));
    }

    private static string? Trigger(
        CompiledStateMachine machine,
        CompiledStateDefinition state,
        RuntimeState runtime,
        bool activationPressed,
        IReadOnlyList<IGameplayEvent> facts,
        ScopedResourceStore resources,
        string? requestedTarget,
        int scopeKey)
    {
        if (requestedTarget is not null) return "rule";
        if (facts.Any(static value => value is PlayerDiedEvent)) return "player-died";
        if (facts.Any(static value => value is BombUsedEvent)) return "bomb-used";
        if (resources.Get(machine.Resource, scopeKey) <= 0) return "resource-empty";
        if (state.Definition.DurationFrames is { } duration && runtime.StateTicks >= duration) return "timer-elapsed";
        if (activationPressed) return "request";
        return state.Transitions.Any(static value => value.Trigger == "automatic") ? "automatic" : null;
    }

    private static ExpressionEvaluationContext CreateContext(
        ScopedResourceStore resources,
        long frame,
        long stateElapsed,
        int scopeKey) => new()
        {
            Resources = resources.CreateExpressionValues(scopeKey),
            Context = new ReadOnlyDictionary<string, ExpressionValue>(new Dictionary<string, ExpressionValue>
            {
                ["frame"] = ExpressionValue.Integer(frame),
                ["stateElapsed"] = ExpressionValue.Integer(stateElapsed)
            })
        };

    private RuntimeState GetOrCreate(
        CompiledStateMachine machine,
        (StateMachineHandle Machine, int ScopeKey) key,
        long frame)
    {
        if (_states.TryGetValue(key, out var runtime)) return runtime;
        runtime = new RuntimeState(machine.InitialState, frame, 0);
        _states.Add(key, runtime);
        return runtime;
    }

    private static ResourceScope ParseScope(string scope) => scope switch
    {
        "run" => ResourceScope.Run,
        "player" => ResourceScope.Player,
        "stage" => ResourceScope.Stage,
        "boss-phase" => ResourceScope.BossPhase,
        _ => throw new DefinitionValidationException($"Unknown state-machine scope '{scope}'.")
    };

    private sealed record RuntimeState(StateHandle State, long EnteredFrame, long StateTicks, bool WasActivationPressed = false);
}

public sealed record RuleSignalEvent(
    long Frame,
    int Sequence,
    string SignalId,
    string SourceDefinitionId,
    bool PresentationOnly) : IGameplayEvent;

public static class RuleCommandExecutor
{
    public static IReadOnlyList<RuleCommand> ApplyCore(
        IEnumerable<RuleCommand> commands,
        ScopedResourceStore resources,
        RunState state,
        GameEventBuffer events,
        StateMachineSystem? stateMachines = null,
        CompiledCatalog? catalog = null,
        int scopeKey = 0,
        double scoreMultiplier = 1)
    {
        var deferred = new List<RuleCommand>();
        foreach (var command in commands)
        {
            events.Publish((frame, sequence) => new RuleCommandAppliedEvent(
                frame,
                sequence,
                command.SourceDefinitionId,
                command.ActionIndex,
                command.Kind.ToString(),
                command.TargetId,
                command.ResourceHandle is { } resource && catalog is not null
                    ? catalog.Get(resource).Definition.Id : null,
                command.Value));
            switch (command.Kind)
            {
                case RuleCommandKind.AddResource:
                    resources.Add(command.ResourceHandle!.Value, command.Value, scopeKey);
                    break;
                case RuleCommandKind.SetResource:
                    resources.Set(command.ResourceHandle!.Value, command.Value, scopeKey);
                    break;
                case RuleCommandKind.ClampResource:
                    resources.Clamp(command.ResourceHandle!.Value, command.Minimum, command.Maximum, scopeKey);
                    break;
                case RuleCommandKind.ConsumeResource:
                    _ = resources.TryConsume(command.ResourceHandle!.Value, Math.Max(0, command.Value), scopeKey);
                    break;
                case RuleCommandKind.AwardScore:
                    var multiplier = command.Minimum == double.MinValue ? 1 : command.Minimum;
                    multiplier = double.IsFinite(multiplier) && multiplier > 0 && double.IsFinite(scoreMultiplier) && scoreMultiplier > 0
                        ? Math.Min(1_000_000, multiplier * scoreMultiplier) : 1;
                    var scaled = command.Value * multiplier;
                    var requested = scaled <= 0 ? 0 : scaled >= long.MaxValue ? long.MaxValue : (long)Math.Floor(scaled);
                    var awarded = state.AwardScore(command.SecondaryId ?? "rule", requested);
                    if (catalog is not null)
                        resources.Set(catalog.ResolveResource("score"), state.Score, scopeKey);
                    if (awarded > 0)
                        events.Publish((frame, sequence) => new ScoreAwardedEvent(
                            frame, sequence, command.SourceDefinitionId,
                            command.Value >= long.MaxValue ? long.MaxValue : Math.Max(0, (long)Math.Floor(command.Value)),
                            multiplier, awarded,
                            command.SourceDefinitionId, command.SecondaryId ?? "rule"));
                    break;
                case RuleCommandKind.RequestStateTransition when stateMachines is not null && catalog is not null:
                    stateMachines.Request(catalog.ResolveStateMachine(command.TargetId!), command.SecondaryId!, scopeKey);
                    break;
                case RuleCommandKind.EmitGameplaySignal:
                case RuleCommandKind.EmitPresentationSignal:
                case RuleCommandKind.SetRouteFlag:
                case RuleCommandKind.EmitAchievementCandidate:
                    events.Publish((frame, sequence) => new RuleSignalEvent(
                        frame, sequence, command.TargetId!, command.SourceDefinitionId,
                        command.Kind == RuleCommandKind.EmitPresentationSignal));
                    break;
                default:
                    deferred.Add(command);
                    break;
            }
        }
        return Array.AsReadOnly(deferred.ToArray());
    }
}
