using System.Text.Json;

namespace GoatShooooting.Framework;

public interface IStringCatalog
{
    string Locale { get; }
    IReadOnlyList<string> MissingKeys { get; }
    string Get(string key);
    void SetLocale(string locale);
}

public sealed class LocalizedStringCatalog : IStringCatalog
{
    public static readonly IReadOnlyList<string> RequiredKeys =
    [
        "menu.start",
        "menu.training",
        "menu.leaderboard",
        "menu.options",
        "menu.information",
        "menu.quit",
        "menu.resume",
        "menu.retry",
        "menu.title",
        "menu.back",
        "menu.playReplay",
        "info.controls.title",
        "info.controls.body",
        "info.scoring.title",
        "info.scoring.body",
        "info.credits.title",
        "info.credits.body",
        "info.licenses.title",
        "info.licenses.body",
        "hud.high",
        "hud.score",
        "hud.chain",
        "hud.multiplier",
        "hud.power",
        "hud.gauge",
        "hud.rank",
        "hud.stage",
        "hud.lives",
        "hud.bombs",
        "hud.warning",
        "glyph.keyboard",
        "glyph.gamepad",
        "options.windowMode",
        "options.windowScale",
        "options.vsync",
        "options.language",
        "options.masterVolume",
        "options.musicVolume",
        "options.effectsVolume",
        "options.voiceVolume",
        "options.muted",
        "options.accessibilityPreset",
        "options.screenShake",
        "options.flash",
        "options.particles",
        "options.background",
        "options.bulletOutline",
        "options.bulletPalette",
        "options.hudScale",
        "options.vibration",
        "options.moveUp",
        "options.moveDown",
        "options.moveLeft",
        "options.moveRight",
        "options.fire",
        "options.focus",
        "options.special",
        "options.bomb",
        "options.pause",
        "options.confirm",
        "options.cancel",
        "options.retry"
    ];

    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _locales;
    private readonly string _fallbackLocale;
    private string _locale;

    public LocalizedStringCatalog(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> locales,
        string locale,
        string fallbackLocale = "en")
    {
        _locales = locales ?? throw new ArgumentNullException(nameof(locales));
        if (!_locales.ContainsKey(fallbackLocale)) throw new ArgumentException("Fallback locale is missing.", nameof(locales));
        _fallbackLocale = fallbackLocale;
        _locale = locale;
        MissingKeys = GetMissingKeys(locale);
    }

    public string Locale => _locale;
    public IReadOnlyList<string> MissingKeys { get; private set; }

    public string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (_locales.TryGetValue(_locale, out var selected) && selected.TryGetValue(key, out var value)) return value;
        if (_locales[_fallbackLocale].TryGetValue(key, out value)) return value;
        return $"[{key}]";
    }

    public void SetLocale(string locale)
    {
        _locale = locale;
        MissingKeys = GetMissingKeys(locale);
    }

    public static LocalizedStringCatalog CreateBuiltIn(string locale = "en") => new(
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["en"] = BuiltInEnglish
        },
        locale);

    private static readonly IReadOnlyDictionary<string, string> BuiltInEnglish =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["menu.start"] = "START",
            ["menu.training"] = "TRAINING",
            ["menu.leaderboard"] = "LEADERBOARD",
            ["menu.options"] = "OPTIONS",
            ["menu.information"] = "INFORMATION",
            ["menu.quit"] = "QUIT",
            ["menu.resume"] = "RESUME",
            ["menu.retry"] = "RETRY",
            ["menu.title"] = "TITLE",
            ["menu.back"] = "BACK",
            ["menu.playReplay"] = "PLAY REPLAY",
            ["info.controls.title"] = "CONTROLS",
            ["info.controls.body"] = "MOVE ARROWS OR WASD  FIRE Z  FOCUS CTRL  BOMB X  SPECIAL C",
            ["info.scoring.title"] = "SCORING HELP",
            ["info.scoring.body"] = "CHAIN KILLS  GRAZE BULLETS  COLLECT ITEMS  KEEP MULTIPLIER",
            ["info.credits.title"] = "CREDITS",
            ["info.credits.body"] = "DESIGN CODE AND ORIGINAL ART  GOAT SHOOOOTING TEAM",
            ["info.licenses.title"] = "LICENSES",
            ["info.licenses.body"] = "SEE THIRD-PARTY-NOTICES.md  ORIGINAL ASSETS IN ASSET LEDGER",
            ["hud.high"] = "HIGH",
            ["hud.score"] = "SCORE",
            ["hud.chain"] = "CHAIN",
            ["hud.multiplier"] = "MULTI",
            ["hud.power"] = "POWER",
            ["hud.gauge"] = "GAUGE",
            ["hud.rank"] = "RANK",
            ["hud.stage"] = "STAGE",
            ["hud.lives"] = "LIVES",
            ["hud.bombs"] = "BOMBS",
            ["hud.warning"] = "WARNING",
            ["glyph.keyboard"] = "KEY",
            ["glyph.gamepad"] = "PAD"
            ,
            ["options.windowMode"] = "WINDOW MODE",
            ["options.windowScale"] = "WINDOW SCALE",
            ["options.vsync"] = "VSYNC",
            ["options.language"] = "LANGUAGE",
            ["options.masterVolume"] = "MASTER VOLUME",
            ["options.musicVolume"] = "BGM VOLUME",
            ["options.effectsVolume"] = "SE VOLUME",
            ["options.voiceVolume"] = "VOICE VOLUME",
            ["options.muted"] = "MUTED",
            ["options.accessibilityPreset"] = "ACCESSIBILITY PRESET",
            ["options.screenShake"] = "SCREEN SHAKE",
            ["options.flash"] = "SCREEN FLASH",
            ["options.particles"] = "PARTICLES",
            ["options.background"] = "BACKGROUND",
            ["options.bulletOutline"] = "BULLET OUTLINE",
            ["options.bulletPalette"] = "BULLET PALETTE",
            ["options.hudScale"] = "HUD SCALE",
            ["options.vibration"] = "VIBRATION",
            ["options.moveUp"] = "MOVE UP",
            ["options.moveDown"] = "MOVE DOWN",
            ["options.moveLeft"] = "MOVE LEFT",
            ["options.moveRight"] = "MOVE RIGHT",
            ["options.fire"] = "FIRE",
            ["options.focus"] = "FOCUS",
            ["options.special"] = "SPECIAL",
            ["options.bomb"] = "BOMB",
            ["options.pause"] = "PAUSE KEY",
            ["options.confirm"] = "CONFIRM",
            ["options.cancel"] = "CANCEL",
            ["options.retry"] = "RETRY"
        };

    public static IReadOnlyDictionary<string, string> EnglishDefaults => BuiltInEnglish;

    private IReadOnlyList<string> GetMissingKeys(string locale) =>
        !_locales.TryGetValue(locale, out var selected)
            ? RequiredKeys.ToArray()
            : RequiredKeys.Where(key => !selected.ContainsKey(key)).ToArray();
}

public static class JsonStringCatalogLoader
{
    public static LocalizedStringCatalog Load(string gameDirectory, string locale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        var root = Path.Combine(Path.GetFullPath(gameDirectory), "strings");
        var locales = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        if (Directory.Exists(root))
        {
            foreach (var path in Directory.GetFiles(root, "*.json", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                Dictionary<string, string> values;
                try
                {
                    values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ??
                        throw new InvalidDataException($"String catalog '{path}' contains null.");
                }
                catch (JsonException exception)
                {
                    throw new InvalidDataException($"String catalog '{path}' is invalid: {exception.Message}", exception);
                }
                if (values.Any(static pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null))
                    throw new InvalidDataException($"String catalog '{path}' contains an invalid entry.");
                locales.Add(name, new Dictionary<string, string>(values, StringComparer.Ordinal));
            }
        }
        if (!locales.TryGetValue("en", out var english))
            locales["en"] = LocalizedStringCatalog.EnglishDefaults;
        else
        {
            var missing = LocalizedStringCatalog.RequiredKeys.Where(key => !english.ContainsKey(key)).ToArray();
            if (missing.Length > 0)
                throw new InvalidDataException($"English string catalog is missing: {string.Join(", ", missing)}.");
        }
        return new LocalizedStringCatalog(locales, locale);
    }
}
