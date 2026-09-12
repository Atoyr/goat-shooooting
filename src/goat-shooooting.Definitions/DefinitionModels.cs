namespace GoatShooooting.Definitions;

/// <summary>Static, serializable game configuration. It deliberately contains no runtime state.</summary>
public sealed record GameDefinition
{
    public string PlayerId { get; init; } = string.Empty;
    public string StageId { get; init; } = string.Empty;
    public int Width { get; init; } = 800;
    public int Height { get; init; } = 720;
}

public sealed record PlayerDefinition
{
    public string Id { get; init; } = string.Empty;
    public int Hp { get; init; }
    public float Speed { get; init; }
    public string WeaponId { get; init; } = string.Empty;
    public float X { get; init; }
    public float Y { get; init; }
    public float Radius { get; init; }
    public float InvincibilitySeconds { get; init; } = 1;
}

public sealed record EnemyDefinition
{
    public string Id { get; init; } = string.Empty;
    public int Hp { get; init; }
    public float Speed { get; init; }
    public string? WeaponId { get; init; }
    public float Radius { get; init; }
    public int Score { get; init; } = 100;
    public string MovementPattern { get; init; } = "straight";
    public float MovementAmplitude { get; init; }
    public float MovementFrequency { get; init; }
}

public sealed record BulletDefinition
{
    public string Id { get; init; } = string.Empty;
    public float Speed { get; init; }
    public int Damage { get; init; }
    public float Radius { get; init; }
    public float Lifetime { get; init; }
    public string MovementPattern { get; init; } = "straight";
    public float HomingTurnDegreesPerSecond { get; init; } = 180;
}

public sealed record WeaponDefinition
{
    public string Id { get; init; } = string.Empty;
    public string BulletId { get; init; } = string.Empty;
    public float Cooldown { get; init; }
    public int ProjectileCount { get; init; } = 1;
    public float SpreadDegrees { get; init; }
    public string FirePattern { get; init; } = "spread";
    public float RotationDegreesPerShot { get; init; } = 12;
    public int RotationSwitchShots { get; init; } = 24;
}

public sealed record StageDefinition
{
    public string Id { get; init; } = string.Empty;
    public IReadOnlyList<StageEventDefinition> Events { get; init; } = Array.Empty<StageEventDefinition>();
}

public sealed record StageEventDefinition
{
    public float Time { get; init; }
    public string Type { get; init; } = string.Empty;
    public string EnemyId { get; init; } = string.Empty;
    public float X { get; init; }
    public float Y { get; init; }
    public int Count { get; init; } = 1;
    public float SpawnInterval { get; init; }
    public float SpacingX { get; init; }
}
