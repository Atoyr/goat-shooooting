using System.Text.Json.Serialization;

namespace GoatShooooting.Definitions;

/// <summary>Static, serializable game configuration. It deliberately contains no runtime state.</summary>
public sealed record GameDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = string.Empty;
    public string PlayerId { get; init; } = string.Empty;
    public string StageId { get; init; } = string.Empty;
    public string DefaultRuleSetId { get; init; } = string.Empty;
    public IReadOnlyList<string> RuleSetIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DifficultyIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ShipIds { get; init; } = Array.Empty<string>();
    public string StageRouteId { get; init; } = string.Empty;
    public int Width { get; init; } = 800;
    public int Height { get; init; } = 720;
    public string ScreenLayout { get; init; } = "full";
    public int HudPanelWidth { get; init; } = 200;
    public string ScorePosition { get; init; } = "playfield-top-right";
}

public sealed record PlayerDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = string.Empty;
    public int Lives { get; init; } = 2;
    public int Bombs { get; init; } = 2;
    public int BombDamage { get; init; } = 50;
    public float Speed { get; init; }
    public string WeaponId { get; init; } = string.Empty;
    public float X { get; init; }
    public float Y { get; init; }
    public float Radius { get; init; }
    public float InvincibilitySeconds { get; init; } = 1;
}

public sealed record EnemyDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = string.Empty;
    public int Hp { get; init; }
    public float Speed { get; init; }
    public string? WeaponId { get; init; }
    public float Radius { get; init; }
    public int Score { get; init; } = 100;
    public string MovementPattern { get; init; } = "straight";
    public float MovementAmplitude { get; init; }
    public float MovementFrequency { get; init; }
    public CapabilityDefinition? Motion { get; init; }
    public string? MotionPatternId { get; init; }
    public IReadOnlyList<string> AttackPatternIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<DropEntryDefinition> DropTable { get; init; } = Array.Empty<DropEntryDefinition>();
    [JsonIgnore]
    internal bool MigratedFromV1 { get; init; }
}

public sealed record BulletDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = string.Empty;
    public float Speed { get; init; }
    public int Damage { get; init; }
    public float Radius { get; init; }
    public float Lifetime { get; init; }
    public string MovementPattern { get; init; } = "straight";
    public float HomingTurnDegreesPerSecond { get; init; } = 180;
    public CapabilityDefinition? Behavior { get; init; }
    [JsonIgnore]
    internal bool MigratedFromV1 { get; init; }
}

public sealed record WeaponDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = string.Empty;
    public string BulletId { get; init; } = string.Empty;
    public string ProjectileId { get; init; } = string.Empty;
    public float Cooldown { get; init; }
    public int ProjectileCount { get; init; } = 1;
    public float SpreadDegrees { get; init; }
    public string FirePattern { get; init; } = "spread";
    public float RotationDegreesPerShot { get; init; } = 12;
    public int RotationSwitchShots { get; init; } = 24;
    public CapabilityDefinition? Pattern { get; init; }
    public string ActionType { get; init; } = "projectile";
    public IReadOnlyList<EmitterDefinition> Emitters { get; init; } = Array.Empty<EmitterDefinition>();
    public LaserWeaponDefinition? Laser { get; init; }
    public LockOnWeaponDefinition? LockOn { get; init; }
    [JsonIgnore]
    internal bool MigratedFromV1 { get; init; }
}

public sealed record StageDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public float OpeningDuration { get; init; }
    public float ResultsDuration { get; init; }
    public string? NextStageId { get; init; }
    public string? BackgroundId { get; init; }
    public IReadOnlyList<StageEventDefinition> Events { get; init; } = Array.Empty<StageEventDefinition>();
    public IReadOnlyList<StageObjectiveDefinition> Objectives { get; init; } = Array.Empty<StageObjectiveDefinition>();
}

public sealed record StageObjectiveDefinition
{
    public string Type { get; init; } = string.Empty;
    public string? BossId { get; init; }
}

public sealed record StageEventDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public float Time { get; init; }
    public string Type { get; init; } = string.Empty;
    public string EnemyId { get; init; } = string.Empty;
    public float X { get; init; }
    public float Y { get; init; }
    public int Count { get; init; } = 1;
    public float SpawnInterval { get; init; }
    public float SpacingX { get; init; }
    public bool IsBoss { get; init; }
    public string? BossId { get; init; }
}
