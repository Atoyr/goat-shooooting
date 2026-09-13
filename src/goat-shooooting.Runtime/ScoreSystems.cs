using System.Text.Json;
using GoatShooooting.Definitions;

namespace GoatShooooting.Runtime;

public interface IScoreRule
{
    void Advance(long frame, RunState state)
    {
    }

    void Apply(ScoreRuleContext context);
}

public sealed class ScoreRuleContext
{
    private readonly List<ScoreCandidate> _candidates = new();

    internal ScoreRuleContext(IGameplayEvent gameplayEvent, RunState state)
    {
        Event = gameplayEvent;
        State = state;
    }

    public IGameplayEvent Event { get; }
    public RunState State { get; }
    public double EventMultiplier { get; set; } = 1;
    internal IReadOnlyList<ScoreCandidate> Candidates => _candidates;

    public void Add(long amount, string reason, string source, string category, double multiplier = 1)
    {
        if (amount <= 0) return;
        _candidates.Add(new ScoreCandidate(amount, reason, source, category, multiplier));
    }
}

internal readonly record struct ScoreCandidate(
    long BaseAmount,
    string Reason,
    string Source,
    string Category,
    double Multiplier);

public sealed class ScoreRulePipeline
{
    private readonly IReadOnlyList<IScoreRule> _rules;

    public ScoreRulePipeline(IEnumerable<IScoreRule> rules) =>
        _rules = (rules ?? throw new ArgumentNullException(nameof(rules))).ToArray();

    public static ScoreRulePipeline Create(
        RuleSetDefinition ruleSet,
        RuntimeCapabilityRegistry capabilities)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (ruleSet.ScoreRules.Count == 0)
        {
            return new ScoreRulePipeline(new IScoreRule[] { new LegacyScoreRule() });
        }

        return new ScoreRulePipeline(ruleSet.ScoreRules.Select((definition, index) =>
            capabilities.ScoreRules.Resolve(
                definition.Type,
                $"rulesets/{ruleSet.Id}.json $.scoreRules[{index}]").Create(definition)));
    }

    public void Apply(
        IEnumerable<IGameplayEvent> gameplayEvents,
        RunState state,
        GameEventBuffer output)
    {
        ArgumentNullException.ThrowIfNull(gameplayEvents);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(output);
        foreach (var rule in _rules) rule.Advance(output.Frame, state);
        foreach (var gameplayEvent in gameplayEvents
            .Where(static item => item is not ScoreAwardedEvent)
            .OrderBy(static item => item.Sequence)
            .ToArray())
        {
            var context = new ScoreRuleContext(gameplayEvent, state);
            foreach (var rule in _rules) rule.Apply(context);
            foreach (var candidate in context.Candidates)
            {
                var multiplier = ClampMultiplier(
                    context.EventMultiplier * candidate.Multiplier * state.SpecialScoreMultiplier);
                var requested = ScaleSaturating(candidate.BaseAmount, multiplier);
                var awarded = state.AwardScore(candidate.Category, requested);
                if (awarded <= 0) continue;
                output.Publish((frame, sequence) => new ScoreAwardedEvent(
                    frame,
                    sequence,
                    candidate.Reason,
                    candidate.BaseAmount,
                    multiplier,
                    awarded,
                    candidate.Source,
                    candidate.Category));
            }
        }
    }

    private static double ClampMultiplier(double value) =>
        !double.IsFinite(value) || value <= 0 ? 1 : Math.Min(value, 1_000_000);

    private static long ScaleSaturating(long value, double multiplier)
    {
        var result = (decimal)value * (decimal)multiplier;
        return result >= long.MaxValue ? long.MaxValue : Math.Max(0, (long)decimal.Floor(result));
    }
}

internal sealed class LegacyScoreRule : IScoreRule
{
    public void Apply(ScoreRuleContext context)
    {
        switch (context.Event)
        {
            case EnemyDestroyedEvent destroyed when destroyed.BaseScore > 0:
                context.Add(destroyed.BaseScore, "enemy-destroyed", destroyed.EnemyDefinitionId, "enemy");
                break;
            case ItemCollectedEvent item when item.ScoreValue > 0:
                context.Add(item.ScoreValue, "item-collected", item.ItemDefinitionId, "item");
                break;
        }
    }
}

internal enum BuiltInScoreRuleKind
{
    BaseKill,
    Chain,
    HitCombo,
    Multiplier,
    PointBlank,
    Graze,
    ProjectileCancel,
    ItemGrowth,
    BossBonus,
    StageClear,
    ResourceConversion,
    ExtendThreshold
}

internal sealed class BuiltInScoreRuleFactory(
    string type,
    BuiltInScoreRuleKind kind) : IScoreRuleFactory
{
    public string Type { get; } = type;

    public static IReadOnlyList<IScoreRuleFactory> CreateAll() => new IScoreRuleFactory[]
    {
        new BuiltInScoreRuleFactory("base-kill", BuiltInScoreRuleKind.BaseKill),
        new BuiltInScoreRuleFactory("chain", BuiltInScoreRuleKind.Chain),
        new BuiltInScoreRuleFactory("hit-combo", BuiltInScoreRuleKind.HitCombo),
        new BuiltInScoreRuleFactory("multiplier", BuiltInScoreRuleKind.Multiplier),
        new BuiltInScoreRuleFactory("point-blank", BuiltInScoreRuleKind.PointBlank),
        new BuiltInScoreRuleFactory("graze", BuiltInScoreRuleKind.Graze),
        new BuiltInScoreRuleFactory("projectile-cancel", BuiltInScoreRuleKind.ProjectileCancel),
        new BuiltInScoreRuleFactory("item-growth", BuiltInScoreRuleKind.ItemGrowth),
        new BuiltInScoreRuleFactory("boss-bonus", BuiltInScoreRuleKind.BossBonus),
        new BuiltInScoreRuleFactory("stage-clear", BuiltInScoreRuleKind.StageClear),
        new BuiltInScoreRuleFactory("resource-conversion", BuiltInScoreRuleKind.ResourceConversion),
        new BuiltInScoreRuleFactory("extend-threshold", BuiltInScoreRuleKind.ExtendThreshold)
    };

    public void Validate(CapabilityDefinition capability, string path)
    {
        var parameters = kind switch
        {
            BuiltInScoreRuleKind.Chain => new[] { "timeoutFrames", "bonusPerChain" },
            BuiltInScoreRuleKind.HitCombo => new[] { "timeoutFrames", "bonusPerHit" },
            BuiltInScoreRuleKind.Multiplier => new[] { "base", "perChain", "perHit", "maximum" },
            BuiltInScoreRuleKind.PointBlank => new[] { "distance", "multiplier" },
            BuiltInScoreRuleKind.Graze or BuiltInScoreRuleKind.ProjectileCancel => new[] { "points" },
            BuiltInScoreRuleKind.ItemGrowth => new[] { "growthPerItem", "maximumMultiplier", "timeoutFrames" },
            BuiltInScoreRuleKind.StageClear => new[] { "stagePoints", "allClearPoints" },
            BuiltInScoreRuleKind.ResourceConversion => new[] { "lifePoints", "bombPoints" },
            _ => Array.Empty<string>()
        };
        CapabilityParameters.RequireOnly(capability, path, parameters);
        foreach (var name in parameters)
        {
            if (!capability.Parameters.TryGetValue(name, out var value) || value.ValueKind != JsonValueKind.Number)
            {
                throw new DefinitionValidationException($"Capability parameter '{path}.parameters.{name}' must be numeric.");
            }

            if (name is "base" or "perChain" or "perHit" or "maximum" or "distance" or "multiplier" or
                "growthPerItem" or "maximumMultiplier")
            {
                if (!value.TryGetDouble(out var number) || !double.IsFinite(number) || number < 0)
                {
                    throw new DefinitionValidationException($"Capability parameter '{path}.parameters.{name}' must be finite and non-negative.");
                }
            }
            else if (!value.TryGetInt64(out var integer) || integer < 0)
            {
                throw new DefinitionValidationException($"Capability parameter '{path}.parameters.{name}' must be a non-negative integer.");
            }
        }

        if (kind is BuiltInScoreRuleKind.Chain or BuiltInScoreRuleKind.HitCombo or BuiltInScoreRuleKind.ItemGrowth &&
            GetLong(capability, "timeoutFrames") <= 0)
        {
            throw new DefinitionValidationException($"Capability parameter '{path}.parameters.timeoutFrames' must be positive.");
        }

        if (kind is BuiltInScoreRuleKind.Multiplier or BuiltInScoreRuleKind.PointBlank or BuiltInScoreRuleKind.ItemGrowth &&
            parameters.Any(name => name is "maximum" or "distance" or "multiplier" or "maximumMultiplier" && GetDouble(capability, name) <= 0))
        {
            throw new DefinitionValidationException($"Capability at '{path}' requires positive limit and multiplier values.");
        }
    }

    public IScoreRule Create(CapabilityDefinition capability) => new BuiltInScoreRule(kind, capability);

    private static long GetLong(CapabilityDefinition definition, string name) =>
        definition.Parameters[name].GetInt64();

    private static double GetDouble(CapabilityDefinition definition, string name) =>
        definition.Parameters[name].GetDouble();
}

internal sealed class BuiltInScoreRule(
    BuiltInScoreRuleKind kind,
    CapabilityDefinition definition) : IScoreRule
{
    public void Advance(long frame, RunState state)
    {
        if (kind == BuiltInScoreRuleKind.Chain && state.LastKillFrame >= 0 &&
            frame - state.LastKillFrame > GetLong("timeoutFrames")) state.Chain = 0;
        if (kind == BuiltInScoreRuleKind.HitCombo && state.LastHitFrame >= 0 &&
            frame - state.LastHitFrame > GetLong("timeoutFrames")) state.HitCombo = 0;
        if (kind == BuiltInScoreRuleKind.ItemGrowth && state.LastItemFrame >= 0 &&
            frame - state.LastItemFrame > GetLong("timeoutFrames")) state.ConsecutiveItems = 0;
        if (kind == BuiltInScoreRuleKind.Multiplier) UpdateMultiplier(state);
    }

    public void Apply(ScoreRuleContext context)
    {
        switch (kind)
        {
            case BuiltInScoreRuleKind.BaseKill:
                if (context.Event is EnemyDestroyedEvent destroyed)
                {
                    context.Add(destroyed.BaseScore, "enemy-destroyed", destroyed.EnemyDefinitionId, "enemy");
                }

                break;
            case BuiltInScoreRuleKind.Chain:
                ApplyChain(context);
                break;
            case BuiltInScoreRuleKind.HitCombo:
                ApplyHitCombo(context);
                break;
            case BuiltInScoreRuleKind.Multiplier:
                ApplyMultiplier(context);
                break;
            case BuiltInScoreRuleKind.PointBlank:
                if (context.Event is EnemyDestroyedEvent { DistanceToPlayer: { } distance } &&
                    distance <= GetDouble("distance")) context.EventMultiplier *= GetDouble("multiplier");
                break;
            case BuiltInScoreRuleKind.Graze:
                if (context.Event is PlayerGrazedEvent graze)
                {
                    context.Add(GetLong("points"), "graze", graze.ProjectileEntityId.ToString(), "graze");
                }

                break;
            case BuiltInScoreRuleKind.ProjectileCancel:
                if (context.Event is ProjectileCancelledEvent { AwardsScore: true } cancelled)
                {
                    context.Add(GetLong("points"), "projectile-cancel", cancelled.ProjectileEntityId.ToString(), "cancel");
                }

                break;
            case BuiltInScoreRuleKind.ItemGrowth:
                ApplyItemGrowth(context);
                break;
            case BuiltInScoreRuleKind.BossBonus:
                if (context.Event is BossPhaseBonusEvent bonus)
                {
                    var total = SumSaturating(bonus.BaseBonus, bonus.TimeBonus, bonus.NoMissBonus, bonus.NoBombBonus);
                    context.Add(total, "boss-phase", $"{bonus.BossDefinitionId}/{bonus.PhaseId}", "boss");
                }

                break;
            case BuiltInScoreRuleKind.StageClear:
                if (context.Event is StageClearedEvent stage)
                {
                    context.Add(GetLong("stagePoints"), "stage-clear", stage.StageId, "clear");
                }
                else if (context.Event is AllClearedEvent allClear)
                {
                    context.Add(GetLong("allClearPoints"), "all-clear", allClear.StageId, "clear");
                }

                break;
            case BuiltInScoreRuleKind.ResourceConversion:
                if (context.Event is AllClearedEvent resources)
                {
                    var life = MultiplySaturating(GetLong("lifePoints"), resources.RemainingLives);
                    var bomb = MultiplySaturating(GetLong("bombPoints"), resources.RemainingBombs);
                    context.Add(SumSaturating(life, bomb), "remaining-resources", resources.StageId, "resource");
                }

                break;
            case BuiltInScoreRuleKind.ExtendThreshold:
                break;
        }
    }

    private void ApplyChain(ScoreRuleContext context)
    {
        if (context.Event is not EnemyDestroyedEvent destroyed) return;
        var timeout = GetLong("timeoutFrames");
        if (context.State.LastKillFrame < 0 || context.Event.Frame - context.State.LastKillFrame > timeout)
        {
            context.State.Chain = 0;
        }

        context.State.Chain++;
        context.State.MaximumChain = Math.Max(context.State.MaximumChain, context.State.Chain);
        context.State.LastKillFrame = context.Event.Frame;
        context.Add(
            MultiplySaturating(GetLong("bonusPerChain"), Math.Max(0, context.State.Chain - 1)),
            "chain",
            destroyed.EnemyDefinitionId,
            "chain");
    }

    private void ApplyHitCombo(ScoreRuleContext context)
    {
        if (context.Event is not ProjectileHitEvent { Team: ProjectileTeam.Player } hit) return;
        var timeout = GetLong("timeoutFrames");
        if (context.State.LastHitFrame < 0 || context.Event.Frame - context.State.LastHitFrame > timeout)
        {
            context.State.HitCombo = 0;
        }

        context.State.HitCombo++;
        context.State.LastHitFrame = context.Event.Frame;
        context.Add(
            MultiplySaturating(GetLong("bonusPerHit"), context.State.HitCombo),
            "hit-combo",
            hit.TargetEntityId.ToString(),
            "hit");
    }

    private void ApplyMultiplier(ScoreRuleContext context)
    {
        UpdateMultiplier(context.State);
        context.EventMultiplier *= context.State.Multiplier;
    }

    private void UpdateMultiplier(RunState state)
    {
        var value = GetDouble("base") +
            (Math.Max(0, state.Chain - 1) * GetDouble("perChain")) +
            (state.HitCombo * GetDouble("perHit"));
        state.Multiplier = Math.Clamp(value, 0, GetDouble("maximum"));
    }

    private void ApplyItemGrowth(ScoreRuleContext context)
    {
        if (context.Event is not ItemCollectedEvent { ScoreValue: > 0 } item) return;
        var timeout = GetLong("timeoutFrames");
        if (context.State.LastItemFrame < 0 || context.Event.Frame - context.State.LastItemFrame > timeout)
        {
            context.State.ConsecutiveItems = 0;
        }

        context.State.ConsecutiveItems++;
        context.State.LastItemFrame = context.Event.Frame;
        var multiplier = Math.Min(
            GetDouble("maximumMultiplier"),
            1 + ((context.State.ConsecutiveItems - 1) * GetDouble("growthPerItem")));
        context.Add(item.ScoreValue, "item-collected", item.ItemDefinitionId, "item", multiplier);
    }

    private long GetLong(string name) => definition.Parameters[name].GetInt64();
    private double GetDouble(string name) => definition.Parameters[name].GetDouble();

    private static long SumSaturating(params long[] values)
    {
        var result = 0L;
        foreach (var value in values) result = value > long.MaxValue - result ? long.MaxValue : result + value;
        return result;
    }

    private static long MultiplySaturating(long value, long multiplier) =>
        multiplier <= 0 || value <= 0 ? 0 : value > long.MaxValue / multiplier ? long.MaxValue : value * multiplier;
}
