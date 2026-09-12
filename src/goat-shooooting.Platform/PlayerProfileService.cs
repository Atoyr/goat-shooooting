namespace GoatShooooting.Platform;

public sealed class PlayerProfileService
{
    public PlayerProfile SelectGame(PlayerProfile profile, string gameId)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        return profile with { LastGameId = gameId };
    }

    public PlayerProfile RecordCompletedRun(
        PlayerProfile profile,
        string gameId,
        int score,
        bool cleared)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        var updated = profile.WithHighScore(gameId, score) with { LastGameId = gameId };
        if (!cleared)
        {
            return updated;
        }

        var clearCounts = new Dictionary<string, int>(updated.ClearCounts, StringComparer.Ordinal);
        clearCounts.TryGetValue(gameId, out var currentCount);
        clearCounts[gameId] = currentCount == int.MaxValue ? int.MaxValue : currentCount + 1;
        return updated with { ClearCounts = clearCounts };
    }
}
