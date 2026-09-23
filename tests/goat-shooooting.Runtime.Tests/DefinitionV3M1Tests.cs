using System.Text.Json;
using GoatShooooting.Definitions;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class DefinitionV3M1Tests
{
    [Fact]
    public void PureExpressionCompilesTypedParametersAndUsesExplicitDivideFallback()
    {
        var schema = CompiledParameterSchema.Compile(
            new Dictionary<string, ProgramParameterDefinition>
            {
                ["numerator"] = Parameter("number", 12.5, minimum: 0),
                ["denominator"] = Parameter("number", 0.0, minimum: 0),
                ["fallback"] = Parameter("number", 4.0)
            },
            "programs/test.json");
        var expression = Json("""
            { "op": "divide-safe", "left": { "parameter": "numerator" },
              "right": { "parameter": "denominator" }, "fallback": { "parameter": "fallback" } }
            """);

        var compiled = ExpressionCompiler.Compile(expression, ExpressionValueType.Number, schema);
        var values = schema.Bind(null, "test");

        Assert.Equal(4, compiled.Evaluate(new ExpressionEvaluationContext { Parameters = values }).NumberValue);
        Assert.Throws<DefinitionValidationException>(() => ExpressionCompiler.Compile(
            expression, ExpressionValueType.Boolean, schema));
    }

    [Fact]
    public void ParameterBindingRejectsTypeAndRangeViolationsBeforeRuntime()
    {
        var schema = CompiledParameterSchema.Compile(
            new Dictionary<string, ProgramParameterDefinition>
            {
                ["count"] = Parameter("integer", 4, minimum: 1, maximum: 8)
            },
            "programs/test.json");

        Assert.Throws<DefinitionValidationException>(() => schema.Bind(
            new Dictionary<string, JsonElement> { ["count"] = Json("12") }, "sets/too-large"));
        Assert.Throws<DefinitionValidationException>(() => schema.Bind(
            new Dictionary<string, JsonElement> { ["count"] = Json("true") }, "sets/wrong-type"));
    }

    [Fact]
    public void VariantChangesProgramTopologyWhileParametersRemainTyped()
    {
        var standard = Program("standard", 8);
        var dense = Program("dense", 16);
        var parameterSet = new ParameterSetDefinition
        {
            Id = "dense-values",
            Values = new Dictionary<string, JsonElement> { ["count"] = Json("24") }
        };
        var variant = new VariantDefinition
        {
            Id = "dense-mode",
            Bindings = new[]
            {
                new VariantBindingDefinition
                {
                    SlotId = "boss.main", ProgramId = "dense", ParameterSetId = parameterSet.Id
                }
            }
        };
        var compiled = new DefinitionCompiler().Compile(
            CreateCatalog(new[] { standard, dense }, new[] { variant }, new[] { parameterSet }),
            RuntimeCapabilityRegistry.CreateBuiltIn());
        var slot = new SemanticProgramSlotDefinition { SlotId = "boss.main", DefaultProgramId = "standard" };

        var normal = compiled.ResolveProgramBinding(slot);
        var selected = compiled.ResolveProgramBinding(slot, compiled.ResolveVariant("dense-mode"));

        Assert.Equal("standard", normal.Program.Definition.Id);
        Assert.Equal(8, normal.ParameterValues.Single().IntegerValue);
        Assert.Equal("dense", selected.Program.Definition.Id);
        Assert.Equal(24, selected.ParameterValues.Single().IntegerValue);
        Assert.Equal("variant:dense-mode", selected.Source);
    }

    [Fact]
    public void VariantRuleBindingsRejectUnknownRulesAndDuplicateSemanticSlots()
    {
        var program = Program("standard", 8);
        var rule = new EventRuleDefinition
        {
            Id = "score-standard",
            On = "enemy-destroyed",
            Actions =
            [
                new RuleActionDefinition
                {
                    Op = "award-score",
                    Arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        ["category"] = Json("\"kill\""),
                        ["base"] = Json("1")
                    }
                }
            ]
        };
        var unknownRule = new VariantDefinition
        {
            Id = "unknown-rule",
            RuleBindings =
            [
                new VariantRuleBindingDefinition
                {
                    SlotId = "score.main",
                    EventRuleId = "missing"
                }
            ]
        };
        var duplicateSlot = new VariantDefinition
        {
            Id = "duplicate-slot",
            RuleBindings =
            [
                new VariantRuleBindingDefinition
                {
                    SlotId = "score.main",
                    EventRuleId = rule.Id
                },
                new VariantRuleBindingDefinition
                {
                    SlotId = "score.main",
                    EventRuleId = rule.Id
                }
            ]
        };

        Assert.Throws<DefinitionValidationException>(() => CreateCatalog(
            [program], [unknownRule], [], [rule]));
        var exception = Assert.Throws<DefinitionValidationException>(() => CreateCatalog(
            [program], [duplicateSlot], [], [rule]));
        Assert.Contains("duplicate rule binding", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ModifierResolutionUsesStableTierPriorityAndDeclarationOrderWithProvenance()
    {
        var key = StatKeyRegistry.BuiltIn.Resolve(BuiltInStatKeys.EnemyHp);
        var variant = ModifierCompiler.Compile("variant", new[]
        {
            Modifier(BuiltInStatKeys.EnemyHp, "add", 3, priority: 5)
        }, ModifierSourceTier.Variant);
        var difficulty = ModifierCompiler.Compile("difficulty", new[]
        {
            Modifier(BuiltInStatKeys.EnemyHp, "multiply", 2, priority: -10),
            Modifier(BuiltInStatKeys.EnemyHp, "add", 1, priority: -10)
        }, ModifierSourceTier.Difficulty);

        var resolved = ModifierResolver.Resolve(
            new Dictionary<StatKey, double> { [key] = 10 },
            new[] { difficulty, variant });

        Assert.Equal(27, resolved.Get(key));
        Assert.Collection(resolved.Provenance,
            step => Assert.Equal("variant", step.SourceDefinitionId),
            step => Assert.Equal(ModifierOperation.Multiply, step.Operation),
            step => Assert.Equal(ModifierOperation.Add, step.Operation));
        Assert.Equal(27, resolved.Provenance[^1].After);
    }

    [Fact]
    public void SameTierAndPriorityOverrideConflictIsAValidationError()
    {
        var first = ModifierCompiler.Compile("first", new[]
        {
            Modifier(BuiltInStatKeys.WorldTimeScale, "override", 0.5, priority: 10)
        }, ModifierSourceTier.State);
        var second = ModifierCompiler.Compile("second", new[]
        {
            Modifier(BuiltInStatKeys.WorldTimeScale, "override", 0.25, priority: 10)
        }, ModifierSourceTier.State);

        Assert.Throws<DefinitionValidationException>(() => ModifierResolver.Resolve(
            new Dictionary<StatKey, double>(), new[] { first, second }));
    }

    [Fact]
    public void LegacyDifficultyIsCompiledToRegisteredModifiersWithoutChangingValues()
    {
        var difficulty = new DifficultyDefinition
        {
            Id = "hard",
            EnemyHpMultiplier = 1.5f,
            ProjectileSpeedMultiplier = 1.25f,
            FireIntervalMultiplier = 0.8f,
            AdditionalProjectileCount = 2
        };
        var state = new RunState();
        var modifiers = new RunModifierState();

        modifiers.Configure(difficulty, ModifierCompiler.CompileDifficulty(difficulty), null, null, state);

        Assert.Equal(difficulty.EnemyHpMultiplier, modifiers.EnemyHealthMultiplier);
        Assert.Equal(difficulty.ProjectileSpeedMultiplier, modifiers.EnemyProjectileSpeedMultiplier);
        Assert.Equal(difficulty.FireIntervalMultiplier, modifiers.EnemyFireIntervalMultiplier);
        Assert.Equal(difficulty.AdditionalProjectileCount, modifiers.AdditionalEnemyProjectiles);
        Assert.Equal(4, modifiers.Provenance.Count);
        Assert.All(modifiers.Provenance, step => Assert.Equal(ModifierSourceTier.Difficulty, step.SourceTier));
    }

    private static DefinitionCatalog CreateCatalog(
        IEnumerable<ProgramDefinition> programs,
        IEnumerable<VariantDefinition> variants,
        IEnumerable<ParameterSetDefinition> parameterSets,
        IEnumerable<EventRuleDefinition>? eventRules = null)
    {
        var baseline = TestDefinitions.Create();
        return new DefinitionCatalog(
            baseline.Game,
            baseline.Players.Values,
            baseline.Enemies.Values,
            baseline.Bullets.Values,
            baseline.Weapons.Values,
            baseline.Stages.Values,
            programs: programs,
            variants: variants,
            parameterSets: parameterSets,
            eventRules: eventRules);
    }

    private static ProgramDefinition Program(string id, int count) => new()
    {
        Id = id,
        Domain = "projectile",
        Parameters = new Dictionary<string, ProgramParameterDefinition>
        {
            ["count"] = Parameter("integer", count, minimum: 1, maximum: 64)
        },
        EntryPoints = new Dictionary<string, IReadOnlyList<ProgramNodeDefinition>>
        {
            ["onSpawn"] = Array.Empty<ProgramNodeDefinition>()
        }
    };

    private static ProgramParameterDefinition Parameter(
        string type,
        object value,
        double? minimum = null,
        double? maximum = null) => new()
        {
            Type = type,
            Default = JsonSerializer.SerializeToElement(value),
            Minimum = minimum,
            Maximum = maximum
        };

    private static ModifierDefinition Modifier(string key, string operation, double value, int priority) => new()
    {
        StatKey = key,
        Operation = operation,
        Value = JsonSerializer.SerializeToElement(value),
        Priority = priority
    };

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
