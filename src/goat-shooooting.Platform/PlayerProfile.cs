namespace GoatShooooting.Platform;

public sealed record PlayerProfile
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string LastGameId { get; init; } = "sample";
    public Dictionary<string, int> HighScores { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> ClearCounts { get; init; } = new(StringComparer.Ordinal);

    public PlayerProfile WithHighScore(string gameId, int score)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        var normalized = Normalize();
        var highScores = new Dictionary<string, int>(normalized.HighScores, StringComparer.Ordinal);
        var candidate = Math.Max(0, score);
        if (!highScores.TryGetValue(gameId, out var current) || candidate > current)
        {
            highScores[gameId] = candidate;
        }

        return normalized with { HighScores = highScores };
    }

    internal PlayerProfile Normalize() => this with
    {
        SchemaVersion = CurrentSchemaVersion,
        LastGameId = string.IsNullOrWhiteSpace(LastGameId) ? "sample" : LastGameId,
        HighScores = NormalizeValues(HighScores),
        ClearCounts = NormalizeValues(ClearCounts)
    };

    private static Dictionary<string, int> NormalizeValues(Dictionary<string, int>? values)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (values is null)
        {
            return result;
        }

        foreach (var (key, value) in values)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = Math.Max(0, value);
            }
        }

        return result;
    }
}
