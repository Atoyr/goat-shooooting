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
}

public sealed record EnemyDefinition
{
    public string Id { get; init; } = string.Empty;
    public int Hp { get; init; }
    public float Speed { get; init; }
    public string? WeaponId { get; init; }
    public float Radius { get; init; }
}

public sealed record BulletDefinition
{
    public string Id { get; init; } = string.Empty;
    public float Speed { get; init; }
    public int Damage { get; init; }
    public float Radius { get; init; }
    public float Lifetime { get; init; }
}

public sealed record WeaponDefinition
{
    public string Id { get; init; } = string.Empty;
    public string BulletId { get; init; } = string.Empty;
    public float Cooldown { get; init; }
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
}
