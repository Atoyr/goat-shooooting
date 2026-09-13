namespace GoatShooooting.Platform;

public sealed class PlayerProfileService
{
    public PlayerProfile SelectGame(PlayerProfile profile, string gameId)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        return profile.Normalize() with { LastGameId = gameId };
    }

    public PlayerProfile SelectRunConfiguration(PlayerProfile profile, ScoreCategoryKey category)
    {
        ArgumentNullException.ThrowIfNull(profile);
        category = category.Normalize();
        var ruleSets = new Dictionary<string, string>(profile.LastRuleSetIds, StringComparer.Ordinal)
        {
            [category.GameId] = category.RuleSetId
        };
        var difficulties = new Dictionary<string, string>(profile.LastDifficultyIds, StringComparer.Ordinal)
        {
            [category.GameId] = category.DifficultyId
        };
        var ships = new Dictionary<string, string>(profile.LastShipIds, StringComparer.Ordinal)
        {
            [category.GameId] = category.ShipId
        };
        return profile.Normalize() with
        {
            LastGameId = category.GameId,
            LastRuleSetIds = ruleSets,
            LastDifficultyIds = difficulties,
            LastShipIds = ships
        };
    }

    public PlayerProfile RecordCompletedRun(PlayerProfile profile, CompletedRunRecord run)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(run.RunId);
        var normalized = profile.Normalize();
        if (normalized.RecordedRunIds.Contains(run.RunId)) return normalized;
        var category = run.Category.Normalize();
        var categories = new Dictionary<string, PlayerCategoryStats>(normalized.CategoryStats, StringComparer.Ordinal);
        var current = categories.GetValueOrDefault(category.StableId) ?? new PlayerCategoryStats { Category = category };
        categories[category.StableId] = current with
        {
            BestScore = Math.Max(current.BestScore, Math.Max(0, run.Score)),
            ClearCount = run.Cleared ? Increment(current.ClearCount) : current.ClearCount,
            BestStage = Math.Max(current.BestStage, Math.Max(0, run.BestStage)),
            PlayCount = Increment(current.PlayCount),
            PlayTimeFrames = AddSaturating(current.PlayTimeFrames, Math.Max(0, run.PlayTimeFrames))
        };
        var runIds = new HashSet<string>(normalized.RecordedRunIds, StringComparer.Ordinal) { run.RunId };
        var highScores = new Dictionary<string, int>(normalized.HighScores, StringComparer.Ordinal);
        var compatibleScore = (int)Math.Min(int.MaxValue, Math.Max(0, run.Score));
        if (!highScores.TryGetValue(category.GameId, out var globalBest) || compatibleScore > globalBest)
            highScores[category.GameId] = compatibleScore;
        var clearCounts = new Dictionary<string, int>(normalized.ClearCounts, StringComparer.Ordinal);
        if (run.Cleared) clearCounts[category.GameId] = Increment(clearCounts.GetValueOrDefault(category.GameId));
        return SelectRunConfiguration(normalized with
        {
            CategoryStats = categories,
            RecordedRunIds = runIds,
            HighScores = highScores,
            ClearCounts = clearCounts
        }, category);
    }

    public PlayerProfile RecordCompletedRun(
        PlayerProfile profile,
        string gameId,
        int score,
        bool cleared)
    {
        return RecordCompletedRun(profile, new CompletedRunRecord
        {
            RunId = $"legacy:{gameId}:{profile.RecordedRunIds.Count}:{score}:{cleared}",
            Category = ScoreCategoryKey.Legacy(gameId),
            Score = score,
            Cleared = cleared
        });
    }

    private static int Increment(int value) => value == int.MaxValue ? int.MaxValue : value + 1;
    private static long AddSaturating(long left, long right) => left > long.MaxValue - right ? long.MaxValue : left + right;
}
