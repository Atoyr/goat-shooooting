using Microsoft.Xna.Framework.Input;
using GoatShooooting.Runtime;

namespace GoatShooooting.Framework;

/// <summary>MonoGame keyboard adapter. Runtime systems only see <see cref="IInputState"/>.</summary>
public sealed class KeyboardInputState : IInputState
{
    private readonly GameInputState _input = new();

    public float MoveX => _input.MoveX;
    public float MoveY => _input.MoveY;
    public bool Fire => _input.Fire;
    public bool Focus => _input.Focus;
    public bool Special => _input.Special;
    public bool Bomb => _input.Bomb;
    public bool Retry => _input.Retry;
    public bool Pause => _input.Pause;
    public bool QuitRequested => _input.QuitRequested;
    public bool MenuUpPressed => _input.UpPressed;
    public bool MenuDownPressed => _input.DownPressed;
    public bool MenuConfirmPressed => _input.ConfirmPressed;

    public void Update() => Apply(Keyboard.GetState().GetPressedKeys());

    public void Apply(IEnumerable<Keys> pressedKeys)
    {
        _input.Apply(pressedKeys, default, isGamePadConnected: false);
    }
}
