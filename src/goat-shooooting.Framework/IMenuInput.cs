namespace GoatShooooting.Framework;

public interface IMenuInput
{
    bool UpPressed { get; }
    bool DownPressed { get; }
    bool LeftPressed { get; }
    bool RightPressed { get; }
    bool ConfirmPressed { get; }
    bool CancelPressed { get; }
}

public enum ActiveInputDevice
{
    Keyboard,
    GamePad
}
