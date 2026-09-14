using System.Globalization;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoatShooooting.Platform;

public sealed class JsonUserDataStore : IUserDataStore
{
    private const string SettingsFileName = "settings.json";
    private const string ProfileFileName = "profile.json";

    private readonly string _rootDirectory;
    private readonly IUserDataLogger _logger;
    private readonly IAtomicFileWriter _fileWriter;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly JsonSerializerOptions _jsonOptions;

    public JsonUserDataStore(string rootDirectory, IUserDataLogger? logger = null)
        : this(rootDirectory, logger, new AtomicFileWriter(), static () => DateTimeOffset.UtcNow)
    {
    }

    internal JsonUserDataStore(
        string rootDirectory,
        IUserDataLogger? logger,
        IAtomicFileWriter fileWriter,
        Func<DateTimeOffset> utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _logger = logger ?? new FileUserDataLogger(_rootDirectory);
        _fileWriter = fileWriter ?? throw new ArgumentNullException(nameof(fileWriter));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    }

    public LoadResult<GameSettings> LoadSettings() => Load(
        SettingsFileName,
        static () => new GameSettings(),
        GameSettings.CurrentSchemaVersion,
        DeserializeSettings,
        static settings => settings.Normalize());

    public LoadResult<PlayerProfile> LoadProfile() => Load(
        ProfileFileName,
        static () => new PlayerProfile(),
        PlayerProfile.CurrentSchemaVersion,
        DeserializeProfile,
        static profile => profile.Normalize());

    public void SaveSettings(GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Save(SettingsFileName, settings.Normalize());
    }

    public void SaveProfile(PlayerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Save(ProfileFileName, profile.Normalize());
    }

    private LoadResult<T> Load<T>(
        string fileName,
        Func<T> createDefault,
        int currentSchemaVersion,
        Func<JsonElement, int, JsonSerializerOptions, T> deserialize,
        Func<T, T> normalize)
    {
        var path = Path.Combine(_rootDirectory, fileName);
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return new LoadResult<T>(createDefault(), LoadStatus.NotFound);
        }

        try
        {
            var json = File.ReadAllText(path);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("The user data root must be a JSON object.");
            }

            var schemaVersion = ReadSchemaVersion(document.RootElement);
            if (schemaVersion < 0 || schemaVersion > currentSchemaVersion)
            {
                SafeLog(
                    UserDataLogLevel.Warning,
                    $"Ignored '{fileName}' because schema version {schemaVersion} is not supported (current: {currentSchemaVersion}).");
                return new LoadResult<T>(createDefault(), LoadStatus.UnsupportedSchema);
            }

            var value = normalize(deserialize(document.RootElement, schemaVersion, _jsonOptions));
            if (schemaVersion == currentSchemaVersion)
            {
                return new LoadResult<T>(value, LoadStatus.Loaded);
            }

            Save(fileName, value);
            SafeLog(UserDataLogLevel.Information, $"Migrated '{fileName}' from schema version {schemaVersion} to {currentSchemaVersion}.");
            return new LoadResult<T>(value, LoadStatus.Migrated);
        }
        catch (Exception exception) when (IsInvalidData(exception))
        {
            var invalidDataPath = QuarantineInvalidFile(path);
            SafeLog(UserDataLogLevel.Warning, $"Recovered from invalid JSON in '{fileName}'.", exception);
            return new LoadResult<T>(createDefault(), LoadStatus.RecoveredFromInvalidData, invalidDataPath);
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            SafeLog(UserDataLogLevel.Error, $"Could not read '{fileName}'. Defaults will be used.", exception);
            return new LoadResult<T>(createDefault(), LoadStatus.ReadFailed);
        }
    }

    private void Save<T>(string fileName, T value)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, _jsonOptions);
            _fileWriter.Write(Path.Combine(_rootDirectory, fileName), bytes);
        }
        catch (Exception exception) when (IsFileFailure(exception) || exception is JsonException)
        {
            SafeLog(UserDataLogLevel.Error, $"Could not save '{fileName}'. The previous file was left unchanged.", exception);
        }
    }

    private string? QuarantineInvalidFile(string path)
    {
        var timestamp = _utcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmssfffffff'Z'", CultureInfo.InvariantCulture);
        var invalidDataPath = $"{path}.invalid-{timestamp}";
        try
        {
            File.Move(path, invalidDataPath);
            return invalidDataPath;
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            SafeLog(UserDataLogLevel.Error, $"Could not quarantine invalid user data '{Path.GetFileName(path)}'.", exception);
            return null;
        }
    }

    private static int ReadSchemaVersion(JsonElement root)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, nameof(GameSettings.SchemaVersion), StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out var version))
                {
                    throw new InvalidDataException("SchemaVersion must be an integer.");
                }

                return version;
            }
        }

        return 0;
    }

    private static GameSettings DeserializeSettings(
        JsonElement root,
        int schemaVersion,
        JsonSerializerOptions options) =>
        schemaVersion switch
        {
            0 => DeserializeRequired<GameSettings>(root, options) with
            {
                SchemaVersion = GameSettings.CurrentSchemaVersion
            },
            1 => DeserializeRequired<GameSettings>(root, options) with
            {
                SchemaVersion = GameSettings.CurrentSchemaVersion
            },
            2 => DeserializeRequired<GameSettings>(root, options) with
            {
                SchemaVersion = GameSettings.CurrentSchemaVersion
            },
            GameSettings.CurrentSchemaVersion => DeserializeRequired<GameSettings>(root, options),
            _ => throw new InvalidOperationException($"No settings migration exists for schema version {schemaVersion}.")
        };

    private static PlayerProfile DeserializeProfile(
        JsonElement root,
        int schemaVersion,
        JsonSerializerOptions options) =>
        schemaVersion switch
        {
            0 => DeserializeRequired<PlayerProfile>(root, options) with
            {
                SchemaVersion = PlayerProfile.CurrentSchemaVersion
            },
            1 => DeserializeRequired<PlayerProfile>(root, options) with
            {
                SchemaVersion = PlayerProfile.CurrentSchemaVersion
            },
            PlayerProfile.CurrentSchemaVersion => DeserializeRequired<PlayerProfile>(root, options),
            _ => throw new InvalidOperationException($"No profile migration exists for schema version {schemaVersion}.")
        };

    private static T DeserializeRequired<T>(JsonElement root, JsonSerializerOptions options) =>
        root.Deserialize<T>(options) ?? throw new InvalidDataException("The JSON document did not contain user data.");

    private void SafeLog(UserDataLogLevel level, string message, Exception? exception = null)
    {
        try
        {
            _logger.Log(level, message, exception);
        }
        catch (Exception logException) when (logException is not OutOfMemoryException and not StackOverflowException)
        {
            // A custom logger must not turn a recoverable user-data problem into a startup failure.
        }
    }

    private static bool IsInvalidData(Exception exception) => exception is
        JsonException or InvalidDataException or FormatException;

    private static bool IsFileFailure(Exception exception) => exception is
        IOException or UnauthorizedAccessException or SecurityException or NotSupportedException;
}
