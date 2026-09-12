namespace GoatShooooting.Platform;

public enum LoadStatus
{
    NotFound,
    Loaded,
    Migrated,
    RecoveredFromInvalidData,
    UnsupportedSchema,
    ReadFailed
}

public sealed record LoadResult<T>(T Value, LoadStatus Status, string? InvalidDataPath = null)
{
    public bool UsedDefault => Status is LoadStatus.NotFound
        or LoadStatus.RecoveredFromInvalidData
        or LoadStatus.UnsupportedSchema
        or LoadStatus.ReadFailed;
}

public interface IUserDataStore
{
    LoadResult<GameSettings> LoadSettings();
    LoadResult<PlayerProfile> LoadProfile();
    void SaveSettings(GameSettings settings);
    void SaveProfile(PlayerProfile profile);
}
