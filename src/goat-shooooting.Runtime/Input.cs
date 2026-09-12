namespace GoatShooooting.Runtime;

/// <summary>Frame input consumed by game logic without depending on a windowing API.</summary>
public interface IInputState
{
    float MoveX { get; }
    float MoveY { get; }
    bool Fire { get; }
    bool Bomb { get; }
    bool Retry { get; }
    bool Pause { get; }
}

public sealed class MutableInputState : IInputState
{
    public float MoveX { get; set; }
    public float MoveY { get; set; }
    public bool Fire { get; set; }
    public bool Bomb { get; set; }
    public bool Retry { get; set; }
    public bool Pause { get; set; }
}
