using System.Text;
using GoatShooooting.Platform;
using Xunit;

namespace GoatShooooting.Platform.Tests;

public sealed class JsonUserDataStoreTests
{
    [Fact]
    public void FirstLoadUsesDefaultsWithoutCreatingFiles()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonUserDataStore(directory.Path, new RecordingLogger());

        var settings = store.LoadSettings();
        var profile = store.LoadProfile();

        Assert.Equal(LoadStatus.NotFound, settings.Status);
        Assert.True(settings.UsedDefault);
        Assert.Equal(WindowMode.Windowed, settings.Value.Display.WindowMode);
        Assert.Equal(1, settings.Value.Display.WindowScale);
        Assert.Equal(1.0f, settings.Value.Audio.MasterVolume);
        Assert.Equal("Z", settings.Value.Input.Fire);
        Assert.Equal(LoadStatus.NotFound, profile.Status);
        Assert.Equal("sample", profile.Value.LastGameId);
        Assert.Empty(profile.Value.HighScores);
        Assert.False(File.Exists(System.IO.Path.Combine(directory.Path, "settings.json")));
        Assert.False(File.Exists(System.IO.Path.Combine(directory.Path, "profile.json")));
    }

    [Fact]
    public void SettingsAndProfileRoundTrip()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonUserDataStore(directory.Path, new RecordingLogger());
        var settings = new GameSettings
        {
            Display = new DisplaySettings
            {
                WindowMode = WindowMode.BorderlessFullscreen,
                WindowScale = 3,
                VSync = false
            },
            Audio = new AudioSettings { MasterVolume = 0.75f, EffectsVolume = 0.25f, Muted = true },
            Gameplay = new GameplaySettings { ScreenShakeStrength = 0.4f, ControllerVibration = false },
            Input = new InputSettings { Fire = "Space", Bomb = "LeftShift" }
        };
        var profile = new PlayerProfile
        {
            LastGameId = "gauntlet",
            HighScores = new Dictionary<string, int> { ["sample"] = 1200, ["gauntlet"] = 3400 },
            ClearCounts = new Dictionary<string, int> { ["sample"] = 2 },
            CategoryStats = new Dictionary<string, PlayerCategoryStats>
            {
                [new ScoreCategoryKey("sample", "normal", "arcade", "swift").StableId] = new()
                {
                    Category = new ScoreCategoryKey("sample", "normal", "arcade", "swift"),
                    BestScore = 9876543210,
                    ClearCount = 3,
                    BestStage = 5,
                    PlayCount = 8,
                    PlayTimeFrames = 12345
                }
            },
            LastRuleSetIds = new() { ["sample"] = "normal" },
            LastDifficultyIds = new() { ["sample"] = "arcade" },
            LastShipIds = new() { ["sample"] = "swift" },
            Unlocks = new(StringComparer.Ordinal) { "mode:sprint" },
            RecordedRunIds = new(StringComparer.Ordinal) { "run-1" }
        };

        store.SaveSettings(settings);
        store.SaveProfile(profile);
        var loadedSettings = store.LoadSettings();
        var loadedProfile = store.LoadProfile();

        Assert.Equal(LoadStatus.Loaded, loadedSettings.Status);
        Assert.Equal(WindowMode.BorderlessFullscreen, loadedSettings.Value.Display.WindowMode);
        Assert.Equal(3, loadedSettings.Value.Display.WindowScale);
        Assert.False(loadedSettings.Value.Display.VSync);
        Assert.Equal(0.75f, loadedSettings.Value.Audio.MasterVolume);
        Assert.Equal(0.25f, loadedSettings.Value.Audio.EffectsVolume);
        Assert.True(loadedSettings.Value.Audio.Muted);
        Assert.Equal(0.4f, loadedSettings.Value.Gameplay.ScreenShakeStrength);
        Assert.False(loadedSettings.Value.Gameplay.ControllerVibration);
        Assert.Equal("Space", loadedSettings.Value.Input.Fire);
        Assert.Equal("LeftControl", loadedSettings.Value.Input.Focus);
        Assert.Equal("C", loadedSettings.Value.Input.Special);
        Assert.Equal("LeftShift", loadedSettings.Value.Input.Bomb);
        Assert.Equal(LoadStatus.Loaded, loadedProfile.Status);
        Assert.Equal("gauntlet", loadedProfile.Value.LastGameId);
        Assert.Equal(3400, loadedProfile.Value.HighScores["gauntlet"]);
        Assert.Equal(2, loadedProfile.Value.ClearCounts["sample"]);
        var category = new ScoreCategoryKey("sample", "normal", "arcade", "swift");
        Assert.Equal(9876543210, loadedProfile.Value.GetStats(category)!.BestScore);
        Assert.Equal("normal", loadedProfile.Value.LastRuleSetIds["sample"]);
        Assert.Contains("mode:sprint", loadedProfile.Value.Unlocks);
        Assert.Contains("run-1", loadedProfile.Value.RecordedRunIds);
    }

    [Fact]
    public void SettingsAreNormalizedBeforeSaveAndAfterLoad()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonUserDataStore(directory.Path, new RecordingLogger());

        store.SaveSettings(new GameSettings
        {
            Display = new DisplaySettings { WindowScale = 99 },
            Audio = new AudioSettings { MasterVolume = -2.0f, EffectsVolume = 4.0f },
            Gameplay = new GameplaySettings { ScreenShakeStrength = -0.5f }
        });
        var loaded = store.LoadSettings().Value;

        Assert.Equal(DisplaySettings.MaximumWindowScale, loaded.Display.WindowScale);
        Assert.Equal(0.0f, loaded.Audio.MasterVolume);
        Assert.Equal(1.0f, loaded.Audio.EffectsVolume);
        Assert.Equal(0.0f, loaded.Gameplay.ScreenShakeStrength);

        File.WriteAllText(
            System.IO.Path.Combine(directory.Path, "settings.json"),
            """
            {
              "schemaVersion": 2,
              "display": { "windowScale": -20 },
              "audio": { "masterVolume": 7, "effectsVolume": -1 },
              "gameplay": { "screenShakeStrength": 8 }
            }
            """);
        loaded = store.LoadSettings().Value;

        Assert.Equal(DisplaySettings.MinimumWindowScale, loaded.Display.WindowScale);
        Assert.Equal(1.0f, loaded.Audio.MasterVolume);
        Assert.Equal(0.0f, loaded.Audio.EffectsVolume);
        Assert.Equal(1.0f, loaded.Gameplay.ScreenShakeStrength);
    }

    [Fact]
    public void UnknownPropertiesAreIgnoredForForwardCompatibility()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(
            System.IO.Path.Combine(directory.Path, "settings.json"),
            """
            {
              "schemaVersion": 2,
              "futureRootSetting": true,
              "display": { "windowScale": 2, "futureDisplaySetting": "kept-by-a-future-version" },
              "audio": { "masterVolume": 0.5 }
            }
            """);

        var result = new JsonUserDataStore(directory.Path, new RecordingLogger()).LoadSettings();

        Assert.Equal(LoadStatus.Loaded, result.Status);
        Assert.Equal(2, result.Value.Display.WindowScale);
        Assert.Equal(0.5f, result.Value.Audio.MasterVolume);
    }

    [Fact]
    public void InvalidJsonIsQuarantinedAndDefaultsAreReturned()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = System.IO.Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(settingsPath, "{ this is not valid JSON");
        var logger = new RecordingLogger();
        var store = new JsonUserDataStore(
            directory.Path,
            logger,
            new AtomicFileWriter(),
            static () => new DateTimeOffset(2026, 9, 12, 3, 4, 5, TimeSpan.Zero));

        var result = store.LoadSettings();

        Assert.Equal(LoadStatus.RecoveredFromInvalidData, result.Status);
        Assert.True(result.UsedDefault);
        Assert.False(File.Exists(settingsPath));
        Assert.Equal(
            System.IO.Path.Combine(directory.Path, "settings.json.invalid-20260912T0304050000000Z"),
            result.InvalidDataPath);
        Assert.True(File.Exists(result.InvalidDataPath));
        Assert.Contains(logger.Entries, entry => entry.Level == UserDataLogLevel.Warning);
    }

    [Fact]
    public void UnsupportedSchemaIsPreservedAndDefaultsAreReturned()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = System.IO.Path.Combine(directory.Path, "settings.json");
        const string json = "{ \"schemaVersion\": 99, \"futureSetting\": true }";
        File.WriteAllText(settingsPath, json);

        var result = new JsonUserDataStore(directory.Path, new RecordingLogger()).LoadSettings();

        Assert.Equal(LoadStatus.UnsupportedSchema, result.Status);
        Assert.True(result.UsedDefault);
        Assert.Equal(json, File.ReadAllText(settingsPath));
        Assert.Empty(Directory.GetFiles(directory.Path, "settings.json.invalid-*"));
    }

    [Fact]
    public void LegacyDocumentWithoutSchemaVersionIsMigratedToCurrentVersion()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = System.IO.Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(settingsPath, "{ \"display\": { \"windowScale\": 2 } }");
        var store = new JsonUserDataStore(directory.Path, new RecordingLogger());

        var result = store.LoadSettings();

        Assert.Equal(LoadStatus.Migrated, result.Status);
        Assert.Equal(GameSettings.CurrentSchemaVersion, result.Value.SchemaVersion);
        Assert.Equal(2, result.Value.Display.WindowScale);
        Assert.Equal("LeftControl", result.Value.Input.Focus);
        Assert.Equal("C", result.Value.Input.Special);
        Assert.Contains("\"schemaVersion\": 2", File.ReadAllText(settingsPath), StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyProfileWithoutSchemaVersionIsMigratedToCurrentVersion()
    {
        using var directory = new TemporaryDirectory();
        var profilePath = System.IO.Path.Combine(directory.Path, "profile.json");
        File.WriteAllText(profilePath, "{ \"lastGameId\": \"gauntlet\", \"highScores\": { \"gauntlet\": 4200 } }");
        var store = new JsonUserDataStore(directory.Path, new RecordingLogger());

        var result = store.LoadProfile();

        Assert.Equal(LoadStatus.Migrated, result.Status);
        Assert.Equal(PlayerProfile.CurrentSchemaVersion, result.Value.SchemaVersion);
        Assert.Equal("gauntlet", result.Value.LastGameId);
        Assert.Equal(4200, result.Value.HighScores["gauntlet"]);
        Assert.Equal(4200, result.Value.GetStats(ScoreCategoryKey.Legacy("gauntlet"))!.BestScore);
        Assert.Contains("\"schemaVersion\": 2", File.ReadAllText(profilePath), StringComparison.Ordinal);
    }

    [Fact]
    public void SchemaOneProfileMigratesGameScoreAndClearCountWithoutLoss()
    {
        using var directory = new TemporaryDirectory();
        var profilePath = System.IO.Path.Combine(directory.Path, "profile.json");
        File.WriteAllText(profilePath,
            "{ \"schemaVersion\": 1, \"lastGameId\": \"sample\", \"highScores\": { \"sample\": 2147483647 }, \"clearCounts\": { \"sample\": 7 } }");

        var result = new JsonUserDataStore(directory.Path, new RecordingLogger()).LoadProfile();

        Assert.Equal(LoadStatus.Migrated, result.Status);
        var stats = result.Value.GetStats(ScoreCategoryKey.Legacy("sample"))!;
        Assert.Equal(int.MaxValue, stats.BestScore);
        Assert.Equal(7, stats.ClearCount);
        Assert.Equal(int.MaxValue, result.Value.HighScores["sample"]);
    }

    [Fact]
    public void SaveFailureLeavesPreviousValidFileUntouched()
    {
        using var directory = new TemporaryDirectory();
        var logger = new RecordingLogger();
        var workingStore = new JsonUserDataStore(directory.Path, logger);
        workingStore.SaveSettings(new GameSettings
        {
            Audio = new AudioSettings { MasterVolume = 0.25f }
        });
        var originalJson = File.ReadAllText(System.IO.Path.Combine(directory.Path, "settings.json"));
        var failingStore = new JsonUserDataStore(
            directory.Path,
            logger,
            new InterruptedAtomicFileWriter(),
            static () => DateTimeOffset.UtcNow);

        var exception = Record.Exception(() => failingStore.SaveSettings(new GameSettings
        {
            Audio = new AudioSettings { MasterVolume = 0.9f }
        }));

        Assert.Null(exception);
        Assert.Equal(originalJson, File.ReadAllText(System.IO.Path.Combine(directory.Path, "settings.json")));
        Assert.Equal(0.25f, workingStore.LoadSettings().Value.Audio.MasterVolume);
        Assert.Contains(logger.Entries, entry => entry.Level == UserDataLogLevel.Error);
    }

    [Fact]
    public void ReadAndWriteFailuresAreLoggedWithoutEscaping()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(System.IO.Path.Combine(directory.Path, "settings.json"));
        var logger = new RecordingLogger();
        var store = new JsonUserDataStore(directory.Path, logger);

        var loadException = Record.Exception(() => store.LoadSettings());
        var result = store.LoadSettings();
        var saveException = Record.Exception(() => store.SaveSettings(new GameSettings()));

        Assert.Null(loadException);
        Assert.Equal(LoadStatus.ReadFailed, result.Status);
        Assert.True(result.UsedDefault);
        Assert.Null(saveException);
        Assert.True(logger.Entries.Count(entry => entry.Level == UserDataLogLevel.Error) >= 3);
    }

    private sealed class InterruptedAtomicFileWriter : IAtomicFileWriter
    {
        public void Write(string targetPath, ReadOnlySpan<byte> content)
        {
            var temporaryPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(targetPath)!,
                $"{System.IO.Path.GetFileName(targetPath)}.tmp-interrupted");
            try
            {
                using var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None);
                stream.Write(content);
                stream.Flush(flushToDisk: true);
                throw new IOException("Simulated interruption before replacement.");
            }
            finally
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

internal sealed class RecordingLogger : IUserDataLogger
{
    public List<Entry> Entries { get; } = new();

    public void Log(UserDataLogLevel level, string message, Exception? exception = null) =>
        Entries.Add(new Entry(level, message, exception));

    public sealed record Entry(UserDataLogLevel Level, string Message, Exception? Exception);
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"goat-shooooting-platform-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
