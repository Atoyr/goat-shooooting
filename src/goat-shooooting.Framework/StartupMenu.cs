namespace GoatShooooting.Framework;

public enum StartupMenuSelection
{
    StartGame,
    Quit
}

public enum StartupMenuAction
{
    None,
    StartGame,
    Quit
}

/// <summary>Owns startup-menu selection without depending on MonoGame rendering.</summary>
public sealed class StartupMenu
{
    public bool IsOpen { get; private set; } = true;
    public StartupMenuSelection Selection { get; private set; }

    public StartupMenuAction Update(bool upPressed, bool downPressed, bool confirmPressed)
    {
        if (!IsOpen)
        {
            return StartupMenuAction.None;
        }

        if (upPressed || downPressed)
        {
            Selection = Selection == StartupMenuSelection.StartGame
                ? StartupMenuSelection.Quit
                : StartupMenuSelection.StartGame;
        }

        if (!confirmPressed)
        {
            return StartupMenuAction.None;
        }

        if (Selection == StartupMenuSelection.Quit)
        {
            return StartupMenuAction.Quit;
        }

        IsOpen = false;
        return StartupMenuAction.StartGame;
    }
}
