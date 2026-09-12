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
        "Up",
        "Down",
        "Left",
        "Right",
        "F",
        "G",
        "C",
        "V"
    ];

    public static readonly string[] ItemLabels =
    [
        "WINDOW MODE",
        "WINDOW SCALE",
        "VSYNC",
        "MASTER VOLUME",
        "EFFECTS VOLUME",
        "MUTED",
        "SCREEN SHAKE",
        "VIBRATION",
        "MOVE UP",
        "MOVE DOWN",
        "MOVE LEFT",
        "MOVE RIGHT",
        "FIRE",
        "BOMB",
        "PAUSE KEY",
        "CONFIRM",
        "CANCEL",
        "RETRY",
        "BACK"
    ];

    public static int BackIndex => ItemLabels.Length - 1;

    public static bool IsInputIndex(int index) => index is >= 8 and <= 17;

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
        3 => settings with
        {
            Audio = settings.Audio with
            {
                MasterVolume = AdjustUnit(settings.Audio.MasterVolume, direction)
            }
        },
        4 => settings with
        {
            Audio = settings.Audio with
            {
                EffectsVolume = AdjustUnit(settings.Audio.EffectsVolume, direction)
            }
        },
        5 => settings with { Audio = settings.Audio with { Muted = !settings.Audio.Muted } },
        6 => settings with
        {
            Gameplay = settings.Gameplay with
            {
                ScreenShakeStrength = AdjustUnit(settings.Gameplay.ScreenShakeStrength, direction)
            }
        },
        7 => settings with
        {
            Gameplay = settings.Gameplay with
            {
                ControllerVibration = !settings.Gameplay.ControllerVibration
            }
        },
        >= 8 and <= 17 => AdjustInput(settings, index, direction),
        _ => settings
    };

    public static string GetValue(GameSettings settings, int index) => index switch
    {
        0 => settings.Display.WindowMode == WindowMode.Windowed ? "WINDOWED" : "BORDERLESS",
        1 => $"{settings.Display.WindowScale}X",
        2 => OnOff(settings.Display.VSync),
        3 => Percent(settings.Audio.MasterVolume),
        4 => Percent(settings.Audio.EffectsVolume),
        5 => OnOff(settings.Audio.Muted),
        6 => Percent(settings.Gameplay.ScreenShakeStrength),
        7 => OnOff(settings.Gameplay.ControllerVibration),
        8 => settings.Input.MoveUp,
        9 => settings.Input.MoveDown,
        10 => settings.Input.MoveLeft,
        11 => settings.Input.MoveRight,
        12 => settings.Input.Fire,
        13 => settings.Input.Bomb,
        14 => settings.Input.Pause,
        15 => settings.Input.Confirm,
        16 => settings.Input.Cancel,
        17 => settings.Input.Retry,
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
        if ((index == 15 && string.Equals(key, input.Cancel, StringComparison.OrdinalIgnoreCase)) ||
            (index == 16 && string.Equals(key, input.Confirm, StringComparison.OrdinalIgnoreCase)))
        {
            adjustedSettings = settings;
            return false;
        }

        var adjusted = index switch
        {
            8 => input with { MoveUp = key },
            9 => input with { MoveDown = key },
            10 => input with { MoveLeft = key },
            11 => input with { MoveRight = key },
            12 => input with { Fire = key },
            13 => input with { Bomb = key },
            14 => input with { Pause = key },
            15 => input with { Confirm = key },
            16 => input with { Cancel = key },
            17 => input with { Retry = key },
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
        if ((index == 15 && string.Equals(next, input.Cancel, StringComparison.OrdinalIgnoreCase)) ||
            (index == 16 && string.Equals(next, input.Confirm, StringComparison.OrdinalIgnoreCase)))
        {
            next = CycleKey(next, direction);
        }

        var adjusted = index switch
        {
            8 => input with { MoveUp = next },
            9 => input with { MoveDown = next },
            10 => input with { MoveLeft = next },
            11 => input with { MoveRight = next },
            12 => input with { Fire = next },
            13 => input with { Bomb = next },
            14 => input with { Pause = next },
            15 => input with { Confirm = next },
            16 => input with { Cancel = next },
            17 => input with { Retry = next },
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
}
