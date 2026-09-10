using Microsoft.Xna.Framework.Input;
using GoatShooooting.Runtime;

namespace GoatShooooting.Framework;

/// <summary>MonoGame keyboard adapter. Runtime systems only see <see cref="IInputState"/>.</summary>
public sealed class KeyboardInputState : IInputState
{
    public float MoveX { get; private set; }
    public float MoveY { get; private set; }
    public bool Fire { get; private set; }
    public bool Retry { get; private set; }
    public bool QuitRequested { get; private set; }

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
        Retry = keys.Contains(Keys.R) || keys.Contains(Keys.Enter);
        QuitRequested = keys.Contains(Keys.Escape);
    }
}
