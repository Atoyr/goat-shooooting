namespace GoatShooooting.Platform;

public enum WindowMode
{
    Windowed,
    BorderlessFullscreen
}

public sealed record GameSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public DisplaySettings Display { get; init; } = new();
    public AudioSettings Audio { get; init; } = new();
    public GameplaySettings Gameplay { get; init; } = new();
    public InputSettings Input { get; init; } = new();

    internal GameSettings Normalize()
    {
        var defaults = new GameSettings();
        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            Display = (Display ?? defaults.Display).Normalize(),
            Audio = (Audio ?? defaults.Audio).Normalize(),
            Gameplay = (Gameplay ?? defaults.Gameplay).Normalize(),
            Input = (Input ?? defaults.Input).Normalize()
        };
    }
}

public sealed record DisplaySettings
{
    public const int MinimumWindowScale = 1;
    public const int MaximumWindowScale = 4;

    public WindowMode WindowMode { get; init; } = WindowMode.Windowed;
    public int WindowScale { get; init; } = MinimumWindowScale;
    public bool VSync { get; init; } = true;

    internal DisplaySettings Normalize() => this with
    {
        WindowScale = Math.Clamp(WindowScale, MinimumWindowScale, MaximumWindowScale)
    };
}

public sealed record AudioSettings
{
    public float MasterVolume { get; init; } = 1.0f;
    public float EffectsVolume { get; init; } = 1.0f;
    public bool Muted { get; init; }

    internal AudioSettings Normalize() => this with
    {
        MasterVolume = NormalizeUnitValue(MasterVolume),
        EffectsVolume = NormalizeUnitValue(EffectsVolume)
    };

    private static float NormalizeUnitValue(float value) =>
        float.IsFinite(value) ? Math.Clamp(value, 0.0f, 1.0f) : 1.0f;
}

public sealed record GameplaySettings
{
    public float ScreenShakeStrength { get; init; } = 1.0f;
    public bool ControllerVibration { get; init; } = true;

    internal GameplaySettings Normalize() => this with
    {
        ScreenShakeStrength = float.IsFinite(ScreenShakeStrength)
            ? Math.Clamp(ScreenShakeStrength, 0.0f, 1.0f)
            : 1.0f
    };
}

public sealed record InputSettings
{
    public string MoveUp { get; init; } = "W";
    public string MoveDown { get; init; } = "S";
    public string MoveLeft { get; init; } = "A";
    public string MoveRight { get; init; } = "D";
    public string Fire { get; init; } = "Z";
    public string Bomb { get; init; } = "X";
    public string Pause { get; init; } = "P";
    public string Confirm { get; init; } = "Enter";
    public string Cancel { get; init; } = "Escape";
    public string Retry { get; init; } = "R";

    internal InputSettings Normalize()
    {
        var defaults = new InputSettings();
        return this with
        {
            MoveUp = ValueOrDefault(MoveUp, defaults.MoveUp),
            MoveDown = ValueOrDefault(MoveDown, defaults.MoveDown),
            MoveLeft = ValueOrDefault(MoveLeft, defaults.MoveLeft),
            MoveRight = ValueOrDefault(MoveRight, defaults.MoveRight),
            Fire = ValueOrDefault(Fire, defaults.Fire),
            Bomb = ValueOrDefault(Bomb, defaults.Bomb),
            Pause = ValueOrDefault(Pause, defaults.Pause),
            Confirm = ValueOrDefault(Confirm, defaults.Confirm),
            Cancel = ValueOrDefault(Cancel, defaults.Cancel),
            Retry = ValueOrDefault(Retry, defaults.Retry)
        };
    }

    private static string ValueOrDefault(string? value, string defaultValue) =>
        string.IsNullOrWhiteSpace(value) ? defaultValue : value;
}
