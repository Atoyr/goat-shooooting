using System.Collections.ObjectModel;
using System.Text.Json;
using GoatShooooting.Definitions;

namespace GoatShooooting.Runtime;

public readonly record struct StatKey(int Value);

public static class BuiltInStatKeys
{
    public const string PlayerDamage = "player.damage";
    public const string PlayerFireInterval = "player.fire-interval";
    public const string PlayerMoveSpeed = "player.move-speed";
    public const string EnemyHp = "enemy.hp";
    public const string EnemyProjectileSpeed = "enemy.projectile-speed";
    public const string EnemyFireInterval = "enemy.fire-interval";
    public const string EmitterProjectileCount = "emitter.projectile-count";
    public const string ScoreEventMultiplier = "score.event-multiplier";
    public const string WorldTimeScale = "world.time-scale";
    public const string InteractionPower = "interaction.power";
}

public sealed class StatKeyRegistry
{
    private readonly string[] _ids;
    private readonly IReadOnlyList<string> _readOnlyIds;
    private readonly IReadOnlyDictionary<string, StatKey> _keys;

    private StatKeyRegistry(IEnumerable<string> ids)
    {
        _ids = ids.Order(StringComparer.Ordinal).ToArray();
        _readOnlyIds = Array.AsReadOnly(_ids);
        _keys = new ReadOnlyDictionary<string, StatKey>(_ids
            .Select((id, index) => (id, key: new StatKey(index)))
            .ToDictionary(static item => item.id, static item => item.key, StringComparer.Ordinal));
    }

    public IReadOnlyList<string> Ids => _readOnlyIds;
    public static StatKeyRegistry BuiltIn { get; } = new(new[]
    {
        BuiltInStatKeys.PlayerDamage,
        BuiltInStatKeys.PlayerFireInterval,
        BuiltInStatKeys.PlayerMoveSpeed,
        BuiltInStatKeys.EnemyHp,
        BuiltInStatKeys.EnemyProjectileSpeed,
        BuiltInStatKeys.EnemyFireInterval,
        BuiltInStatKeys.EmitterProjectileCount,
        BuiltInStatKeys.ScoreEventMultiplier,
        BuiltInStatKeys.WorldTimeScale,
        BuiltInStatKeys.InteractionPower
    });

    public StatKey Resolve(string id, string path = "$.statKey") =>
        !string.IsNullOrWhiteSpace(id) && _keys.TryGetValue(id, out var key) ? key :
        throw new DefinitionValidationException($"{path} references unknown StatKey '{id}'.");
    public string GetId(StatKey key) => (uint)key.Value < (uint)_ids.Length ? _ids[key.Value] :
        throw new ArgumentOutOfRangeException(nameof(key));
}

public enum ModifierOperation { Add, Multiply, Override, Curve }
public enum ModifierSourceTier { ProgramDefault, Variant, Difficulty, Ship, Rank, State }

public sealed record ModifierCurvePoint(double Input, double Output);

public sealed record CompiledModifier(
    StatKey StatKey,
    ModifierOperation Operation,
    double Value,
    ModifierSourceTier SourceTier,
    int Priority,
    string SourceDefinitionId,
    int DeclarationIndex,
    string Scope,
    long? StartFrame,
    long? EndFrame,
    string? SourceState,
    IReadOnlyList<ModifierCurvePoint>? Curve = null);

public sealed record CompiledModifierSet(string Id, IReadOnlyList<CompiledModifier> Modifiers)
{
    public static CompiledModifierSet Empty { get; } = new("empty", Array.Empty<CompiledModifier>());
}

public sealed record ModifierProvenance(
    string StatKey,
    ModifierOperation Operation,
    ModifierSourceTier SourceTier,
    int Priority,
    string SourceDefinitionId,
    int DeclarationIndex,
    string Scope,
    string? SourceState,
    double Before,
    double Operand,
    double After);

public sealed class ModifierResolution
{
    private readonly IReadOnlyDictionary<StatKey, double> _values;
    internal ModifierResolution(
        IReadOnlyDictionary<StatKey, double> values,
        IReadOnlyList<ModifierProvenance> provenance)
    {
        _values = values;
        Provenance = provenance;
    }

    public IReadOnlyList<ModifierProvenance> Provenance { get; }
    public double Get(StatKey key, double fallback = 0) => _values.TryGetValue(key, out var value) ? value : fallback;
}

public static class ModifierCompiler
{
    public static CompiledModifierSet Compile(
        string definitionId,
        IEnumerable<ModifierDefinition> definitions,
        ModifierSourceTier sourceTier,
        StatKeyRegistry? registry = null,
        string scope = "run",
        string? sourceState = null,
        string path = "$.modifiers")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        ArgumentNullException.ThrowIfNull(definitions);
        registry ??= StatKeyRegistry.BuiltIn;
        var modifiers = definitions.Select((definition, index) => Compile(
            definitionId, definition, sourceTier, index, registry, scope, sourceState, $"{path}[{index}]")).ToArray();
        ModifierResolver.ValidateOverrides(modifiers);
        return new CompiledModifierSet(definitionId, Array.AsReadOnly(modifiers));
    }

    public static CompiledModifierSet CompileDifficulty(
        DifficultyDefinition difficulty,
        StatKeyRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(difficulty);
        registry ??= StatKeyRegistry.BuiltIn;
        var values = new[]
        {
            Create(registry, BuiltInStatKeys.EnemyHp, ModifierOperation.Multiply, difficulty.EnemyHpMultiplier, 0),
            Create(registry, BuiltInStatKeys.EnemyProjectileSpeed, ModifierOperation.Multiply, difficulty.ProjectileSpeedMultiplier, 1),
            Create(registry, BuiltInStatKeys.EnemyFireInterval, ModifierOperation.Multiply, difficulty.FireIntervalMultiplier, 2),
            Create(registry, BuiltInStatKeys.EmitterProjectileCount, ModifierOperation.Add, difficulty.AdditionalProjectileCount, 3)
        };
        return new CompiledModifierSet(difficulty.Id, Array.AsReadOnly(values));

        CompiledModifier Create(
            StatKeyRegistry keys,
            string stat,
            ModifierOperation operation,
            double value,
            int index) => new(
                keys.Resolve(stat), operation, value, ModifierSourceTier.Difficulty, 0,
                difficulty.Id, index, "run", null, null, difficulty.Id);
    }

    private static CompiledModifier Compile(
        string definitionId,
        ModifierDefinition definition,
        ModifierSourceTier sourceTier,
        int index,
        StatKeyRegistry registry,
        string scope,
        string? sourceState,
        string path)
    {
        var operation = definition.Operation switch
        {
            "add" => ModifierOperation.Add,
            "multiply" => ModifierOperation.Multiply,
            "override" => ModifierOperation.Override,
            "curve" => ModifierOperation.Curve,
            _ => throw new DefinitionValidationException($"{path}.operation has unsupported modifier operation '{definition.Operation}'.")
        };
        IReadOnlyList<ModifierCurvePoint>? curve = null;
        var value = 0d;
        if (operation == ModifierOperation.Curve)
        {
            curve = CompileCurve(definition.Value, $"{path}.value");
        }
        else if (definition.Value.ValueKind != JsonValueKind.Number ||
            !definition.Value.TryGetDouble(out value) || !double.IsFinite(value))
        {
            throw new DefinitionValidationException($"{path}.value must be a finite number.");
        }

        return new CompiledModifier(
            registry.Resolve(definition.StatKey, $"{path}.statKey"),
            operation,
            value,
            sourceTier,
            definition.Priority,
            definitionId,
            index,
            scope,
            null,
            null,
            sourceState,
            curve);
    }

    private static IReadOnlyList<ModifierCurvePoint> CompileCurve(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < 2)
            throw new DefinitionValidationException($"{path} curve requires at least two points.");
        var points = value.EnumerateArray().Select((point, index) =>
        {
            if (point.ValueKind != JsonValueKind.Object ||
                !point.TryGetProperty("input", out var input) || !input.TryGetDouble(out var x) || !double.IsFinite(x) ||
                !point.TryGetProperty("output", out var output) || !output.TryGetDouble(out var y) || !double.IsFinite(y))
                throw new DefinitionValidationException($"{path}[{index}] requires finite input and output numbers.");
            return new ModifierCurvePoint(x, y);
        }).ToArray();
        for (var index = 1; index < points.Length; index++)
        {
            if (points[index - 1].Input >= points[index].Input)
                throw new DefinitionValidationException($"{path} curve inputs must be strictly increasing.");
        }
        return Array.AsReadOnly(points);
    }
}

public static class ModifierResolver
{
    public static ModifierResolution Resolve(
        IReadOnlyDictionary<StatKey, double> baseValues,
        IEnumerable<CompiledModifierSet> sets,
        StatKeyRegistry? registry = null,
        long frame = 0)
    {
        ArgumentNullException.ThrowIfNull(baseValues);
        ArgumentNullException.ThrowIfNull(sets);
        registry ??= StatKeyRegistry.BuiltIn;
        var modifiers = sets.SelectMany(static set => set.Modifiers)
            .Where(modifier =>
                (modifier.StartFrame is null || modifier.StartFrame <= frame) &&
                (modifier.EndFrame is null || modifier.EndFrame >= frame))
            .OrderBy(static modifier => modifier.SourceTier)
            .ThenBy(static modifier => modifier.Priority)
            .ThenBy(static modifier => modifier.SourceDefinitionId, StringComparer.Ordinal)
            .ThenBy(static modifier => modifier.DeclarationIndex)
            .ToArray();
        ValidateOverrides(modifiers);
        var values = new Dictionary<StatKey, double>(baseValues);
        var provenance = new List<ModifierProvenance>(modifiers.Length);
        foreach (var modifier in modifiers)
        {
            values.TryGetValue(modifier.StatKey, out var before);
            var after = modifier.Operation switch
            {
                ModifierOperation.Add => before + modifier.Value,
                ModifierOperation.Multiply => before * modifier.Value,
                ModifierOperation.Override => modifier.Value,
                ModifierOperation.Curve => EvaluateCurve(modifier.Curve!, before),
                _ => throw new ArgumentOutOfRangeException()
            };
            if (!double.IsFinite(after))
                throw new DefinitionValidationException(
                    $"Modifier '{modifier.SourceDefinitionId}' produces a non-finite '{registry.GetId(modifier.StatKey)}' value.");
            values[modifier.StatKey] = after;
            provenance.Add(new ModifierProvenance(
                registry.GetId(modifier.StatKey), modifier.Operation, modifier.SourceTier, modifier.Priority,
                modifier.SourceDefinitionId, modifier.DeclarationIndex, modifier.Scope, modifier.SourceState,
                before, modifier.Value, after));
        }
        return new ModifierResolution(
            new ReadOnlyDictionary<StatKey, double>(values),
            Array.AsReadOnly(provenance.ToArray()));
    }

    internal static void ValidateOverrides(IEnumerable<CompiledModifier> modifiers)
    {
        var conflict = modifiers.Where(static modifier => modifier.Operation == ModifierOperation.Override)
            .GroupBy(static modifier => (modifier.StatKey, modifier.SourceTier, modifier.Priority))
            .FirstOrDefault(static group => group.Count() > 1);
        if (conflict is null) return;
        throw new DefinitionValidationException(
            $"StatKey {conflict.Key.StatKey.Value} has multiple override modifiers at " +
            $"tier '{conflict.Key.SourceTier}' and priority {conflict.Key.Priority}: " +
            string.Join(", ", conflict.Select(static modifier => modifier.SourceDefinitionId)) + ".");
    }

    private static double EvaluateCurve(IReadOnlyList<ModifierCurvePoint> points, double input)
    {
        if (input <= points[0].Input) return points[0].Output;
        if (input >= points[^1].Input) return points[^1].Output;
        for (var index = 1; index < points.Count; index++)
        {
            if (input > points[index].Input) continue;
            var previous = points[index - 1];
            var next = points[index];
            var amount = (input - previous.Input) / (next.Input - previous.Input);
            return previous.Output + ((next.Output - previous.Output) * amount);
        }
        throw new InvalidOperationException("Curve evaluation did not find a segment.");
    }
}
