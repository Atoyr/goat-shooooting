using GoatShooooting.Platform;

namespace GoatShooooting.Framework;

internal static class OptionsMenu
{
    private static readonly string[] KeyChoices =
    [
        "W",
        "A",
        "S",
        "D",
        "Z",
        "X",
        "P",
        "R",
        "Enter",
        "Escape",
        "Space",
        "LeftShift",
        "RightShift",
        "LeftControl",
        "RightControl",
        "Up",
        "Down",
        "Left",
        "Right",
        "F",
        "G",
        "C",
        "V"
    ];

    private static readonly string[] LabelKeys =
    [
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
        "options.retry",
        "menu.back"
    ];

    public static IReadOnlyList<string> GetLabels(IStringCatalog strings) =>
        LabelKeys.Select(strings.Get).ToArray();

    public static int BackIndex => LabelKeys.Length - 1;

    public static bool IsInputIndex(int index) => index is >= 18 and <= 29;

    public static GameSettings Adjust(GameSettings settings, int index, int direction) => index switch
    {
        0 => settings with
        {
            Display = settings.Display with
            {
                WindowMode = settings.Display.WindowMode == WindowMode.Windowed
                    ? WindowMode.BorderlessFullscreen
                    : WindowMode.Windowed
            }
        },
        1 => settings with
        {
            Display = settings.Display with
            {
                WindowScale = Math.Clamp(
                    settings.Display.WindowScale + direction,
                    DisplaySettings.MinimumWindowScale,
                    DisplaySettings.MaximumWindowScale)
            }
        },
        2 => settings with { Display = settings.Display with { VSync = !settings.Display.VSync } },
        3 => settings with { Locale = settings.Locale == "en" ? "ja" : "en" },
        4 => settings with
        {
            Audio = settings.Audio with
            {
                MasterVolume = AdjustUnit(settings.Audio.MasterVolume, direction)
            }
        },
        5 => settings with
        {
            Audio = settings.Audio with
            {
                MusicVolume = AdjustUnit(settings.Audio.MusicVolume, direction)
            }
        },
        6 => settings with
        {
            Audio = settings.Audio with
            {
                EffectsVolume = AdjustUnit(settings.Audio.EffectsVolume, direction)
            }
        },
        7 => settings with
        {
            Audio = settings.Audio with
            {
                VoiceVolume = AdjustUnit(settings.Audio.VoiceVolume, direction)
            }
        },
        8 => settings with { Audio = settings.Audio with { Muted = !settings.Audio.Muted } },
        9 => settings.ApplyAccessibilityPreset(),
        10 => settings with
        {
            Gameplay = settings.Gameplay with
            {
                ScreenShakeStrength = AdjustUnit(settings.Gameplay.ScreenShakeStrength, direction)
            }
        },
        11 => settings with { Gameplay = settings.Gameplay with { FlashIntensity = AdjustUnit(settings.Gameplay.FlashIntensity, direction) } },
        12 => settings with { Gameplay = settings.Gameplay with { ParticleDensity = AdjustUnit(settings.Gameplay.ParticleDensity, direction) } },
        13 => settings with { Gameplay = settings.Gameplay with { BackgroundBrightness = AdjustUnit(settings.Gameplay.BackgroundBrightness, direction) } },
        14 => settings with { Gameplay = settings.Gameplay with { BulletOutline = !settings.Gameplay.BulletOutline } },
        15 => settings with { Gameplay = settings.Gameplay with { BulletPalette = CyclePalette(settings.Gameplay.BulletPalette, direction) } },
        16 => settings with { Gameplay = settings.Gameplay with { HudScale = AdjustHudScale(settings.Gameplay.HudScale, direction) } },
        17 => settings with
        {
            Gameplay = settings.Gameplay with
            {
                ControllerVibration = !settings.Gameplay.ControllerVibration
            }
        },
        >= 18 and <= 29 => AdjustInput(settings, index, direction),
        _ => settings
    };

    public static string GetValue(GameSettings settings, int index) => index switch
    {
        0 => settings.Display.WindowMode == WindowMode.Windowed ? "WINDOWED" : "BORDERLESS",
        1 => $"{settings.Display.WindowScale}X",
        2 => OnOff(settings.Display.VSync),
        3 => settings.Locale.ToUpperInvariant(),
        4 => Percent(settings.Audio.MasterVolume),
        5 => Percent(settings.Audio.MusicVolume),
        6 => Percent(settings.Audio.EffectsVolume),
        7 => Percent(settings.Audio.VoiceVolume),
        8 => OnOff(settings.Audio.Muted),
        9 => UsesAccessibilityPreset(settings) ? "ACTIVE" : "APPLY",
        10 => Percent(settings.Gameplay.ScreenShakeStrength),
        11 => Percent(settings.Gameplay.FlashIntensity),
        12 => Percent(settings.Gameplay.ParticleDensity),
        13 => Percent(settings.Gameplay.BackgroundBrightness),
        14 => OnOff(settings.Gameplay.BulletOutline),
        15 => settings.Gameplay.BulletPalette.ToUpperInvariant(),
        16 => Percent(settings.Gameplay.HudScale),
        17 => OnOff(settings.Gameplay.ControllerVibration),
        18 => settings.Input.MoveUp,
        19 => settings.Input.MoveDown,
        20 => settings.Input.MoveLeft,
        21 => settings.Input.MoveRight,
        22 => settings.Input.Fire,
        23 => settings.Input.Focus,
        24 => settings.Input.Special,
        25 => settings.Input.Bomb,
        26 => settings.Input.Pause,
        27 => settings.Input.Confirm,
        28 => settings.Input.Cancel,
        29 => settings.Input.Retry,
        _ => string.Empty
    };

    public static bool TryBindInput(
        GameSettings settings,
        int index,
        string key,
        out GameSettings adjustedSettings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!IsInputIndex(index) || string.IsNullOrWhiteSpace(key))
        {
            adjustedSettings = settings;
            return false;
        }

        var input = settings.Input;
        if ((index == 27 && string.Equals(key, input.Cancel, StringComparison.OrdinalIgnoreCase)) ||
            (index == 28 && string.Equals(key, input.Confirm, StringComparison.OrdinalIgnoreCase)))
        {
            adjustedSettings = settings;
            return false;
        }

        var adjusted = index switch
        {
            18 => input with { MoveUp = key },
            19 => input with { MoveDown = key },
            20 => input with { MoveLeft = key },
            21 => input with { MoveRight = key },
            22 => input with { Fire = key },
            23 => input with { Focus = key },
            24 => input with { Special = key },
            25 => input with { Bomb = key },
            26 => input with { Pause = key },
            27 => input with { Confirm = key },
            28 => input with { Cancel = key },
            29 => input with { Retry = key },
            _ => input
        };
        adjustedSettings = settings with { Input = adjusted };
        return true;
    }

    private static GameSettings AdjustInput(GameSettings settings, int index, int direction)
    {
        var input = settings.Input;
        var current = GetValue(settings, index);
        var next = CycleKey(current, direction);
        if ((index == 27 && string.Equals(next, input.Cancel, StringComparison.OrdinalIgnoreCase)) ||
            (index == 28 && string.Equals(next, input.Confirm, StringComparison.OrdinalIgnoreCase)))
        {
            next = CycleKey(next, direction);
        }

        var adjusted = index switch
        {
            18 => input with { MoveUp = next },
            19 => input with { MoveDown = next },
            20 => input with { MoveLeft = next },
            21 => input with { MoveRight = next },
            22 => input with { Fire = next },
            23 => input with { Focus = next },
            24 => input with { Special = next },
            25 => input with { Bomb = next },
            26 => input with { Pause = next },
            27 => input with { Confirm = next },
            28 => input with { Cancel = next },
            29 => input with { Retry = next },
            _ => input
        };
        return settings with { Input = adjusted };
    }

    private static string CycleKey(string current, int direction)
    {
        var index = Array.FindIndex(KeyChoices, key => string.Equals(key, current, StringComparison.OrdinalIgnoreCase));
        index = index < 0 ? 0 : index;
        return KeyChoices[(index + direction + KeyChoices.Length) % KeyChoices.Length];
    }

    private static float AdjustUnit(float value, int direction) =>
        Math.Clamp(MathF.Round((value + (direction * 0.1f)) * 10) / 10, 0, 1);

    private static string Percent(float value) => $"{MathF.Round(value * 100):0}%";

    private static string OnOff(bool value) => value ? "ON" : "OFF";

    private static float AdjustHudScale(float value, int direction) =>
        Math.Clamp(MathF.Round((value + (direction * 0.25f)) * 4) / 4, 0.75f, 1.5f);

    private static string CyclePalette(string current, int direction)
    {
        string[] values = ["standard", "deuteranopia", "high-contrast"];
        var index = Array.IndexOf(values, current);
        return values[(Math.Max(0, index) + direction + values.Length) % values.Length];
    }

    private static bool UsesAccessibilityPreset(GameSettings settings) =>
        settings.Gameplay.ScreenShakeStrength == 0 &&
        settings.Gameplay.FlashIntensity == 0.2f &&
        settings.Gameplay.ParticleDensity == 0.5f &&
        settings.Gameplay.BackgroundBrightness == 0.65f &&
        settings.Gameplay.BulletOutline &&
        settings.Gameplay.BulletPalette == "high-contrast" &&
        settings.Gameplay.HudScale == 1.25f &&
        !settings.Gameplay.ControllerVibration;
}
