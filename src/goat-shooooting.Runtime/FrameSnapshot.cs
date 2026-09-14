namespace GoatShooooting.Runtime;

/// <summary>Immutable, renderer-neutral state consumed by the presentation layer.</summary>
public sealed record FrameSnapshot(
    long Frame,
    IReadOnlyList<RenderItem> Items,
    long Score,
    int Chain,
    double Multiplier,
    int Power,
    int MaximumPower,
    int Gauge,
    int MaximumGauge,
    double Rank,
    int StageNumber,
    string StageId,
    int Lives,
    int Bombs,
    BossHudSnapshot? Boss);

public sealed record BossHudSnapshot(
    int EntityId,
    string DefinitionId,
    string Name,
    string PhaseName,
    float HealthFraction,
    float RemainingTime,
    bool Warning);
