using System.Numerics;
using System.Text.Json;
using GoatShooooting.Core;
using GoatShooooting.Definitions;

namespace GoatShooooting.Runtime;

public enum SpecialGaugePhase
{
    Inactive,
    Active,
    Cooldown
}

public sealed record SpecialGaugeRule(
    string Activation,
    int StageCost,
    int MaximumLevel,
    double DamageCharge,
    double KillCharge,
    double GrazeCharge,
    double CancelCharge,
    double ItemCharge,
    double LockCharge,
    double DrainPerSecond,
    double KillExtensionSeconds,
    double CooldownSeconds,
    bool EndOnBomb,
    bool EndOnDeath,
    float DamageMultiplier,
    float FireIntervalMultiplier,
    double ScoreMultiplier,
    bool CancelProjectiles,
    bool Invincible,
    string VisualCue,
    string AudioCue);

public sealed record RankRule(
    double Initial,
    double Minimum,
    double Maximum,
    double DamageGain,
    double KillGain,
    double GrazeGain,
    double CancelGain,
    double ItemGain,
    double BombLoss,
    double DeathLoss,
    double DecayPerSecond,
    float BulletSpeedPerRank,
    float FireRatePerRank,
    double AdditionalProjectileEvery,
    double RevengeEvery,
    int RevengeCount,
    string RevengeProjectileId);

/// <summary>Resolved orthogonal modifiers consumed by factories and weapon systems.</summary>
public sealed class RunModifierState
{
    public DifficultyDefinition? Difficulty { get; private set; }
    public RankRule? RankRule { get; private set; }
    public SpecialGaugeRule? SpecialRule { get; private set; }
    public RunState? RunState { get; private set; }

    public void Configure(
        DifficultyDefinition? difficulty,
        RankRule? rankRule,
        SpecialGaugeRule? specialRule,
        RunState runState)
    {
        Difficulty = difficulty;
        RankRule = rankRule;
        SpecialRule = specialRule;
        RunState = runState ?? throw new ArgumentNullException(nameof(runState));
    }

    public float EnemyHealthMultiplier => Difficulty?.EnemyHpMultiplier ?? 1;
    public float EnemyProjectileSpeedMultiplier =>
        (Difficulty?.ProjectileSpeedMultiplier ?? 1) *
        (1 + ((float)(RunState?.Rank ?? 0) * (RankRule?.BulletSpeedPerRank ?? 0)));
    public float EnemyFireIntervalMultiplier => Math.Max(
        0.05f,
        (Difficulty?.FireIntervalMultiplier ?? 1) *
        (1 - ((float)(RunState?.Rank ?? 0) * (RankRule?.FireRatePerRank ?? 0))));
    public int AdditionalEnemyProjectiles =>
        (Difficulty?.AdditionalProjectileCount ?? 0) + GetRankProjectileCount();
    public float PlayerDamageMultiplier => IsSpecialActive ? SpecialRule!.DamageMultiplier : 1;
    public float PlayerFireIntervalMultiplier => IsSpecialActive ? SpecialRule!.FireIntervalMultiplier : 1;
    public double ScoreMultiplier => IsSpecialActive ? SpecialRule!.ScoreMultiplier : 1;
    public bool IsSpecialActive => RunState?.SpecialPhase == SpecialGaugePhase.Active && SpecialRule is not null;

    public bool IsPatternEnabled(IReadOnlyList<string> tags)
    {
        if (tags.Count == 0) return true;
        if (Difficulty is null) return false;
        return tags.Contains(Difficulty.Id, StringComparer.Ordinal) ||
            tags.Any(tag => Difficulty.PatternTags.Contains(tag, StringComparer.Ordinal));
    }

    private int GetRankProjectileCount()
    {
        if (RankRule is null || RankRule.AdditionalProjectileEvery <= 0) return 0;
        return Math.Max(0, (int)Math.Floor((RunState?.Rank ?? 0) / RankRule.AdditionalProjectileEvery));
    }
}

public sealed class SpecialGaugeSystem
{
    private readonly SpecialGaugeRule? _rule;
    private readonly int _maximumGauge;
    private bool _specialWasPressed;

    private SpecialGaugeSystem(SpecialGaugeRule? rule, int maximumGauge)
    {
        _rule = rule;
        _maximumGauge = maximumGauge;
    }

    public static SpecialGaugeSystem Create(RuleSetDefinition rules, RuntimeCapabilityRegistry capabilities)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (rules.SpecialGaugeRule is null) return new SpecialGaugeSystem(null, rules.MaximumGauge);
        var factory = capabilities.SpecialGaugeRules.Resolve(
            rules.SpecialGaugeRule.Type,
            $"rulesets/{rules.Id}.json $.specialGaugeRule");
        return new SpecialGaugeSystem(factory.Create(rules.SpecialGaugeRule), rules.MaximumGauge);
    }

    public SpecialGaugeRule? Rule => _rule;

    public void Reset(RunState state, int initialGauge, bool specialPressed)
    {
        state.SpecialGaugeValue = Math.Clamp(initialGauge, 0, _maximumGauge);
        state.Gauge = (int)state.SpecialGaugeValue;
        state.SpecialPhase = SpecialGaugePhase.Inactive;
        state.SpecialLevel = 0;
        state.SpecialTimeRemaining = 0;
        state.SpecialCooldownRemaining = 0;
        state.SpecialScoreMultiplier = 1;
        _specialWasPressed = specialPressed;
    }

    public void BeginTick(
        InputFrame input,
        float deltaTime,
        RunState state,
        Entity player,
        ProjectileStore projectiles,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        if (_rule is null) return;
        if (state.SpecialPhase == SpecialGaugePhase.Active)
        {
            state.SpecialGaugeValue = Math.Max(0, state.SpecialGaugeValue - (_rule.DrainPerSecond * deltaTime));
            state.SpecialTimeRemaining = _rule.DrainPerSecond <= 0
                ? double.PositiveInfinity
                : state.SpecialGaugeValue / _rule.DrainPerSecond;
            if (_rule.Invincible && player.TryGet<InvincibilityComponent>(out var invincibility))
            {
                invincibility.Remaining = Math.Max(invincibility.Remaining, deltaTime * 2);
            }

            if (state.SpecialGaugeValue <= 0) End(state, "drained", events);
        }
        else if (state.SpecialPhase == SpecialGaugePhase.Cooldown)
        {
            state.SpecialCooldownRemaining = Math.Max(0, state.SpecialCooldownRemaining - deltaTime);
            if (state.SpecialCooldownRemaining <= 0) state.SpecialPhase = SpecialGaugePhase.Inactive;
        }

        var pressed = input.IsPressed(InputButtons.Special);
        if (pressed && !_specialWasPressed && _rule.Activation is "manual" or "staged")
        {
            TryActivate(state, player.Id, projectiles, telemetry, events);
        }

        _specialWasPressed = pressed;
        state.SpecialScoreMultiplier = state.SpecialPhase == SpecialGaugePhase.Active
            ? _rule.ScoreMultiplier
            : 1;
        state.Gauge = Math.Max(0, (int)Math.Ceiling(state.SpecialGaugeValue));
    }

    public void Observe(
        IReadOnlyList<IGameplayEvent> gameplayEvents,
        RunState state,
        int playerEntityId,
        ProjectileStore projectiles,
        SimulationTelemetry telemetry,
        GameEventBuffer output)
    {
        if (_rule is null) return;
        var charge = 0d;
        foreach (var gameplayEvent in gameplayEvents.ToArray())
        {
            switch (gameplayEvent)
            {
                case EnemyDamagedEvent damaged:
                    charge += damaged.Damage * _rule.DamageCharge;
                    break;
                case EnemyDestroyedEvent:
                    charge += _rule.KillCharge;
                    if (state.SpecialPhase == SpecialGaugePhase.Active)
                    {
                        state.SpecialGaugeValue += _rule.KillExtensionSeconds * _rule.DrainPerSecond;
                    }
                    break;
                case PlayerGrazedEvent:
                    charge += _rule.GrazeCharge;
                    break;
                case ProjectileCancelledEvent:
                    charge += _rule.CancelCharge;
                    break;
                case ItemCollectedEvent:
                    charge += _rule.ItemCharge;
                    break;
                case TargetsLockedEvent locked:
                    charge += locked.Count * _rule.LockCharge;
                    break;
                case BombUsedEvent when _rule.EndOnBomb:
                    End(state, "bomb", output);
                    break;
                case PlayerDiedEvent when _rule.EndOnDeath:
                    End(state, "death", output);
                    break;
            }
        }

        if (state.SpecialPhase != SpecialGaugePhase.Cooldown)
        {
            var maximum = GaugeCapacity;
            state.SpecialGaugeValue = Math.Clamp(state.SpecialGaugeValue + charge, 0, maximum);
        }

        state.Gauge = Math.Max(0, (int)Math.Ceiling(state.SpecialGaugeValue));
        if (_rule.Activation == "automatic" && state.SpecialPhase == SpecialGaugePhase.Inactive &&
            state.SpecialGaugeValue >= GaugeCapacity)
        {
            TryActivate(state, playerEntityId, projectiles, telemetry, output);
        }
    }

    private void TryActivate(
        RunState state,
        int playerEntityId,
        ProjectileStore projectiles,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        if (_rule is null || state.SpecialPhase != SpecialGaugePhase.Inactive ||
            state.SpecialGaugeValue < _rule.StageCost) return;
        if (_rule.Activation != "staged" &&
            state.SpecialGaugeValue < GaugeCapacity) return;
        var level = Math.Clamp((int)(state.SpecialGaugeValue / _rule.StageCost), 1, _rule.MaximumLevel);
        if (_rule.Activation != "staged") level = _rule.MaximumLevel;
        var available = Math.Min(state.SpecialGaugeValue, level * _rule.StageCost);
        state.SpecialGaugeValue = available;
        state.SpecialPhase = SpecialGaugePhase.Active;
        state.SpecialScoreMultiplier = _rule.ScoreMultiplier;
        state.SpecialLevel = level;
        state.SpecialTimeRemaining = _rule.DrainPerSecond <= 0
            ? double.PositiveInfinity
            : available / _rule.DrainPerSecond;
        if (_rule.CancelProjectiles) CancelEnemyProjectiles(projectiles, telemetry, events);
        events.Publish((frame, sequence) => new SpecialActivatedEvent(
            frame, sequence, playerEntityId, level, _rule.VisualCue, _rule.AudioCue));
    }

    private void End(RunState state, string reason, GameEventBuffer events)
    {
        if (_rule is null || state.SpecialPhase != SpecialGaugePhase.Active) return;
        var level = state.SpecialLevel;
        state.SpecialGaugeValue = 0;
        state.Gauge = 0;
        state.SpecialLevel = 0;
        state.SpecialTimeRemaining = 0;
        state.SpecialCooldownRemaining = _rule.CooldownSeconds;
        state.SpecialScoreMultiplier = 1;
        state.SpecialPhase = _rule.CooldownSeconds > 0 ? SpecialGaugePhase.Cooldown : SpecialGaugePhase.Inactive;
        events.Publish((frame, sequence) => new SpecialEndedEvent(frame, sequence, level, reason));
    }

    private static void CancelEnemyProjectiles(
        ProjectileStore projectiles,
        SimulationTelemetry telemetry,
        GameEventBuffer events)
    {
        for (var index = 0; index < projectiles.ActiveCount; index++)
        {
            if (projectiles.IsPendingRemovalAt(index) || projectiles.TeamAt(index) != ProjectileTeam.Enemy ||
                !projectiles.CanBeCancelledAt(index)) continue;
            projectiles.QueueRemoveAt(index);
            telemetry.EnemyBulletsCleared++;
            var projectileId = projectiles.IdAt(index);
            events.Publish((frame, sequence) => new ProjectileCancelledEvent(frame, sequence, projectileId));
        }
    }

    private double GaugeCapacity => Math.Min(
        _maximumGauge,
        (double)_rule!.StageCost * _rule.MaximumLevel);
}

public sealed class RankSystem
{
    private readonly RankRule? _rule;
    private readonly BulletFactory _bulletFactory;

    private RankSystem(RankRule? rule, RuntimeCapabilityRegistry capabilities)
    {
        _rule = rule;
        _bulletFactory = new BulletFactory(capabilities);
    }

    public static RankSystem Create(RuleSetDefinition rules, RuntimeCapabilityRegistry capabilities)
    {
        if (rules.RankRule is null) return new RankSystem(null, capabilities);
        var factory = capabilities.RankRules.Resolve(rules.RankRule.Type, $"rulesets/{rules.Id}.json $.rankRule");
        return new RankSystem(factory.Create(rules.RankRule), capabilities);
    }

    public RankRule? Rule => _rule;

    public void Reset(RunState state) => state.Rank = _rule?.Initial ?? 0;

    public void SetInitial(RunState state, double rank)
    {
        if (!double.IsFinite(rank) || rank < 0) throw new ArgumentOutOfRangeException(nameof(rank));
        state.Rank = _rule is null ? rank : Math.Clamp(rank, _rule.Minimum, _rule.Maximum);
    }

    public void Observe(
        IReadOnlyList<IGameplayEvent> gameplayEvents,
        float deltaTime,
        RunState state,
        World world,
        DefinitionCatalog definitions,
        ProjectileStore projectiles,
        SimulationTelemetry telemetry,
        GameEventBuffer output)
    {
        if (_rule is null) return;
        var previous = state.Rank;
        state.Rank -= _rule.DecayPerSecond * deltaTime;
        foreach (var gameplayEvent in gameplayEvents.ToArray())
        {
            state.Rank += gameplayEvent switch
            {
                EnemyDamagedEvent damaged => damaged.Damage * _rule.DamageGain,
                EnemyDestroyedEvent => _rule.KillGain,
                PlayerGrazedEvent => _rule.GrazeGain,
                ProjectileCancelledEvent => _rule.CancelGain,
                ItemCollectedEvent => _rule.ItemGain,
                BombUsedEvent => -_rule.BombLoss,
                PlayerDiedEvent => -_rule.DeathLoss,
                _ => 0
            };
        }

        state.Rank = Math.Clamp(state.Rank, _rule.Minimum, _rule.Maximum);
        if (state.Rank != previous)
        {
            output.Publish((frame, sequence) => new RankChangedEvent(frame, sequence, previous, state.Rank));
        }

        SpawnRevengeProjectiles(gameplayEvents, state, world, definitions, projectiles, telemetry);
    }

    private void SpawnRevengeProjectiles(
        IReadOnlyList<IGameplayEvent> gameplayEvents,
        RunState state,
        World world,
        DefinitionCatalog definitions,
        ProjectileStore projectiles,
        SimulationTelemetry telemetry)
    {
        if (_rule is null || _rule.RevengeEvery <= 0 || _rule.RevengeCount <= 0 ||
            string.IsNullOrWhiteSpace(_rule.RevengeProjectileId) || state.Rank < _rule.RevengeEvery) return;
        var count = Math.Min(
            10_000,
            Math.Max(1, (int)Math.Floor(state.Rank / _rule.RevengeEvery)) * _rule.RevengeCount);
        foreach (var destroyed in gameplayEvents.OfType<EnemyDestroyedEvent>())
        {
            var enemy = OptionFollowSystem.FindEntity(world, destroyed.EnemyEntityId);
            if (enemy is null || !enemy.TryGet<TransformComponent>(out var transform)) continue;
            for (var index = 0; index < count; index++)
            {
                var angle = (MathF.PI * 2 * index) / count;
                _bulletFactory.Create(
                    projectiles,
                    definitions.GetProjectile(_rule.RevengeProjectileId),
                    transform.Position,
                    new Vector2(MathF.Cos(angle), MathF.Sin(angle)),
                    CollisionLayer.Enemy,
                    enemy.Id,
                    1,
                    1);
                telemetry.BulletsSpawned++;
                telemetry.EnemyBulletsSpawned++;
            }
        }
    }
}

public sealed class RunRuleSystem
{
    private readonly long? _timeLimitFrames;

    public RunRuleSystem(RuleSetDefinition rules)
    {
        if (rules.TimeLimitSeconds is { } seconds)
        {
            _timeLimitFrames = checked((long)Math.Ceiling(seconds * SimulationTiming.TicksPerSecond));
        }
    }

    public long? TimeLimitFrames => _timeLimitFrames;
    public bool IsTimeExpired(long frame) => _timeLimitFrames is { } limit && frame >= limit;
}

public readonly record struct RunDebugSnapshot(
    long Frame,
    double Rank,
    int Gauge,
    SpecialGaugePhase SpecialPhase,
    int SpecialLevel,
    double SpecialTimeRemaining,
    double SpecialCooldownRemaining);

internal sealed class StandardSpecialGaugeRuleFactory : ISpecialGaugeRuleFactory
{
    public string Type => "radiant-drive";

    public void Validate(CapabilityDefinition capability, string path) => _ = Parse(capability, path);
    public SpecialGaugeRule Create(CapabilityDefinition capability) => Parse(capability, Type);

    private static SpecialGaugeRule Parse(CapabilityDefinition value, string path)
    {
        Parameters.RequireOnly(value, path,
            "activation", "stageCost", "maximumLevel", "damageCharge", "killCharge", "grazeCharge",
            "cancelCharge", "itemCharge", "lockCharge", "drainPerSecond", "killExtensionSeconds",
            "cooldownSeconds", "endOnBomb", "endOnDeath", "damageMultiplier", "fireIntervalMultiplier",
            "scoreMultiplier", "cancelProjectiles", "invincible", "visualCue", "audioCue");
        var activation = Parameters.String(value, "activation", "manual");
        if (activation is not ("manual" or "automatic" or "staged"))
            throw new DefinitionValidationException($"Capability at '{path}' has unsupported activation '{activation}'.");
        return new SpecialGaugeRule(
            activation,
            Parameters.Int(value, "stageCost", 100, 1, 1_000_000),
            Parameters.Int(value, "maximumLevel", 1, 1, 100),
            Parameters.Number(value, "damageCharge"),
            Parameters.Number(value, "killCharge"),
            Parameters.Number(value, "grazeCharge"),
            Parameters.Number(value, "cancelCharge"),
            Parameters.Number(value, "itemCharge"),
            Parameters.Number(value, "lockCharge"),
            Parameters.Number(value, "drainPerSecond", 10),
            Parameters.Number(value, "killExtensionSeconds"),
            Parameters.Number(value, "cooldownSeconds"),
            Parameters.Boolean(value, "endOnBomb", true),
            Parameters.Boolean(value, "endOnDeath", true),
            (float)Parameters.Number(value, "damageMultiplier", 1, 0.01),
            (float)Parameters.Number(value, "fireIntervalMultiplier", 1, 0.01),
            Parameters.Number(value, "scoreMultiplier", 1, 0.01),
            Parameters.Boolean(value, "cancelProjectiles"),
            Parameters.Boolean(value, "invincible"),
            Parameters.String(value, "visualCue", string.Empty),
            Parameters.String(value, "audioCue", string.Empty));
    }
}

internal sealed class StandardRankRuleFactory : IRankRuleFactory
{
    public string Type => "dynamic-rank";

    public void Validate(CapabilityDefinition capability, string path) => _ = Parse(capability, path);
    public RankRule Create(CapabilityDefinition capability) => Parse(capability, Type);

    private static RankRule Parse(CapabilityDefinition value, string path)
    {
        Parameters.RequireOnly(value, path,
            "initial", "minimum", "maximum", "damageGain", "killGain", "grazeGain", "cancelGain",
            "itemGain", "bombLoss", "deathLoss", "decayPerSecond", "bulletSpeedPerRank",
            "fireRatePerRank", "additionalProjectileEvery", "revengeEvery", "revengeCount",
            "revengeProjectileId");
        var minimum = Parameters.Number(value, "minimum");
        var maximum = Parameters.Number(value, "maximum", 1);
        var initial = Parameters.Number(value, "initial");
        if (maximum < minimum || initial < minimum || initial > maximum)
            throw new DefinitionValidationException($"Capability at '{path}' requires minimum <= initial <= maximum.");
        var additionalProjectileEvery = Parameters.Number(value, "additionalProjectileEvery");
        var revengeEvery = Parameters.Number(value, "revengeEvery");
        if (additionalProjectileEvery > 0 && Math.Floor(maximum / additionalProjectileEvery) > 1_000)
            throw new DefinitionValidationException($"Capability at '{path}' can add more than 1000 projectiles per emitter.");
        if (revengeEvery > 0 && Math.Floor(maximum / revengeEvery) > 1_000)
            throw new DefinitionValidationException($"Capability at '{path}' can add more than 1000 revenge tiers.");
        return new RankRule(
            initial, minimum, maximum,
            Parameters.Number(value, "damageGain"), Parameters.Number(value, "killGain"),
            Parameters.Number(value, "grazeGain"), Parameters.Number(value, "cancelGain"),
            Parameters.Number(value, "itemGain"), Parameters.Number(value, "bombLoss"),
            Parameters.Number(value, "deathLoss"), Parameters.Number(value, "decayPerSecond"),
            (float)Parameters.Number(value, "bulletSpeedPerRank"),
            (float)Parameters.Number(value, "fireRatePerRank"),
            additionalProjectileEvery, revengeEvery,
            Parameters.Int(value, "revengeCount", 0, 0, 1_000), Parameters.String(value, "revengeProjectileId", string.Empty));
    }
}

internal static class Parameters
{
    public static void RequireOnly(CapabilityDefinition value, string path, params string[] allowed)
    {
        foreach (var name in value.Parameters.Keys)
            if (!allowed.Contains(name, StringComparer.Ordinal))
                throw new DefinitionValidationException($"Capability at '{path}.parameters.{name}' has an unknown parameter.");
    }

    public static double Number(CapabilityDefinition value, string name, double fallback = 0, double minimum = 0)
    {
        if (!value.Parameters.TryGetValue(name, out var element)) return fallback;
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var result) ||
            !double.IsFinite(result) || result < minimum)
            throw new DefinitionValidationException($"Capability parameter '{name}' must be a finite number >= {minimum}.");
        return result;
    }

    public static int Int(CapabilityDefinition value, string name, int fallback, int minimum, int maximum = int.MaxValue)
    {
        if (!value.Parameters.TryGetValue(name, out var element)) return fallback;
        if (!element.TryGetInt32(out var result) || result < minimum || result > maximum)
            throw new DefinitionValidationException(
                $"Capability parameter '{name}' must be an integer between {minimum} and {maximum}.");
        return result;
    }

    public static bool Boolean(CapabilityDefinition value, string name, bool fallback = false)
    {
        if (!value.Parameters.TryGetValue(name, out var element)) return fallback;
        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new DefinitionValidationException($"Capability parameter '{name}' must be boolean.");
        return element.GetBoolean();
    }

    public static string String(CapabilityDefinition value, string name, string fallback)
    {
        if (!value.Parameters.TryGetValue(name, out var element)) return fallback;
        if (element.ValueKind != JsonValueKind.String)
            throw new DefinitionValidationException($"Capability parameter '{name}' must be a string.");
        return element.GetString() ?? fallback;
    }
}
