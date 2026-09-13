namespace GoatShooooting.Platform;

public readonly record struct ScoreCategoryKey(
    string GameId,
    string RuleSetId,
    string DifficultyId,
    string ShipId)
{
    public const string LegacySelection = "legacy";

    public ScoreCategoryKey Normalize() => new(
        NormalizePart(GameId, "sample"),
        NormalizePart(RuleSetId, LegacySelection),
        NormalizePart(DifficultyId, LegacySelection),
        NormalizePart(ShipId, LegacySelection));

    [System.Text.Json.Serialization.JsonIgnore]
    public string StableId
    {
        get
        {
            var key = Normalize();
            return string.Join('|', Escape(key.GameId), Escape(key.RuleSetId), Escape(key.DifficultyId), Escape(key.ShipId));
        }
    }

    public static ScoreCategoryKey Legacy(string gameId) => new(
        gameId,
        LegacySelection,
        LegacySelection,
        LegacySelection);

    private static string NormalizePart(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string Escape(string value) => Uri.EscapeDataString(value);
}

public sealed record PlayerCategoryStats
{
    public ScoreCategoryKey Category { get; init; }
    public long BestScore { get; init; }
    public int ClearCount { get; init; }
    public int BestStage { get; init; }
    public int PlayCount { get; init; }
    public long PlayTimeFrames { get; init; }
}

public sealed record CompletedRunRecord
{
    public string RunId { get; init; } = string.Empty;
    public ScoreCategoryKey Category { get; init; }
    public long Score { get; init; }
    public bool Cleared { get; init; }
    public bool Continued { get; init; }
    public int BestStage { get; init; }
    public int MaximumChain { get; init; }
    public int Grazes { get; init; }
    public int Misses { get; init; }
    public int Bombs { get; init; }
    public int Continues { get; init; }
    public long PlayTimeFrames { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public IReadOnlyDictionary<string, long> ScoreBreakdown { get; init; } =
        new Dictionary<string, long>(StringComparer.Ordinal);
    public string? ReplayPath { get; init; }
}

public sealed record PlayerProfile
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string LastGameId { get; init; } = "sample";
    public Dictionary<string, int> HighScores { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> ClearCounts { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, PlayerCategoryStats> CategoryStats { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> LastRuleSetIds { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> LastDifficultyIds { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> LastShipIds { get; init; } = new(StringComparer.Ordinal);
    public HashSet<string> Unlocks { get; init; } = new(StringComparer.Ordinal);
    public HashSet<string> RecordedRunIds { get; init; } = new(StringComparer.Ordinal);

    public PlayerProfile WithHighScore(string gameId, int score)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        var normalized = Normalize();
        var highScores = new Dictionary<string, int>(normalized.HighScores, StringComparer.Ordinal);
        var candidate = Math.Max(0, score);
        if (!highScores.TryGetValue(gameId, out var current) || candidate > current) highScores[gameId] = candidate;
        return normalized with { HighScores = highScores };
    }

    public PlayerCategoryStats? GetStats(ScoreCategoryKey category) =>
        Normalize().CategoryStats.GetValueOrDefault(category.StableId);

    internal PlayerProfile Normalize()
    {
        var highScores = NormalizeIntValues(HighScores);
        var clearCounts = NormalizeIntValues(ClearCounts);
        var categories = NormalizeCategories(CategoryStats);
        foreach (var (gameId, score) in highScores)
        {
            var key = ScoreCategoryKey.Legacy(gameId);
            if (categories.ContainsKey(key.StableId)) continue;
            categories[key.StableId] = new PlayerCategoryStats
            {
                Category = key,
                BestScore = score,
                ClearCount = clearCounts.GetValueOrDefault(gameId)
            };
        }

        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            LastGameId = string.IsNullOrWhiteSpace(LastGameId) ? "sample" : LastGameId,
            HighScores = highScores,
            ClearCounts = clearCounts,
            CategoryStats = categories,
            LastRuleSetIds = NormalizeStrings(LastRuleSetIds),
            LastDifficultyIds = NormalizeStrings(LastDifficultyIds),
            LastShipIds = NormalizeStrings(LastShipIds),
            Unlocks = NormalizeSet(Unlocks),
            RecordedRunIds = NormalizeSet(RecordedRunIds)
        };
    }

    private static Dictionary<string, PlayerCategoryStats> NormalizeCategories(
        Dictionary<string, PlayerCategoryStats>? values)
    {
        var result = new Dictionary<string, PlayerCategoryStats>(StringComparer.Ordinal);
        foreach (var stats in values?.Values ?? Enumerable.Empty<PlayerCategoryStats>())
        {
            var category = stats.Category.Normalize();
            result[category.StableId] = stats with
            {
                Category = category,
                BestScore = Math.Max(0, stats.BestScore),
                ClearCount = Math.Max(0, stats.ClearCount),
                BestStage = Math.Max(0, stats.BestStage),
                PlayCount = Math.Max(0, stats.PlayCount),
                PlayTimeFrames = Math.Max(0, stats.PlayTimeFrames)
            };
        }

        return result;
    }

    private static Dictionary<string, int> NormalizeIntValues(Dictionary<string, int>? values)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, value) in values ?? new Dictionary<string, int>())
            if (!string.IsNullOrWhiteSpace(key)) result[key] = Math.Max(0, value);
        return result;
    }

    private static Dictionary<string, string> NormalizeStrings(Dictionary<string, string>? values)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in values ?? new Dictionary<string, string>())
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value)) result[key] = value;
        return result;
    }

    private static HashSet<string> NormalizeSet(HashSet<string>? values) => new(
        (values ?? []).Where(static value => !string.IsNullOrWhiteSpace(value)),
        StringComparer.Ordinal);
}
