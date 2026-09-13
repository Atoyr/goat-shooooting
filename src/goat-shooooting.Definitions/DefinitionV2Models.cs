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
    public int MaximumPower { get; init; } = 100;
    public int MaximumLives { get; init; } = 9;
    public int MaximumBombs { get; init; } = 9;
    public float DeathAnimationSeconds { get; init; } = 0.35f;
    public float RespawnDelaySeconds { get; init; } = 0.45f;
    public float RespawnInvincibilitySeconds { get; init; } = 2;
    public int PowerLossOnDeath { get; init; } = 10;
    public int BombsAfterRespawn { get; init; } = 2;
    public float? RespawnX { get; init; }
    public float? RespawnY { get; init; }
    public IReadOnlyList<string> NormalWeaponIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FocusWeaponIds { get; init; } = Array.Empty<string>();
    public string? BombWeaponId { get; init; }
    public string? SpecialWeaponId { get; init; }
    public IReadOnlyList<OptionUnitDefinition> Options { get; init; } = Array.Empty<OptionUnitDefinition>();
    public IReadOnlyList<PowerLevelModifierDefinition> PowerLevels { get; init; } =
        Array.Empty<PowerLevelModifierDefinition>();
    public string? VisualId { get; init; }
    public string? AudioId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    internal bool MigratedFromV1 { get; init; }
}

public sealed record OptionUnitDefinition
{
    public string Id { get; init; } = string.Empty;
    public float OffsetX { get; init; }
    public float OffsetY { get; init; }
    public float FollowSpeed { get; init; } = 480;
    public float Radius { get; init; } = 5;
    public IReadOnlyList<string> NormalWeaponIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FocusWeaponIds { get; init; } = Array.Empty<string>();
    public string? VisualId { get; init; }
}

public sealed record PowerLevelModifierDefinition
{
    public int MinimumPower { get; init; }
    public int AdditionalProjectileCount { get; init; }
    public float DamageMultiplier { get; init; } = 1;
}

public sealed record EmitterDefinition
{
    public string Id { get; init; } = string.Empty;
    public string ProjectileId { get; init; } = string.Empty;
    public float OffsetX { get; init; }
    public float OffsetY { get; init; }
    public float FireInterval { get; init; }
    public int BurstCount { get; init; } = 1;
    public float BurstInterval { get; init; }
    public string AngleSource { get; init; } = "forward";
    public float FixedAngleDegrees { get; init; }
    public string Distribution { get; init; } = "single";
    public int ProjectileCount { get; init; } = 1;
    public float SpreadDegrees { get; init; }
    public float RotationDegreesPerShot { get; init; }
    public IReadOnlyList<float> SpeedMultipliers { get; init; } = new[] { 1f };
    public string SpeedMode { get; init; } = "fixed";
    public float MinimumSpeedMultiplier { get; init; } = 1;
    public float MaximumSpeedMultiplier { get; init; } = 1;
    public int SpeedLayerCount { get; init; } = 1;
    public float AccelerationPerSecond { get; init; }
    public IReadOnlyList<string> DifficultyTags { get; init; } = Array.Empty<string>();
    public bool UsesLegacyPattern { get; init; }
}

public sealed record LaserWeaponDefinition
{
    public int Damage { get; init; }
    public float DamageInterval { get; init; }
    public float Length { get; init; }
    public float Width { get; init; }
    public string VisualId { get; init; } = string.Empty;
    public string ProjectileInteraction { get; init; } = "none";
}

public sealed record LockOnWeaponDefinition
{
    public int MaximumTargets { get; init; } = 1;
    public float Range { get; init; }
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

public sealed record DropEntryDefinition
{
    public string ItemId { get; init; } = string.Empty;
    public int Count { get; init; } = 1;
    public float Chance { get; init; } = 1;
    public float ScatterSpeed { get; init; } = 80;
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
    public IReadOnlyList<string> DifficultyTags { get; init; } = Array.Empty<string>();
}

public sealed record BossDefinition
{
    public int SchemaVersion { get; init; } = 2;
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string EnemyId { get; init; } = string.Empty;
    public float WarningSeconds { get; init; } = 5;
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
    public float InvulnerabilitySeconds { get; init; }
    public string? CheckpointId { get; init; }
    public string StartProjectileCancel { get; init; } = "none";
    public string EndProjectileCancel { get; init; } = "soft";
    public long BaseBonus { get; init; }
    public long TimeBonusPerSecond { get; init; }
    public long NoMissBonus { get; init; }
    public long NoBombBonus { get; init; }
    public IReadOnlyList<DropEntryDefinition> DropTable { get; init; } = Array.Empty<DropEntryDefinition>();
}

public sealed record RuleSetDefinition
{
    public int SchemaVersion { get; init; } = 2;
    public string Id { get; init; } = string.Empty;
    public string StageRouteId { get; init; } = string.Empty;
    public IReadOnlyList<string> StageIds { get; init; } = Array.Empty<string>();
    public bool AllowContinue { get; init; } = true;
    public int InitialCredits { get; init; }
    public int ContinueCreditCost { get; init; } = 1;
    public int ManualBombCost { get; init; } = 1;
    public int AutoBombCost { get; init; } = 1;
    public float BombInvincibilitySeconds { get; init; } = 1;
    public bool DeathClearsProjectiles { get; init; } = true;
    public IReadOnlyList<long> ExtendScoreThresholds { get; init; } = Array.Empty<long>();
    public float CollectionLineY { get; init; } = 120;
    public float ItemFallSpeed { get; init; } = 90;
    public float ItemMagnetSpeed { get; init; } = 480;
    public float FocusMagnetRadius { get; init; } = 120;
    public float ItemCollectionRadius { get; init; } = 18;
    public int MaximumGauge { get; init; } = 100;
    public int? InitialLives { get; init; }
    public int? InitialBombs { get; init; }
    public int? InitialPower { get; init; }
    public int InitialGauge { get; init; }
    public float? TimeLimitSeconds { get; init; }
    public string ClearCondition { get; init; } = "route-complete";
    public int MaximumPowerItemScoreValue { get; init; } = 1000;
    public IReadOnlyList<CapabilityDefinition> ScoreRules { get; init; } = Array.Empty<CapabilityDefinition>();
    public CapabilityDefinition? SpecialGaugeRule { get; init; }
    public CapabilityDefinition? RankRule { get; init; }
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
    public IReadOnlyList<string> PatternTags { get; init; } = Array.Empty<string>();
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
