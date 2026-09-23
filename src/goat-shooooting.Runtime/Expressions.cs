using System.Collections.ObjectModel;
using System.Numerics;
using System.Text.Json;
using GoatShooooting.Definitions;

namespace GoatShooooting.Runtime;

public enum ExpressionValueType
{
    Number,
    Integer,
    Boolean,
    Vector2,
    Id,
    TagSet
}

public readonly record struct ExpressionValue
{
    private ExpressionValue(ExpressionValueType type, double number, Vector2 vector, string? text, IReadOnlyList<string>? tags)
    {
        Type = type;
        NumberValue = number;
        VectorValue = vector;
        TextValue = text;
        TagValues = tags;
    }

    public ExpressionValueType Type { get; }
    public double NumberValue { get; }
    public Vector2 VectorValue { get; }
    public string? TextValue { get; }
    public IReadOnlyList<string>? TagValues { get; }
    public bool BooleanValue => NumberValue != 0;
    public long IntegerValue => checked((long)NumberValue);

    public static ExpressionValue Number(double value) =>
        double.IsFinite(value) ? new(ExpressionValueType.Number, value, default, null, null) :
        throw new ArgumentOutOfRangeException(nameof(value));
    public static ExpressionValue Integer(long value) => new(ExpressionValueType.Integer, value, default, null, null);
    public static ExpressionValue Boolean(bool value) => new(ExpressionValueType.Boolean, value ? 1 : 0, default, null, null);
    public static ExpressionValue Vector(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) ? new(ExpressionValueType.Vector2, 0, value, null, null) :
        throw new ArgumentOutOfRangeException(nameof(value));
    public static ExpressionValue Id(string value) =>
        !string.IsNullOrWhiteSpace(value) ? new(ExpressionValueType.Id, 0, default, value, null) :
        throw new ArgumentException("An id must not be empty.", nameof(value));
    public static ExpressionValue Tags(IEnumerable<string> values)
    {
        var tags = values.ToArray();
        if (tags.Any(string.IsNullOrWhiteSpace) || tags.Distinct(StringComparer.Ordinal).Count() != tags.Length)
            throw new ArgumentException("Tags must be non-empty and unique.", nameof(values));
        return new(ExpressionValueType.TagSet, 0, default, null, Array.AsReadOnly(tags));
    }
}

public sealed record CompiledParameter(string Id, ExpressionValueType Type, ExpressionValue Default, double? Minimum, double? Maximum);

public sealed class CompiledParameterSchema
{
    private readonly IReadOnlyDictionary<string, int> _indices;

    private CompiledParameterSchema(CompiledParameter[] parameters)
    {
        Parameters = Array.AsReadOnly(parameters);
        _indices = new ReadOnlyDictionary<string, int>(parameters
            .Select((parameter, index) => (parameter.Id, index))
            .ToDictionary(static item => item.Id, static item => item.index, StringComparer.Ordinal));
    }

    public IReadOnlyList<CompiledParameter> Parameters { get; }
    public static CompiledParameterSchema Empty { get; } = new(Array.Empty<CompiledParameter>());

    public static CompiledParameterSchema Compile(
        IReadOnlyDictionary<string, ProgramParameterDefinition> definitions,
        string path)
    {
        var parameters = definitions.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new CompiledParameter(
                pair.Key,
                ExpressionCompiler.ParseType(pair.Value.Type, $"{path}.parameters.{pair.Key}.type"),
                ExpressionCompiler.ParseLiteral(pair.Value.Default,
                    ExpressionCompiler.ParseType(pair.Value.Type, $"{path}.parameters.{pair.Key}.type"),
                    $"{path}.parameters.{pair.Key}.default"),
                pair.Value.Minimum,
                pair.Value.Maximum))
            .ToArray();
        return new CompiledParameterSchema(parameters);
    }

    public int Resolve(string id, string path) =>
        _indices.TryGetValue(id, out var index) ? index :
        throw new DefinitionValidationException($"{path} references unknown parameter '{id}'.");

    public ExpressionValue[] Bind(IReadOnlyDictionary<string, JsonElement>? overrides, string path)
    {
        var result = Parameters.Select(static parameter => parameter.Default).ToArray();
        if (overrides is null) return result;
        foreach (var pair in overrides)
        {
            var index = Resolve(pair.Key, path);
            var parameter = Parameters[index];
            var value = ExpressionCompiler.ParseLiteral(pair.Value, parameter.Type, $"{path}.{pair.Key}");
            if (parameter.Type is ExpressionValueType.Number or ExpressionValueType.Integer)
            {
                if (parameter.Minimum is { } minimum && value.NumberValue < minimum ||
                    parameter.Maximum is { } maximum && value.NumberValue > maximum)
                    throw new DefinitionValidationException($"{path}.{pair.Key} is outside the declared parameter range.");
            }

            result[index] = value;
        }

        return result;
    }
}

public sealed class ExpressionBindingSchema
{
    private readonly Dictionary<string, ExpressionValueType> _difficulty = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExpressionValueType> _resources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExpressionValueType> _context = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExpressionValueType> _events = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExpressionValueType> _snapshots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExpressionValueType> _random = new(StringComparer.Ordinal);

    public ExpressionBindingSchema AddDifficulty(string id, ExpressionValueType type) { _difficulty.Add(id, type); return this; }
    public ExpressionBindingSchema AddResource(string id, ExpressionValueType type) { _resources.Add(id, type); return this; }
    public ExpressionBindingSchema AddContext(string id, ExpressionValueType type) { _context.Add(id, type); return this; }
    public ExpressionBindingSchema AddEvent(string id, ExpressionValueType type) { _events.Add(id, type); return this; }
    public ExpressionBindingSchema AddSnapshot(string id, ExpressionValueType type) { _snapshots.Add(id, type); return this; }
    public ExpressionBindingSchema AddRandom(string id, ExpressionValueType type) { _random.Add(id, type); return this; }

    internal ExpressionValueType Resolve(ExpressionSource source, string id, string path)
    {
        var table = source switch
        {
            ExpressionSource.Difficulty => _difficulty,
            ExpressionSource.Resource => _resources,
            ExpressionSource.Context => _context,
            ExpressionSource.Event => _events,
            ExpressionSource.Snapshot => _snapshots,
            ExpressionSource.Random => _random,
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
        return table.TryGetValue(id, out var type) ? type :
            throw new DefinitionValidationException($"{path} references unknown {source.ToString().ToLowerInvariant()} value '{id}'.");
    }
}

public sealed class ExpressionEvaluationContext
{
    public IReadOnlyList<ExpressionValue> Parameters { get; init; } = Array.Empty<ExpressionValue>();
    public IReadOnlyDictionary<string, ExpressionValue> Difficulty { get; init; } = Empty;
    public IReadOnlyDictionary<string, ExpressionValue> Resources { get; init; } = Empty;
    public IReadOnlyDictionary<string, ExpressionValue> Context { get; init; } = Empty;
    public IReadOnlyDictionary<string, ExpressionValue> Events { get; init; } = Empty;
    public IReadOnlyDictionary<string, ExpressionValue> Snapshots { get; init; } = Empty;
    /// <summary>Values sampled by the owning program from its instance-local deterministic stream.</summary>
    public IReadOnlyDictionary<string, ExpressionValue> Random { get; init; } = Empty;

    private static readonly IReadOnlyDictionary<string, ExpressionValue> Empty =
        new ReadOnlyDictionary<string, ExpressionValue>(new Dictionary<string, ExpressionValue>());

    internal ExpressionValue Read(ExpressionSource source, string id) =>
        GetTable(source).TryGetValue(id, out var value) ? value :
        throw new InvalidOperationException($"The expression context has no {source.ToString().ToLowerInvariant()} value '{id}'.");

    private IReadOnlyDictionary<string, ExpressionValue> GetTable(ExpressionSource source) => source switch
    {
        ExpressionSource.Difficulty => Difficulty,
        ExpressionSource.Resource => Resources,
        ExpressionSource.Context => Context,
        ExpressionSource.Event => Events,
        ExpressionSource.Snapshot => Snapshots,
        ExpressionSource.Random => Random,
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };
}

public sealed class CompiledExpression
{
    private readonly ExpressionNode _root;
    internal CompiledExpression(ExpressionNode root) => _root = root;
    public ExpressionValueType Type => _root.Type;
    public ExpressionValue Evaluate(ExpressionEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var result = _root.Evaluate(context);
        if (result.Type == ExpressionValueType.Number && !double.IsFinite(result.NumberValue))
            throw new InvalidOperationException("A compiled expression produced a non-finite result.");
        return result;
    }
}

public static class ExpressionCompiler
{
    public static CompiledExpression Compile(
        JsonElement expression,
        ExpressionValueType expectedType,
        CompiledParameterSchema parameters,
        ExpressionBindingSchema? bindings = null,
        string path = "$.expression")
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var node = CompileNode(expression, parameters, bindings ?? new ExpressionBindingSchema(), path);
        if (expectedType == ExpressionValueType.Number && node.Type == ExpressionValueType.Integer)
            node = new NumberConversionExpressionNode(node);
        if (node.Type != expectedType)
            throw new DefinitionValidationException($"{path} has type '{Format(node.Type)}', expected '{Format(expectedType)}'.");
        return new CompiledExpression(node);
    }

    internal static ExpressionValueType ParseType(string type, string path) => type switch
    {
        "number" => ExpressionValueType.Number,
        "integer" => ExpressionValueType.Integer,
        "boolean" => ExpressionValueType.Boolean,
        "vector2" => ExpressionValueType.Vector2,
        "id" => ExpressionValueType.Id,
        "tag-set" => ExpressionValueType.TagSet,
        _ => throw new DefinitionValidationException($"{path} has unsupported expression type '{type}'.")
    };

    internal static ExpressionValue ParseLiteral(JsonElement value, ExpressionValueType type, string path)
    {
        try
        {
            return type switch
            {
                ExpressionValueType.Number when value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) => ExpressionValue.Number(number),
                ExpressionValueType.Integer when value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var integer) => ExpressionValue.Integer(integer),
                ExpressionValueType.Boolean when value.ValueKind is JsonValueKind.True or JsonValueKind.False => ExpressionValue.Boolean(value.GetBoolean()),
                ExpressionValueType.Vector2 when value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 2 =>
                    ExpressionValue.Vector(new Vector2(value[0].GetSingle(), value[1].GetSingle())),
                ExpressionValueType.Id when value.ValueKind == JsonValueKind.String => ExpressionValue.Id(value.GetString()!),
                ExpressionValueType.TagSet when value.ValueKind == JsonValueKind.Array =>
                    ExpressionValue.Tags(value.EnumerateArray().Select(static item => item.GetString()!)),
                _ => throw new DefinitionValidationException($"{path} does not match type '{Format(type)}'.")
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or ArgumentException or OverflowException)
        {
            throw new DefinitionValidationException($"{path} does not contain a valid finite '{Format(type)}' literal.");
        }
    }

    private static ExpressionNode CompileNode(
        JsonElement value,
        CompiledParameterSchema parameters,
        ExpressionBindingSchema bindings,
        string path)
    {
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return new LiteralExpressionNode(ExpressionValue.Boolean(value.GetBoolean()));
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt64(out var integer)) return new LiteralExpressionNode(ExpressionValue.Integer(integer));
            return new LiteralExpressionNode(ParseLiteral(value, ExpressionValueType.Number, path));
        }
        if (value.ValueKind == JsonValueKind.String)
            return new LiteralExpressionNode(ExpressionValue.Id(value.GetString()!));
        if (value.ValueKind != JsonValueKind.Object)
            throw new DefinitionValidationException($"{path} must be a scalar or expression object.");

        if (TryString(value, "parameter", out var parameterId))
        {
            var index = parameters.Resolve(parameterId, $"{path}.parameter");
            return new ParameterExpressionNode(index, parameters.Parameters[index].Type);
        }

        foreach (var (name, source) in Sources)
        {
            if (!TryString(value, name, out var id)) continue;
            return new SourceExpressionNode(source, id, bindings.Resolve(source, id, $"{path}.{name}"));
        }

        if (!TryString(value, "op", out var op))
            throw new DefinitionValidationException($"{path} requires 'op' or a typed value source.");
        return CompileOperation(value, op, parameters, bindings, path);
    }

    private static ExpressionNode CompileOperation(
        JsonElement value,
        string op,
        CompiledParameterSchema parameters,
        ExpressionBindingSchema bindings,
        string path)
    {
        ExpressionNode Child(string name) => CompileNode(Required(value, name, path), parameters, bindings, $"{path}.{name}");
        switch (op)
        {
            case "add":
            case "subtract":
            case "multiply":
            case "min":
            case "max":
                {
                    var left = Child("left");
                    var right = Child("right");
                    var type = CoerceNumeric(ref left, ref right, path);
                    return new BinaryExpressionNode(op, type, left, right);
                }
            case "divide-safe":
                {
                    var left = Child("left");
                    var right = Child("right");
                    var type = CoerceNumeric(ref left, ref right, path);
                    var fallback = Child("fallback");
                    if (type == ExpressionValueType.Number && fallback.Type == ExpressionValueType.Integer)
                        fallback = new NumberConversionExpressionNode(fallback);
                    RequireType(fallback, type, $"{path}.fallback");
                    return new DivideExpressionNode(left, right, fallback);
                }
            case "clamp":
                {
                    var input = Child("value");
                    var minimum = Child("minimum");
                    var maximum = Child("maximum");
                    _ = CoerceNumeric(ref input, ref minimum, path);
                    _ = CoerceNumeric(ref input, ref maximum, path);
                    if (input.Type == ExpressionValueType.Number && minimum.Type == ExpressionValueType.Integer)
                        minimum = new NumberConversionExpressionNode(minimum);
                    return new ClampExpressionNode(input, minimum, maximum);
                }
            case "lerp":
                {
                    var start = Child("start");
                    var end = Child("end");
                    if (start.Type != end.Type || start.Type is not (ExpressionValueType.Number or ExpressionValueType.Vector2))
                        throw new DefinitionValidationException($"{path} lerp endpoints must have matching number or vector2 types.");
                    var amount = Child("amount");
                    if (amount.Type == ExpressionValueType.Integer) amount = new NumberConversionExpressionNode(amount);
                    RequireType(amount, ExpressionValueType.Number, $"{path}.amount");
                    return new LerpExpressionNode(start, end, amount);
                }
            case "curve":
                {
                    var input = Child("value");
                    if (input.Type == ExpressionValueType.Integer) input = new NumberConversionExpressionNode(input);
                    RequireType(input, ExpressionValueType.Number, $"{path}.value");
                    var pointsValue = Required(value, "points", path);
                    if (pointsValue.ValueKind != JsonValueKind.Array || pointsValue.GetArrayLength() < 2)
                        throw new DefinitionValidationException($"{path}.points requires at least two curve points.");
                    var points = pointsValue.EnumerateArray().Select((point, index) =>
                    {
                        if (point.ValueKind != JsonValueKind.Object ||
                            !point.TryGetProperty("input", out var xValue) || !xValue.TryGetDouble(out var x) || !double.IsFinite(x) ||
                            !point.TryGetProperty("output", out var yValue) || !yValue.TryGetDouble(out var y) || !double.IsFinite(y))
                            throw new DefinitionValidationException($"{path}.points[{index}] requires finite input and output.");
                        return new ModifierCurvePoint(x, y);
                    }).ToArray();
                    for (var index = 1; index < points.Length; index++)
                        if (points[index - 1].Input >= points[index].Input)
                            throw new DefinitionValidationException($"{path}.points inputs must be strictly increasing.");
                    return new CurveExpressionNode(input, Array.AsReadOnly(points));
                }
            case "equal":
            case "not-equal":
                {
                    var left = Child("left");
                    var right = Child("right");
                    if (left.Type is ExpressionValueType.Number or ExpressionValueType.Integer &&
                        right.Type is ExpressionValueType.Number or ExpressionValueType.Integer)
                        _ = CoerceNumeric(ref left, ref right, path);
                    if (left.Type != right.Type) throw new DefinitionValidationException($"{path} comparison operands must have the same type.");
                    return new BinaryExpressionNode(op, ExpressionValueType.Boolean, left, right);
                }
            case "less":
            case "less-than":
            case "less-or-equal":
            case "less-than-or-equal":
            case "greater":
            case "greater-than":
            case "greater-or-equal":
            case "greater-than-or-equal":
                {
                    var left = Child("left");
                    var right = Child("right");
                    _ = CoerceNumeric(ref left, ref right, path);
                    var normalized = op switch
                    {
                        "less-than" => "less",
                        "less-than-or-equal" => "less-or-equal",
                        "greater-than" => "greater",
                        "greater-than-or-equal" => "greater-or-equal",
                        _ => op
                    };
                    return new BinaryExpressionNode(normalized, ExpressionValueType.Boolean, left, right);
                }
            case "and":
            case "or":
                {
                    var left = Child("left");
                    var right = Child("right");
                    RequireType(left, ExpressionValueType.Boolean, $"{path}.left");
                    RequireType(right, ExpressionValueType.Boolean, $"{path}.right");
                    return new BinaryExpressionNode(op, ExpressionValueType.Boolean, left, right);
                }
            case "not":
                {
                    var input = Child("value");
                    RequireType(input, ExpressionValueType.Boolean, $"{path}.value");
                    return new NotExpressionNode(input);
                }
            default:
                throw new DefinitionValidationException($"{path} has unknown pure expression op '{op}'.");
        }
    }

    private static JsonElement Required(JsonElement owner, string name, string path) =>
        owner.TryGetProperty(name, out var value) ? value :
        throw new DefinitionValidationException($"{path} requires '{name}'.");

    private static bool TryString(JsonElement owner, string name, out string value)
    {
        if (owner.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(element.GetString()))
        {
            value = element.GetString()!;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static ExpressionValueType CoerceNumeric(ref ExpressionNode left, ref ExpressionNode right, string path)
    {
        if (left.Type is not (ExpressionValueType.Number or ExpressionValueType.Integer) ||
            right.Type is not (ExpressionValueType.Number or ExpressionValueType.Integer))
            throw new DefinitionValidationException($"{path} operands must have matching numeric types.");
        if (left.Type == ExpressionValueType.Number && right.Type == ExpressionValueType.Integer)
            right = new NumberConversionExpressionNode(right);
        else if (left.Type == ExpressionValueType.Integer && right.Type == ExpressionValueType.Number)
            left = new NumberConversionExpressionNode(left);
        return left.Type;
    }

    private static void RequireType(ExpressionNode node, ExpressionValueType type, string path)
    {
        if (node.Type != type) throw new DefinitionValidationException($"{path} must have type '{Format(type)}'.");
    }

    private static string Format(ExpressionValueType type) => type switch
    {
        ExpressionValueType.Vector2 => "vector2",
        ExpressionValueType.TagSet => "tag-set",
        _ => type.ToString().ToLowerInvariant()
    };

    private static readonly (string Name, ExpressionSource Source)[] Sources =
    [
        ("difficulty", ExpressionSource.Difficulty),
        ("resource", ExpressionSource.Resource),
        ("context", ExpressionSource.Context),
        ("event", ExpressionSource.Event),
        ("snapshot", ExpressionSource.Snapshot),
        ("random", ExpressionSource.Random)
    ];
}

internal enum ExpressionSource { Difficulty, Resource, Context, Event, Snapshot, Random }

internal abstract class ExpressionNode(ExpressionValueType type)
{
    public ExpressionValueType Type { get; } = type;
    public abstract ExpressionValue Evaluate(ExpressionEvaluationContext context);
}

internal sealed class LiteralExpressionNode(ExpressionValue value) : ExpressionNode(value.Type)
{
    public override ExpressionValue Evaluate(ExpressionEvaluationContext context) => value;
}

internal sealed class ParameterExpressionNode(int index, ExpressionValueType type) : ExpressionNode(type)
{
    public override ExpressionValue Evaluate(ExpressionEvaluationContext context) =>
        (uint)index < (uint)context.Parameters.Count ? context.Parameters[index] :
        throw new InvalidOperationException($"The expression context has no parameter at index {index}.");
}

internal sealed class NumberConversionExpressionNode(ExpressionNode value) : ExpressionNode(ExpressionValueType.Number)
{
    public override ExpressionValue Evaluate(ExpressionEvaluationContext context) =>
        ExpressionValue.Number(value.Evaluate(context).NumberValue);
}

internal sealed class SourceExpressionNode(ExpressionSource source, string id, ExpressionValueType type) : ExpressionNode(type)
{
    public override ExpressionValue Evaluate(ExpressionEvaluationContext context) => context.Read(source, id);
}

internal sealed class BinaryExpressionNode(
    string op,
    ExpressionValueType type,
    ExpressionNode left,
    ExpressionNode right) : ExpressionNode(type)
{
    public override ExpressionValue Evaluate(ExpressionEvaluationContext context)
    {
        var a = left.Evaluate(context);
        if (op == "and" && !a.BooleanValue) return ExpressionValue.Boolean(false);
        if (op == "or" && a.BooleanValue) return ExpressionValue.Boolean(true);
        var b = right.Evaluate(context);
        if (Type == ExpressionValueType.Boolean) return ExpressionValue.Boolean(op switch
        {
            "equal" => a.Equals(b),
            "not-equal" => !a.Equals(b),
            "less" => a.NumberValue < b.NumberValue,
            "less-or-equal" => a.NumberValue <= b.NumberValue,
            "greater" => a.NumberValue > b.NumberValue,
            "greater-or-equal" => a.NumberValue >= b.NumberValue,
            "and" => a.BooleanValue && b.BooleanValue,
            "or" => a.BooleanValue || b.BooleanValue,
            _ => throw new InvalidOperationException($"Unsupported boolean op '{op}'.")
        });
        var result = op switch
        {
            "add" => a.NumberValue + b.NumberValue,
            "subtract" => a.NumberValue - b.NumberValue,
            "multiply" => a.NumberValue * b.NumberValue,
            "min" => Math.Min(a.NumberValue, b.NumberValue),
            "max" => Math.Max(a.NumberValue, b.NumberValue),
            _ => throw new InvalidOperationException($"Unsupported numeric op '{op}'.")
        };
        return Type == ExpressionValueType.Integer
            ? ExpressionValue.Integer(checked((long)result))
            : ExpressionValue.Number(result);
    }
}

internal sealed class DivideExpressionNode(ExpressionNode left, ExpressionNode right, ExpressionNode fallback)
    : ExpressionNode(left.Type)
{
    public override ExpressionValue Evaluate(ExpressionEvaluationContext context)
    {
        var a = left.Evaluate(context);
        var b = right.Evaluate(context);
        if (b.NumberValue == 0) return fallback.Evaluate(context);
        var result = a.NumberValue / b.NumberValue;
        return Type == ExpressionValueType.Integer
            ? ExpressionValue.Integer(checked((long)result))
            : ExpressionValue.Number(result);
    }
}

internal sealed class ClampExpressionNode(ExpressionNode value, ExpressionNode minimum, ExpressionNode maximum)
    : ExpressionNode(value.Type)
{
    public override ExpressionValue Evaluate(ExpressionEvaluationContext context)
    {
        var result = Math.Clamp(
            value.Evaluate(context).NumberValue,
            minimum.Evaluate(context).NumberValue,
            maximum.Evaluate(context).NumberValue);
        return Type == ExpressionValueType.Integer ? ExpressionValue.Integer(checked((long)result)) : ExpressionValue.Number(result);
    }
}

internal sealed class LerpExpressionNode(ExpressionNode start, ExpressionNode end, ExpressionNode amount)
    : ExpressionNode(start.Type)
{
    public override ExpressionValue Evaluate(ExpressionEvaluationContext context)
    {
        var a = start.Evaluate(context);
        var b = end.Evaluate(context);
        var t = amount.Evaluate(context).NumberValue;
        return Type == ExpressionValueType.Vector2
            ? ExpressionValue.Vector(Vector2.Lerp(a.VectorValue, b.VectorValue, checked((float)t)))
            : ExpressionValue.Number(a.NumberValue + ((b.NumberValue - a.NumberValue) * t));
    }
}

internal sealed class NotExpressionNode(ExpressionNode value) : ExpressionNode(ExpressionValueType.Boolean)
{
    public override ExpressionValue Evaluate(ExpressionEvaluationContext context) =>
        ExpressionValue.Boolean(!value.Evaluate(context).BooleanValue);
}

internal sealed class CurveExpressionNode(ExpressionNode value, IReadOnlyList<ModifierCurvePoint> points)
    : ExpressionNode(ExpressionValueType.Number)
{
    public override ExpressionValue Evaluate(ExpressionEvaluationContext context)
    {
        var input = value.Evaluate(context).NumberValue;
        if (input <= points[0].Input) return ExpressionValue.Number(points[0].Output);
        if (input >= points[^1].Input) return ExpressionValue.Number(points[^1].Output);
        for (var index = 1; index < points.Count; index++)
        {
            if (input > points[index].Input) continue;
            var previous = points[index - 1];
            var next = points[index];
            var amount = (input - previous.Input) / (next.Input - previous.Input);
            return ExpressionValue.Number(previous.Output + ((next.Output - previous.Output) * amount));
        }
        throw new InvalidOperationException("Curve evaluation did not find a segment.");
    }
}
