using System.Numerics;
using System.Text.Json;
using GoatShooooting.Core;
using GoatShooooting.Definitions;

namespace GoatShooooting.Runtime;

public interface IRuntimeCapability
{
    string Type { get; }
}

public interface IProjectileBehaviorFactory : IRuntimeCapability
{
    void Validate(CapabilityDefinition capability, string path);
    ProjectileBehaviorConfiguration Create(CapabilityDefinition capability);
}

public interface IActorMotionFactory : IRuntimeCapability
{
    void Validate(CapabilityDefinition capability, string path);
    void Apply(Entity entity, Vector2 origin, CapabilityDefinition capability);
}

public interface IFirePatternFactory : IRuntimeCapability
{
    void Validate(CapabilityDefinition capability, string path);
    IEnumerable<Vector2> GetDirections(
        CapabilityDefinition capability,
        WeaponHolderComponent holder,
        Vector2 baseDirection);
    void Advance(CapabilityDefinition capability, WeaponHolderComponent holder);
}

public interface IScoreRuleFactory : IRuntimeCapability
{
    void Validate(CapabilityDefinition capability, string path);
}

public interface ISpecialGaugeRuleFactory : IRuntimeCapability
{
    void Validate(CapabilityDefinition capability, string path);
}

public interface IStageEventHandler : IRuntimeCapability
{
    void Validate(StageEventDefinition stageEvent, string path);
    void Execute(
        World world,
        DefinitionCatalog definitions,
        StageEventDefinition stageEvent,
        int spawnIndex,
        EnemyFactory enemyFactory);
}

public readonly record struct ProjectileBehaviorConfiguration(
    ProjectileBehavior Behavior,
    float HomingTurnRadiansPerSecond);

public sealed class CapabilityRegistry<TCapability> where TCapability : IRuntimeCapability
{
    private readonly Dictionary<string, TCapability> _capabilities = new(StringComparer.Ordinal);

    public CapabilityRegistry(IEnumerable<TCapability>? capabilities = null)
    {
        foreach (var capability in capabilities ?? Array.Empty<TCapability>()) Register(capability);
    }

    public IReadOnlyCollection<string> Types => _capabilities.Keys;

    public void Register(TCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (string.IsNullOrWhiteSpace(capability.Type))
        {
            throw new ArgumentException("Capability type must not be empty.", nameof(capability));
        }

        if (!_capabilities.TryAdd(capability.Type, capability))
        {
            throw new ArgumentException($"Capability type '{capability.Type}' is already registered.", nameof(capability));
        }
    }

    public TCapability Resolve(string type, string path)
    {
        if (string.IsNullOrWhiteSpace(type) || !_capabilities.TryGetValue(type, out var capability))
        {
            throw new DefinitionValidationException(
                $"Capability at '{path}' uses unsupported type '{type}'.");
        }

        return capability;
    }
}

public sealed class RuntimeCapabilityRegistry
{
    public RuntimeCapabilityRegistry(
        IEnumerable<IProjectileBehaviorFactory>? projectileBehaviors = null,
        IEnumerable<IActorMotionFactory>? actorMotions = null,
        IEnumerable<IFirePatternFactory>? firePatterns = null,
        IEnumerable<IScoreRuleFactory>? scoreRules = null,
        IEnumerable<ISpecialGaugeRuleFactory>? specialGaugeRules = null,
        IEnumerable<IStageEventHandler>? stageEventHandlers = null)
    {
        ProjectileBehaviors = new(projectileBehaviors);
        ActorMotions = new(actorMotions);
        FirePatterns = new(firePatterns);
        ScoreRules = new(scoreRules);
        SpecialGaugeRules = new(specialGaugeRules);
        StageEventHandlers = new(stageEventHandlers);
    }

    public CapabilityRegistry<IProjectileBehaviorFactory> ProjectileBehaviors { get; }
    public CapabilityRegistry<IActorMotionFactory> ActorMotions { get; }
    public CapabilityRegistry<IFirePatternFactory> FirePatterns { get; }
    public CapabilityRegistry<IScoreRuleFactory> ScoreRules { get; }
    public CapabilityRegistry<ISpecialGaugeRuleFactory> SpecialGaugeRules { get; }
    public CapabilityRegistry<IStageEventHandler> StageEventHandlers { get; }

    public static RuntimeCapabilityRegistry CreateBuiltIn() => new(
        projectileBehaviors: new IProjectileBehaviorFactory[]
        {
            new BuiltInProjectileBehaviorFactory("straight", ProjectileBehavior.Straight),
            new BuiltInProjectileBehaviorFactory("homing", ProjectileBehavior.Homing)
        },
        actorMotions: new IActorMotionFactory[]
        {
            new BuiltInActorMotionFactory("straight", ActorMotionKind.Straight),
            new BuiltInActorMotionFactory("sine", ActorMotionKind.Sine),
            new BuiltInActorMotionFactory("zigzag", ActorMotionKind.Zigzag)
        },
        firePatterns: new IFirePatternFactory[]
        {
            new BuiltInFirePatternFactory("spread", FirePatternKind.Spread),
            new BuiltInFirePatternFactory("washing-machine", FirePatternKind.WashingMachine),
            new BuiltInFirePatternFactory("double-washing-machine", FirePatternKind.DoubleWashingMachine)
        },
        stageEventHandlers: new IStageEventHandler[] { new SpawnEnemyStageEventHandler() });
}

public sealed class CapabilityValidator
{
    public void Validate(DefinitionCatalog definitions, RuntimeCapabilityRegistry capabilities)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(capabilities);

        foreach (var enemy in definitions.Enemies.Values)
        {
            var path = $"enemies/{enemy.Id}.json $.motion";
            capabilities.ActorMotions.Resolve(enemy.Motion!.Type, path).Validate(enemy.Motion, path);
        }

        foreach (var bullet in definitions.Bullets.Values)
        {
            ValidateProjectileBehavior(bullet.Behavior!, $"bullets/{bullet.Id}.json $.behavior", capabilities);
        }

        foreach (var projectile in definitions.Projectiles.Values)
        {
            ValidateProjectileBehavior(
                projectile.Behavior,
                $"projectiles/{projectile.Id}.json $.behavior",
                capabilities);
        }

        foreach (var weapon in definitions.Weapons.Values)
        {
            var path = $"weapons/{weapon.Id}.json $.pattern";
            capabilities.FirePatterns.Resolve(weapon.Pattern!.Type, path).Validate(weapon.Pattern, path);
        }

        foreach (var stage in definitions.Stages.Values)
        {
            for (var index = 0; index < stage.Events.Count; index++)
            {
                var stageEvent = stage.Events[index];
                var path = $"stages/{stage.Id}.json $.events[{index}].type";
                capabilities.StageEventHandlers.Resolve(stageEvent.Type, path).Validate(stageEvent, path);
            }
        }

        foreach (var ruleSet in definitions.RuleSets.Values)
        {
            for (var index = 0; index < ruleSet.ScoreRules.Count; index++)
            {
                var rule = ruleSet.ScoreRules[index];
                var path = $"rulesets/{ruleSet.Id}.json $.scoreRules[{index}]";
                capabilities.ScoreRules.Resolve(rule.Type, path).Validate(rule, path);
            }

            if (ruleSet.SpecialGaugeRule is not null)
            {
                var path = $"rulesets/{ruleSet.Id}.json $.specialGaugeRule";
                capabilities.SpecialGaugeRules.Resolve(ruleSet.SpecialGaugeRule.Type, path)
                    .Validate(ruleSet.SpecialGaugeRule, path);
            }
        }

        // Timeline commands are a closed, validated language owned by DefinitionCatalog and
        // interpreted by TimelineSystems. They are deliberately not extensible capabilities.
    }

    private static void ValidateProjectileBehavior(
        CapabilityDefinition behavior,
        string path,
        RuntimeCapabilityRegistry capabilities) =>
        capabilities.ProjectileBehaviors.Resolve(behavior.Type, path).Validate(behavior, path);
}

internal sealed class BuiltInProjectileBehaviorFactory(
    string type,
    ProjectileBehavior behavior) : IProjectileBehaviorFactory
{
    public string Type { get; } = type;

    public void Validate(CapabilityDefinition capability, string path)
    {
        if (behavior == ProjectileBehavior.Homing)
        {
            CapabilityParameters.RequireOnly(capability, path, "turnDegreesPerSecond");
            _ = CapabilityParameters.GetFloat(capability, "turnDegreesPerSecond", path, float.Epsilon, 1_440);
        }
        else
        {
            CapabilityParameters.RequireOnly(capability, path);
        }
    }

    public ProjectileBehaviorConfiguration Create(CapabilityDefinition capability) => new(
        behavior,
        behavior == ProjectileBehavior.Homing
            ? CapabilityParameters.GetFloat(capability, "turnDegreesPerSecond", Type, float.Epsilon, 1_440) *
              (MathF.PI / 180)
            : 0);
}

internal enum ActorMotionKind
{
    Straight,
    Sine,
    Zigzag
}

internal sealed class BuiltInActorMotionFactory(string type, ActorMotionKind kind) : IActorMotionFactory
{
    public string Type { get; } = type;

    public void Validate(CapabilityDefinition capability, string path)
    {
        if (kind == ActorMotionKind.Straight)
        {
            CapabilityParameters.RequireOnly(capability, path);
            return;
        }

        CapabilityParameters.RequireOnly(capability, path, "amplitude", "frequency");
        _ = CapabilityParameters.GetFloat(capability, "amplitude", path, float.Epsilon, 100_000);
        _ = CapabilityParameters.GetFloat(capability, "frequency", path, float.Epsilon, 1_000);
    }

    public void Apply(Entity entity, Vector2 origin, CapabilityDefinition capability)
    {
        if (kind == ActorMotionKind.Straight) return;
        var amplitude = CapabilityParameters.GetFloat(capability, "amplitude", Type, float.Epsilon, 100_000);
        var frequency = CapabilityParameters.GetFloat(capability, "frequency", Type, float.Epsilon, 1_000);
        if (kind == ActorMotionKind.Sine)
        {
            entity.Add(new SineMovementComponent(origin.X, amplitude, frequency));
        }
        else
        {
            entity.Add(new ZigzagMovementComponent(origin.X, amplitude, frequency));
        }
    }
}

internal enum FirePatternKind
{
    Spread,
    WashingMachine,
    DoubleWashingMachine
}

internal sealed class BuiltInFirePatternFactory(string type, FirePatternKind kind) : IFirePatternFactory
{
    public string Type { get; } = type;

    public void Validate(CapabilityDefinition capability, string path)
    {
        if (kind == FirePatternKind.Spread)
        {
            CapabilityParameters.RequireOnly(capability, path, "projectileCount", "spreadDegrees");
            _ = CapabilityParameters.GetInt(capability, "projectileCount", path, 1, 10_000);
            _ = CapabilityParameters.GetFloat(capability, "spreadDegrees", path, 0, 180);
            return;
        }

        CapabilityParameters.RequireOnly(
            capability,
            path,
            "projectileCount",
            "rotationDegreesPerShot",
            "rotationSwitchShots");
        _ = CapabilityParameters.GetInt(capability, "projectileCount", path, 1, 10_000);
        _ = CapabilityParameters.GetFloat(capability, "rotationDegreesPerShot", path, float.Epsilon, 360);
        _ = CapabilityParameters.GetInt(capability, "rotationSwitchShots", path, 1, 1_000_000);
    }

    public IEnumerable<Vector2> GetDirections(
        CapabilityDefinition capability,
        WeaponHolderComponent holder,
        Vector2 baseDirection)
    {
        var projectileCount = CapabilityParameters.GetInt(capability, "projectileCount", Type, 1, 10_000);
        if (kind == FirePatternKind.Spread)
        {
            var spreadDegrees = CapabilityParameters.GetFloat(capability, "spreadDegrees", Type, 0, 180);
            for (var index = 0; index < projectileCount; index++)
            {
                var offset = projectileCount == 1 ? 0 : ((float)index / (projectileCount - 1)) - 0.5f;
                yield return RotateDegrees(baseDirection, offset * spreadDegrees);
            }

            yield break;
        }

        var armSpacing = 360f / projectileCount;
        for (var arm = 0; arm < projectileCount; arm++)
        {
            yield return RotateDegrees(baseDirection, holder.PatternAngleDegrees + (arm * armSpacing));
        }

        if (kind == FirePatternKind.DoubleWashingMachine)
        {
            for (var arm = 0; arm < projectileCount; arm++)
            {
                yield return RotateDegrees(baseDirection, -holder.PatternAngleDegrees + ((arm + 0.5f) * armSpacing));
            }
        }
    }

    public void Advance(CapabilityDefinition capability, WeaponHolderComponent holder)
    {
        if (kind == FirePatternKind.Spread) return;
        var degrees = CapabilityParameters.GetFloat(capability, "rotationDegreesPerShot", Type, float.Epsilon, 360);
        holder.PatternAngleDegrees = NormalizeDegrees(holder.PatternAngleDegrees + (degrees * holder.PatternDirection));
        holder.ShotsSinceDirectionChange++;
        var switchShots = CapabilityParameters.GetInt(capability, "rotationSwitchShots", Type, 1, 1_000_000);
        if (holder.ShotsSinceDirectionChange >= switchShots)
        {
            holder.PatternDirection *= -1;
            holder.ShotsSinceDirectionChange = 0;
        }
    }

    private static Vector2 RotateDegrees(Vector2 vector, float degrees)
    {
        var radians = degrees * (MathF.PI / 180);
        var cosine = MathF.Cos(radians);
        var sine = MathF.Sin(radians);
        return new Vector2((vector.X * cosine) - (vector.Y * sine), (vector.X * sine) + (vector.Y * cosine));
    }

    private static float NormalizeDegrees(float angle)
    {
        angle %= 360;
        return angle < 0 ? angle + 360 : angle;
    }
}

internal sealed class SpawnEnemyStageEventHandler : IStageEventHandler
{
    public string Type => "spawn-enemy";

    public void Validate(StageEventDefinition stageEvent, string path)
    {
        if (string.IsNullOrWhiteSpace(stageEvent.EnemyId))
        {
            throw new DefinitionValidationException($"Stage event at '{path}' requires enemyId.");
        }
    }

    public void Execute(
        World world,
        DefinitionCatalog definitions,
        StageEventDefinition stageEvent,
        int spawnIndex,
        EnemyFactory enemyFactory) => enemyFactory.Create(
            world,
            definitions.GetEnemy(stageEvent.EnemyId),
            new Vector2(stageEvent.X + (spawnIndex * stageEvent.SpacingX), stageEvent.Y),
            stageEvent.IsBoss);
}

internal static class CapabilityParameters
{
    public static void RequireOnly(CapabilityDefinition capability, string path, params string[] allowed)
    {
        foreach (var name in capability.Parameters.Keys)
        {
            if (!allowed.Contains(name, StringComparer.Ordinal))
            {
                throw new DefinitionValidationException(
                    $"Capability at '{path}.parameters.{name}' has an unknown parameter.");
            }
        }
    }

    public static float GetFloat(
        CapabilityDefinition capability,
        string name,
        string path,
        float minimum,
        float maximum)
    {
        if (!capability.Parameters.TryGetValue(name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetSingle(out var result) ||
            !float.IsFinite(result) || result < minimum || result > maximum)
        {
            throw new DefinitionValidationException(
                $"Capability parameter '{path}.parameters.{name}' must be a number between {minimum} and {maximum}.");
        }

        return result;
    }

    public static int GetInt(
        CapabilityDefinition capability,
        string name,
        string path,
        int minimum,
        int maximum)
    {
        if (!capability.Parameters.TryGetValue(name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var result) || result < minimum || result > maximum)
        {
            throw new DefinitionValidationException(
                $"Capability parameter '{path}.parameters.{name}' must be an integer between {minimum} and {maximum}.");
        }

        return result;
    }
}
