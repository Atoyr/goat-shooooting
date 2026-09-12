using GoatShooooting.Platform;

namespace GoatShooooting.Framework;

public enum GameShellState
{
    Title,
    Playing,
    Pause,
    Options,
    Result
}

public enum GameShellCommand
{
    None,
    StartRun,
    ResumeRun,
    RetryRun,
    ReturnToTitle,
    SettingsChanged,
    SaveSettings,
    Quit
}

/// <summary>Window-independent title, pause, result, and options menu state machine.</summary>
public sealed class GameShell
{
    private static readonly string[] TitleItems = ["START", "OPTIONS", "QUIT"];
    private static readonly string[] PauseItems = ["RESUME", "OPTIONS", "RETRY", "TITLE"];
    private static readonly string[] ResultItems = ["RETRY", "TITLE"];
    private GameShellState _optionsReturnState;
    private int _selectionIndex;

    public GameShell(GameSettings settings)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public GameShellState State { get; private set; } = GameShellState.Title;
    public GameSettings Settings { get; private set; }
    public int SelectionIndex => _selectionIndex;
    public IReadOnlyList<string> MenuItems => State switch
    {
        GameShellState.Title => TitleItems,
        GameShellState.Pause => PauseItems,
        GameShellState.Result => ResultItems,
        GameShellState.Options => OptionsMenu.ItemLabels,
        _ => []
    };

    public string SelectedValue => State == GameShellState.Options
        ? OptionsMenu.GetValue(Settings, _selectionIndex)
        : string.Empty;

    public GameShellCommand Update(IMenuInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (State == GameShellState.Playing)
        {
            return GameShellCommand.None;
        }

        if (input.CancelPressed)
        {
            return Cancel();
        }

        var items = MenuItems;
        if (input.UpPressed)
        {
            _selectionIndex = (_selectionIndex + items.Count - 1) % items.Count;
        }
        else if (input.DownPressed)
        {
            _selectionIndex = (_selectionIndex + 1) % items.Count;
        }

        if (State == GameShellState.Options)
        {
            var direction = input.LeftPressed ? -1 : input.RightPressed || input.ConfirmPressed ? 1 : 0;
            if (direction != 0)
            {
                if (_selectionIndex == OptionsMenu.BackIndex)
                {
                    if (input.ConfirmPressed)
                    {
                        return CloseOptions();
                    }
                }
                else
                {
                    Settings = OptionsMenu.Adjust(Settings, _selectionIndex, direction);
                    return GameShellCommand.SettingsChanged;
                }
            }

            return GameShellCommand.None;
        }

        return input.ConfirmPressed ? ConfirmSelection() : GameShellCommand.None;
    }

    public void Pause()
    {
        if (State == GameShellState.Playing)
        {
            State = GameShellState.Pause;
            _selectionIndex = 0;
        }
    }

    public void Resume()
    {
        if (State == GameShellState.Pause)
        {
            State = GameShellState.Playing;
            _selectionIndex = 0;
        }
    }

    public void ShowResult()
    {
        if (State == GameShellState.Playing)
        {
            State = GameShellState.Result;
            _selectionIndex = 0;
        }
    }

    public void ReplaceSettings(GameSettings settings) =>
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public GameShellCommand RetryResult()
    {
        if (State != GameShellState.Result)
        {
            return GameShellCommand.None;
        }

        State = GameShellState.Playing;
        _selectionIndex = 0;
        return GameShellCommand.RetryRun;
    }

    private GameShellCommand Cancel() => State switch
    {
        GameShellState.Title => GameShellCommand.Quit,
        GameShellState.Pause => ResumeFromMenu(),
        GameShellState.Result => ReturnToTitle(),
        GameShellState.Options => CloseOptions(),
        _ => GameShellCommand.None
    };

    private GameShellCommand ConfirmSelection() => State switch
    {
        GameShellState.Title => ConfirmTitle(),
        GameShellState.Pause => ConfirmPause(),
        GameShellState.Result => ConfirmResult(),
        _ => GameShellCommand.None
    };

    private GameShellCommand ConfirmTitle()
    {
        if (_selectionIndex == 1)
        {
            OpenOptions(GameShellState.Title);
            return GameShellCommand.None;
        }

        if (_selectionIndex == 2)
        {
            return GameShellCommand.Quit;
        }

        State = GameShellState.Playing;
        _selectionIndex = 0;
        return GameShellCommand.StartRun;
    }

    private GameShellCommand ConfirmPause()
    {
        switch (_selectionIndex)
        {
            case 0:
                return ResumeFromMenu();
            case 1:
                OpenOptions(GameShellState.Pause);
                return GameShellCommand.None;
            case 2:
                State = GameShellState.Playing;
                _selectionIndex = 0;
                return GameShellCommand.RetryRun;
            default:
                return ReturnToTitle();
        }
    }

    private GameShellCommand ConfirmResult()
    {
        if (_selectionIndex == 0)
        {
            State = GameShellState.Playing;
            _selectionIndex = 0;
            return GameShellCommand.RetryRun;
        }

        return ReturnToTitle();
    }

    private GameShellCommand ResumeFromMenu()
    {
        State = GameShellState.Playing;
        _selectionIndex = 0;
        return GameShellCommand.ResumeRun;
    }

    private GameShellCommand ReturnToTitle()
    {
        State = GameShellState.Title;
        _selectionIndex = 0;
        return GameShellCommand.ReturnToTitle;
    }

    private void OpenOptions(GameShellState returnState)
    {
        _optionsReturnState = returnState;
        State = GameShellState.Options;
        _selectionIndex = 0;
    }

    private GameShellCommand CloseOptions()
    {
        State = _optionsReturnState;
        _selectionIndex = 0;
        return GameShellCommand.SaveSettings;
    }
}
