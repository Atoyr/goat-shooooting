using Microsoft.Xna.Framework.Input;
using GoatShooooting.Runtime;

namespace GoatShooooting.Framework;

/// <summary>MonoGame keyboard adapter. Runtime systems only see <see cref="IInputState"/>.</summary>
public sealed class KeyboardInputState : IInputState
{
    private HashSet<Keys> _previousKeys = [];

    public float MoveX { get; private set; }
    public float MoveY { get; private set; }
    public bool Fire { get; private set; }
    public bool Bomb { get; private set; }
    public bool Retry { get; private set; }
    public bool Pause { get; private set; }
    public bool QuitRequested { get; private set; }
    public bool MenuUpPressed { get; private set; }
    public bool MenuDownPressed { get; private set; }
    public bool MenuConfirmPressed { get; private set; }

    public void Update() => Apply(Keyboard.GetState().GetPressedKeys());

    public void Apply(IEnumerable<Keys> pressedKeys)
    {
        ArgumentNullException.ThrowIfNull(pressedKeys);
        var keys = pressedKeys.ToHashSet();
        MoveX = (keys.Contains(Keys.Right) || keys.Contains(Keys.D) ? 1 : 0)
            - (keys.Contains(Keys.Left) || keys.Contains(Keys.A) ? 1 : 0);
        MoveY = (keys.Contains(Keys.Down) || keys.Contains(Keys.S) ? 1 : 0)
            - (keys.Contains(Keys.Up) || keys.Contains(Keys.W) ? 1 : 0);
        Fire = keys.Contains(Keys.Space) || keys.Contains(Keys.Z);
        Bomb = keys.Contains(Keys.X) || keys.Contains(Keys.LeftShift) || keys.Contains(Keys.RightShift);
        Retry = keys.Contains(Keys.R) || keys.Contains(Keys.Enter);
        Pause = keys.Contains(Keys.P);
        QuitRequested = keys.Contains(Keys.Escape);
        MenuUpPressed = IsNewPress(keys, Keys.Up) || IsNewPress(keys, Keys.W);
        MenuDownPressed = IsNewPress(keys, Keys.Down) || IsNewPress(keys, Keys.S);
        MenuConfirmPressed = IsNewPress(keys, Keys.Enter) ||
            IsNewPress(keys, Keys.Z) ||
            IsNewPress(keys, Keys.Space);
        _previousKeys = keys;
    }

    private bool IsNewPress(IReadOnlySet<Keys> keys, Keys key) =>
        keys.Contains(key) && !_previousKeys.Contains(key);
}
