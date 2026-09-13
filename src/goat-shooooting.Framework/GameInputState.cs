using GoatShooooting.Platform;
using GoatShooooting.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace GoatShooooting.Framework;

/// <summary>Combines keyboard and first-player gamepad input into logical game and menu actions.</summary>
public sealed class GameInputState : IInputState, IMenuInput
{
    public const float DefaultGamePadDeadZone = 0.2f;

    private HashSet<Keys> _previousKeys = [];
    private GamePadState _previousGamePadState;
    private bool _previousGamePadConnected;
    private KeyboardBindings _bindings;

    public GameInputState(InputSettings? settings = null)
    {
        _bindings = KeyboardBindings.CreateDefault();
        if (settings is not null)
        {
            TryApplySettings(settings, out _);
        }
    }

    public float MoveX { get; private set; }
    public float MoveY { get; private set; }
    public bool Fire { get; private set; }
    public bool Focus { get; private set; }
    public bool Special { get; private set; }
    public bool Bomb { get; private set; }
    public bool Retry { get; private set; }
    public bool RetryPressed { get; private set; }
    public bool Pause { get; private set; }
    public bool PausePressed { get; private set; }
    public bool ToggleFullscreenPressed { get; private set; }
    public bool QuitRequested { get; private set; }
    public bool UpPressed { get; private set; }
    public bool DownPressed { get; private set; }
    public bool LeftPressed { get; private set; }
    public bool RightPressed { get; private set; }
    public bool ConfirmPressed { get; private set; }
    public bool CancelPressed { get; private set; }
    public ActiveInputDevice ActiveDevice { get; private set; } = ActiveInputDevice.Keyboard;
    public bool IsGamePadConnected { get; private set; }
    public bool GamePadDisconnectedThisFrame { get; private set; }
    public bool KeyboardInputDetected { get; private set; }
    public string? NewlyPressedKey { get; private set; }
    public InputSettings CurrentInputSettings => _bindings.Settings;

    public void Update()
    {
        var gamePadState = GamePad.GetState(PlayerIndex.One, GamePadDeadZone.None);
        Apply(Keyboard.GetState().GetPressedKeys(), gamePadState, gamePadState.IsConnected);
    }

    public void Apply(IEnumerable<Keys> pressedKeys, GamePadState gamePadState, bool isGamePadConnected)
    {
        ArgumentNullException.ThrowIfNull(pressedKeys);
        var pressedKeyArray = pressedKeys.Distinct().ToArray();
        var keys = pressedKeyArray.ToHashSet();
        var stick = isGamePadConnected
            ? ApplyCircularDeadZone(gamePadState.ThumbSticks.Left, DefaultGamePadDeadZone)
            : Vector2.Zero;

        KeyboardInputDetected = keys.Count > 0;
        NewlyPressedKey = pressedKeyArray
            .Where(key => !_previousKeys.Contains(key))
            .Select(key => key.ToString())
            .FirstOrDefault();
        IsGamePadConnected = isGamePadConnected;
        GamePadDisconnectedThisFrame = _previousGamePadConnected && !isGamePadConnected;

        var keyboardMoveX = (IsDown(keys, _bindings.MoveRight, Keys.Right) ? 1 : 0)
            - (IsDown(keys, _bindings.MoveLeft, Keys.Left) ? 1 : 0);
        var keyboardMoveY = (IsDown(keys, _bindings.MoveDown, Keys.Down) ? 1 : 0)
            - (IsDown(keys, _bindings.MoveUp, Keys.Up) ? 1 : 0);
        var dPadMoveX = isGamePadConnected
            ? (gamePadState.IsButtonDown(Buttons.DPadRight) ? 1 : 0)
                - (gamePadState.IsButtonDown(Buttons.DPadLeft) ? 1 : 0)
            : 0;
        var dPadMoveY = isGamePadConnected
            ? (gamePadState.IsButtonDown(Buttons.DPadDown) ? 1 : 0)
                - (gamePadState.IsButtonDown(Buttons.DPadUp) ? 1 : 0)
            : 0;

        MoveX = Math.Clamp(keyboardMoveX + dPadMoveX + stick.X, -1.0f, 1.0f);
        MoveY = Math.Clamp(keyboardMoveY + dPadMoveY - stick.Y, -1.0f, 1.0f);
        Fire = IsDown(keys, _bindings.Fire, Keys.Space) || IsGamePadButtonDown(gamePadState, isGamePadConnected, Buttons.A, Buttons.X);
        Focus = IsDown(keys, _bindings.Focus) ||
            IsGamePadButtonDown(gamePadState, isGamePadConnected, Buttons.LeftShoulder);
        Special = IsDown(keys, _bindings.Special) ||
            IsGamePadButtonDown(gamePadState, isGamePadConnected, Buttons.RightShoulder);
        Bomb = IsDown(keys, _bindings.Bomb, Keys.LeftShift, Keys.RightShift) ||
            IsGamePadButtonDown(gamePadState, isGamePadConnected, Buttons.B, Buttons.Y);
        Retry = IsDown(keys, _bindings.Retry, Keys.Enter) ||
            IsGamePadButtonDown(gamePadState, isGamePadConnected, Buttons.A);
        RetryPressed = IsNewPress(keys, _bindings.Retry, Keys.Enter) ||
            IsNewGamePadPress(gamePadState, isGamePadConnected, Buttons.A);
        Pause = keys.Contains(_bindings.Pause) ||
            IsGamePadButtonDown(gamePadState, isGamePadConnected, Buttons.Start);
        PausePressed = IsNewPress(keys, _bindings.Pause) ||
            IsNewGamePadPress(gamePadState, isGamePadConnected, Buttons.Start);
        ToggleFullscreenPressed = (keys.Contains(Keys.LeftAlt) || keys.Contains(Keys.RightAlt)) &&
            IsNewPress(keys, Keys.Enter);
        QuitRequested = keys.Contains(Keys.Escape);

        var gamePadUp = IsGamePadDirectionDown(gamePadState, isGamePadConnected, Buttons.DPadUp, stick.Y > 0);
        var gamePadDown = IsGamePadDirectionDown(gamePadState, isGamePadConnected, Buttons.DPadDown, stick.Y < 0);
        var gamePadLeft = IsGamePadDirectionDown(gamePadState, isGamePadConnected, Buttons.DPadLeft, stick.X < 0);
        var gamePadRight = IsGamePadDirectionDown(gamePadState, isGamePadConnected, Buttons.DPadRight, stick.X > 0);
        var previousStick = _previousGamePadConnected
            ? ApplyCircularDeadZone(_previousGamePadState.ThumbSticks.Left, DefaultGamePadDeadZone)
            : Vector2.Zero;

        UpPressed = IsNewPress(keys, _bindings.MoveUp, Keys.Up) ||
            IsNewGamePadDirection(gamePadUp, Buttons.DPadUp, previousStick.Y > 0);
        DownPressed = IsNewPress(keys, _bindings.MoveDown, Keys.Down) ||
            IsNewGamePadDirection(gamePadDown, Buttons.DPadDown, previousStick.Y < 0);
        LeftPressed = IsNewPress(keys, _bindings.MoveLeft, Keys.Left) ||
            IsNewGamePadDirection(gamePadLeft, Buttons.DPadLeft, previousStick.X < 0);
        RightPressed = IsNewPress(keys, _bindings.MoveRight, Keys.Right) ||
            IsNewGamePadDirection(gamePadRight, Buttons.DPadRight, previousStick.X > 0);
        ConfirmPressed = IsNewPress(keys, _bindings.Confirm, Keys.Z, Keys.Space) ||
            IsNewGamePadPress(gamePadState, isGamePadConnected, Buttons.A);
        CancelPressed = IsNewPress(keys, _bindings.Cancel) ||
            IsNewGamePadPress(gamePadState, isGamePadConnected, Buttons.B);

        var gamePadInputDetected = HasGamePadInput(gamePadState, isGamePadConnected, stick);
        if (KeyboardInputDetected)
        {
            ActiveDevice = ActiveInputDevice.Keyboard;
        }

        if (gamePadInputDetected)
        {
            ActiveDevice = ActiveInputDevice.GamePad;
        }

        _previousKeys = keys;
        _previousGamePadState = gamePadState;
        _previousGamePadConnected = isGamePadConnected;
    }

    public bool TryApplySettings(InputSettings settings, out InputSettings normalizedSettings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!KeyboardBindings.TryCreate(settings, out var bindings))
        {
            normalizedSettings = _bindings.Settings;
            return false;
        }

        _bindings = bindings;
        normalizedSettings = bindings.Settings;
        return true;
    }

    internal void RequestPause() => Pause = true;

    private bool IsNewPress(IReadOnlySet<Keys> keys, params Keys[] candidates) =>
        candidates.Any(key => keys.Contains(key) && !_previousKeys.Contains(key));

    private bool IsNewGamePadPress(GamePadState state, bool connected, Buttons button) =>
        connected && state.IsButtonDown(button) &&
        (!_previousGamePadConnected || _previousGamePadState.IsButtonUp(button));

    private bool IsNewGamePadDirection(bool isDown, Buttons dPadButton, bool previousStickDirection) =>
        isDown && (!_previousGamePadConnected ||
            (!_previousGamePadState.IsButtonDown(dPadButton) && !previousStickDirection));

    private static bool IsDown(IReadOnlySet<Keys> keys, params Keys[] candidates) =>
        candidates.Any(keys.Contains);

    private static bool IsGamePadButtonDown(GamePadState state, bool connected, params Buttons[] buttons) =>
        connected && buttons.Any(state.IsButtonDown);

    private static bool IsGamePadDirectionDown(
        GamePadState state,
        bool connected,
        Buttons dPadButton,
        bool stickDirection) =>
        connected && (state.IsButtonDown(dPadButton) || stickDirection);

    private static bool HasGamePadInput(GamePadState state, bool connected, Vector2 stick) =>
        connected && (stick != Vector2.Zero ||
            IsGamePadButtonDown(
                state,
                connected,
                Buttons.A,
                Buttons.B,
                Buttons.X,
                Buttons.Y,
                Buttons.LeftShoulder,
                Buttons.RightShoulder,
                Buttons.Start,
                Buttons.Back,
                Buttons.DPadUp,
                Buttons.DPadDown,
                Buttons.DPadLeft,
                Buttons.DPadRight) ||
            state.Triggers.Left > 0 ||
            state.Triggers.Right > 0);

    private static Vector2 ApplyCircularDeadZone(Vector2 value, float deadZone)
    {
        var length = value.Length();
        if (length <= deadZone)
        {
            return Vector2.Zero;
        }

        var scaledLength = Math.Clamp((length - deadZone) / (1.0f - deadZone), 0.0f, 1.0f);
        return Vector2.Normalize(value) * scaledLength;
    }

    private sealed class KeyboardBindings
    {
        private KeyboardBindings(InputSettings settings)
        {
            Settings = settings;
            MoveUp = Parse(settings.MoveUp);
            MoveDown = Parse(settings.MoveDown);
            MoveLeft = Parse(settings.MoveLeft);
            MoveRight = Parse(settings.MoveRight);
            Fire = Parse(settings.Fire);
            Focus = Parse(settings.Focus);
            Special = Parse(settings.Special);
            Bomb = Parse(settings.Bomb);
            Pause = Parse(settings.Pause);
            Confirm = Parse(settings.Confirm);
            Cancel = Parse(settings.Cancel);
            Retry = Parse(settings.Retry);
        }

        public InputSettings Settings { get; }
        public Keys MoveUp { get; }
        public Keys MoveDown { get; }
        public Keys MoveLeft { get; }
        public Keys MoveRight { get; }
        public Keys Fire { get; }
        public Keys Focus { get; }
        public Keys Special { get; }
        public Keys Bomb { get; }
        public Keys Pause { get; }
        public Keys Confirm { get; }
        public Keys Cancel { get; }
        public Keys Retry { get; }

        public static KeyboardBindings CreateDefault()
        {
            TryCreate(new InputSettings(), out var bindings);
            return bindings;
        }

        public static bool TryCreate(InputSettings requested, out KeyboardBindings bindings)
        {
            var defaults = new InputSettings();
            var normalized = requested with
            {
                MoveUp = NormalizeKey(requested.MoveUp, defaults.MoveUp),
                MoveDown = NormalizeKey(requested.MoveDown, defaults.MoveDown),
                MoveLeft = NormalizeKey(requested.MoveLeft, defaults.MoveLeft),
                MoveRight = NormalizeKey(requested.MoveRight, defaults.MoveRight),
                Fire = NormalizeKey(requested.Fire, defaults.Fire),
                Focus = NormalizeKey(requested.Focus, defaults.Focus),
                Special = NormalizeKey(requested.Special, defaults.Special),
                Bomb = NormalizeKey(requested.Bomb, defaults.Bomb),
                Pause = NormalizeKey(requested.Pause, defaults.Pause),
                Confirm = NormalizeKey(requested.Confirm, defaults.Confirm),
                Cancel = NormalizeKey(requested.Cancel, defaults.Cancel),
                Retry = NormalizeKey(requested.Retry, defaults.Retry)
            };

            var candidate = new KeyboardBindings(normalized);
            if (candidate.Confirm == candidate.Cancel)
            {
                bindings = null!;
                return false;
            }

            bindings = candidate;
            return true;
        }

        private static string NormalizeKey(string? value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                int.TryParse(value, out _) ||
                !Enum.TryParse<Keys>(value, ignoreCase: true, out var key) ||
                !Enum.IsDefined(key))
            {
                return fallback;
            }

            return key.ToString();
        }

        private static Keys Parse(string value) => Enum.Parse<Keys>(value, ignoreCase: false);
    }
}
