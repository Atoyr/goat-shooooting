using System.Text.Json;

namespace GoatShooooting.Definitions;

public sealed record CapabilityDefinition
{
    public string Type { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, JsonElement> Parameters { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}

public sealed record ShipDefinition
{
    public int SchemaVersion { get; init; } = 2;
    public string Id { get; init; } = string.Empty;
    public float HitRadius { get; init; }
    public float GrazeRadius { get; init; }
    public float NormalSpeed { get; init; }
    public float FocusSpeed { get; init; }
    public int InitialLives { get; init; } = 2;
    public int InitialBombs { get; init; } = 2;
    public int InitialPower { get; init; }
    public IReadOnlyList<string> NormalWeaponIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FocusWeaponIds { get; init; } = Array.Empty<string>();
    public string? VisualId { get; init; }
    public string? AudioId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    internal bool MigratedFromV1 { get; init; }
}

public sealed record ProjectileDefinition
{
    public int SchemaVersion { get; init; } = 2;
    public string Id { get; init; } = string.Empty;
    public float Speed { get; init; }
    public int Damage { get; init; }
    public float HitRadius { get; init; }
    public float Lifetime { get; init; }
    public string VisualId { get; init; } = string.Empty;
    public CapabilityDefinition Behavior { get; init; } = new() { Type = "straight" };
    public bool CanDamage { get; init; } = true;
    public bool CanBeCancelled { get; init; } = true;
    public string CancelResistance { get; init; } = "soft";
    public int PierceCount { get; init; }
    public string DamageType { get; init; } = "normal";
    public string ClearBehavior { get; init; } = "remove";
    [System.Text.Json.Serialization.JsonIgnore]
    internal bool MigratedFromV1 { get; init; }
}

public sealed record ItemDefinition
{
    public int SchemaVersion { get; init; } = 2;
    public string Id { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public int Value { get; init; }
    public string VisualId { get; init; } = string.Empty;
}

public sealed record PatternDefinition
{
    public int SchemaVersion { get; init; } = 2;
    public string Id { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public IReadOnlyList<TimelineCommandDefinition> Commands { get; init; } =
        Array.Empty<TimelineCommandDefinition>();
}

public sealed record TimelineCommandDefinition
{
    public string Type { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, JsonElement> Parameters { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    public string? PatternId { get; init; }
    public int RepeatCount { get; init; } = 1;
    public int MaximumSpawnCount { get; init; }
}

public sealed record BossDefinition
{
    public int SchemaVersion { get; init; } = 2;
    public string Id { get; init; } = string.Empty;
    public string EnemyId { get; init; } = string.Empty;
    public IReadOnlyList<BossPhaseDefinition> Phases { get; init; } = Array.Empty<BossPhaseDefinition>();
}

public sealed record BossPhaseDefinition
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public int Hp { get; init; }
    public float TimeLimit { get; init; }
    public string? MotionPatternId { get; init; }
    public IReadOnlyList<string> AttackPatternIds { get; init; } = Array.Empty<string>();
}

public sealed record RuleSetDefinition
{
    public int SchemaVersion { get; init; } = 2;
    public string Id { get; init; } = string.Empty;
    public string StageRouteId { get; init; } = string.Empty;
    public IReadOnlyList<string> StageIds { get; init; } = Array.Empty<string>();
    public bool AllowContinue { get; init; } = true;
    public IReadOnlyList<CapabilityDefinition> ScoreRules { get; init; } = Array.Empty<CapabilityDefinition>();
    public CapabilityDefinition? SpecialGaugeRule { get; init; }
}

public sealed record DifficultyDefinition
{
    public int SchemaVersion { get; init; } = 2;
    public string Id { get; init; } = string.Empty;
    public float ProjectileSpeedMultiplier { get; init; } = 1;
    public float FireIntervalMultiplier { get; init; } = 1;
    public float EnemyHpMultiplier { get; init; } = 1;
    public int AdditionalProjectileCount { get; init; }
    public bool AutoBomb { get; init; }
}

public sealed record VisualDefinition
{
    public int SchemaVersion { get; init; } = 2;
    public string Id { get; init; } = string.Empty;
    public string AssetId { get; init; } = string.Empty;
}

public sealed record AudioDefinition
{
    public int SchemaVersion { get; init; } = 2;
    public string Id { get; init; } = string.Empty;
    public string AssetId { get; init; } = string.Empty;
}
