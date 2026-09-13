using GoatShooooting.Platform;
using GoatShooooting.Runtime;

namespace GoatShooooting.Framework;

public enum GameShellState
{
    Title,
    ModeSelect,
    DifficultySelect,
    ShipSelect,
    Playing,
    Pause,
    Options,
    Result,
    Leaderboard
}

public enum GameShellCommand
{
    None,
    StartRun,
    ResumeRun,
    RetryRun,
    ReturnToTitle,
    GameSelectionChanged,
    RunSelectionChanged,
    OpenLeaderboard,
    SettingsChanged,
    SaveSettings,
    Quit
}

public sealed record RunSelectionOption(
    string Id,
    string? ConfigurationId = null,
    bool IsAvailable = true,
    string? UnlockId = null)
{
    public bool IsEnabled(IReadOnlySet<string> unlocks) =>
        IsAvailable && (string.IsNullOrWhiteSpace(UnlockId) || unlocks.Contains(UnlockId));
}

public sealed record GameRunOptions(
    string GameId,
    IReadOnlyList<RunSelectionOption> Modes,
    IReadOnlyList<RunSelectionOption> Difficulties,
    IReadOnlyList<RunSelectionOption> Ships,
    string? DefaultModeId = null,
    string? DefaultDifficultyId = null,
    string? DefaultShipId = null);

public static class RunSelectionConfiguration
{
    public static RunConfiguration Create(GameShell shell, long seed)
    {
        ArgumentNullException.ThrowIfNull(shell);
        return new RunConfiguration(
            shell.SelectedGameId,
            seed,
            shell.SelectedRuleSetConfigurationId,
            shell.SelectedDifficultyConfigurationId,
            shell.SelectedShipConfigurationId);
    }
}

/// <summary>Window-independent production menu state machine.</summary>
public sealed class GameShell
{
    private static readonly string[] TitleItems = ["START", "LEADERBOARD", "OPTIONS", "QUIT"];
    private static readonly string[] PauseItems = ["RESUME", "OPTIONS", "RETRY", "TITLE"];
    private static readonly string[] ResultItems = ["RETRY", "LEADERBOARD", "TITLE"];
    private readonly IReadOnlyDictionary<string, GameRunOptions> _runOptions;
    private readonly string[] _gameIds;
    private readonly HashSet<string> _unlocks;
    private readonly Dictionary<string, string> _lastModes;
    private readonly Dictionary<string, string> _lastDifficulties;
    private readonly Dictionary<string, string> _lastShips;
    private GameShellState _optionsReturnState;
    private int _selectionIndex;
    private bool _keyBindingRejected;
    private GameShellState _leaderboardReturnState = GameShellState.Title;
    private RunSelectionOption _selectedMode = null!;
    private RunSelectionOption _selectedDifficulty = null!;
    private RunSelectionOption _selectedShip = null!;

    public GameShell(GameSettings settings)
        : this(settings, [LegacyOptions("sample")], "sample", new PlayerProfile())
    {
    }

    public GameShell(GameSettings settings, IEnumerable<string> gameIds, string selectedGameId)
        : this(
            settings,
            (gameIds ?? throw new ArgumentNullException(nameof(gameIds))).Select(LegacyOptions),
            selectedGameId,
            new PlayerProfile())
    {
    }

    public GameShell(
        GameSettings settings,
        IEnumerable<GameRunOptions> runOptions,
        string selectedGameId,
        PlayerProfile profile)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ArgumentNullException.ThrowIfNull(runOptions);
        ArgumentNullException.ThrowIfNull(profile);
        _unlocks = new HashSet<string>(profile.Unlocks, StringComparer.Ordinal);
        _lastModes = new Dictionary<string, string>(profile.LastRuleSetIds, StringComparer.Ordinal);
        _lastDifficulties = new Dictionary<string, string>(profile.LastDifficultyIds, StringComparer.Ordinal);
        _lastShips = new Dictionary<string, string>(profile.LastShipIds, StringComparer.Ordinal);
        _runOptions = runOptions.ToDictionary(static options => options.GameId, StringComparer.Ordinal);
        _gameIds = _runOptions.Keys.Order(StringComparer.Ordinal).ToArray();
        if (_gameIds.Length == 0) throw new ArgumentException("At least one game id is required.", nameof(runOptions));
        foreach (var options in _runOptions.Values)
        {
            if (options.Modes.Count == 0 || options.Difficulties.Count == 0 || options.Ships.Count == 0)
                throw new ArgumentException($"Game '{options.GameId}' must expose mode, difficulty, and ship choices.", nameof(runOptions));
        }

        SelectedGameId = _runOptions.ContainsKey(selectedGameId) ? selectedGameId : _gameIds[0];
        RestoreSelections();
    }

    public GameShellState State { get; private set; } = GameShellState.Title;
    public GameSettings Settings { get; private set; }
    public string SelectedGameId { get; private set; }
    public string SelectedModeId => _selectedMode.Id;
    public string SelectedDifficultyId => _selectedDifficulty.Id;
    public string SelectedShipId => _selectedShip.Id;
    public string? SelectedRuleSetConfigurationId => _selectedMode.ConfigurationId;
    public string? SelectedDifficultyConfigurationId => _selectedDifficulty.ConfigurationId;
    public string? SelectedShipConfigurationId => _selectedShip.ConfigurationId;
    public ScoreCategoryKey SelectedCategory => new(
        SelectedGameId,
        SelectedModeId,
        SelectedDifficultyId,
        SelectedShipId);
    public int SelectionIndex => _selectionIndex;
    public bool IsAwaitingKeyBinding { get; private set; }
    public IReadOnlyList<string> MenuItems => State switch
    {
        GameShellState.Title => TitleItems,
        GameShellState.ModeSelect => SelectionLabels(Current.Modes),
        GameShellState.DifficultySelect => SelectionLabels(Current.Difficulties),
        GameShellState.ShipSelect => SelectionLabels(Current.Ships),
        GameShellState.Pause => PauseItems,
        GameShellState.Result => ResultItems,
        GameShellState.Leaderboard => ["BACK"],
        GameShellState.Options => OptionsMenu.ItemLabels,
        _ => []
    };

    public string SelectedValue => State == GameShellState.Options
        ? IsAwaitingKeyBinding
            ? _keyBindingRejected ? "KEY IN USE" : "PRESS A KEY"
            : OptionsMenu.GetValue(Settings, _selectionIndex)
        : string.Empty;

    public GameShellCommand Update(IMenuInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (State == GameShellState.Playing) return GameShellCommand.None;
        if (State == GameShellState.Options && IsAwaitingKeyBinding) return UpdateKeyBinding(input);
        if (input.CancelPressed) return Cancel();

        if (State == GameShellState.Title && _selectionIndex == 0 && (input.LeftPressed || input.RightPressed))
        {
            var direction = input.LeftPressed ? -1 : 1;
            var gameIndex = Array.IndexOf(_gameIds, SelectedGameId);
            SelectedGameId = _gameIds[(gameIndex + direction + _gameIds.Length) % _gameIds.Length];
            RestoreSelections();
            return GameShellCommand.GameSelectionChanged;
        }

        var items = MenuItems;
        if (input.UpPressed) _selectionIndex = (_selectionIndex + items.Count - 1) % items.Count;
        else if (input.DownPressed) _selectionIndex = (_selectionIndex + 1) % items.Count;

        if (State == GameShellState.Options) return UpdateOptions(input);
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
        if (State != GameShellState.Result) return GameShellCommand.None;
        State = GameShellState.Playing;
        _selectionIndex = 0;
        return GameShellCommand.RetryRun;
    }

    private GameRunOptions Current => _runOptions[SelectedGameId];

    private GameShellCommand UpdateKeyBinding(IMenuInput input)
    {
        if (input.CancelPressed)
        {
            IsAwaitingKeyBinding = false;
            _keyBindingRejected = false;
            return GameShellCommand.None;
        }
        if (input.NewlyPressedKey is null) return GameShellCommand.None;
        if (!OptionsMenu.TryBindInput(Settings, _selectionIndex, input.NewlyPressedKey, out var adjustedSettings))
        {
            _keyBindingRejected = true;
            return GameShellCommand.None;
        }
        Settings = adjustedSettings;
        IsAwaitingKeyBinding = false;
        _keyBindingRejected = false;
        return GameShellCommand.SettingsChanged;
    }

    private GameShellCommand UpdateOptions(IMenuInput input)
    {
        if (OptionsMenu.IsInputIndex(_selectionIndex) && input.ConfirmPressed)
        {
            IsAwaitingKeyBinding = true;
            _keyBindingRejected = false;
            return GameShellCommand.None;
        }
        var direction = input.LeftPressed ? -1 : input.RightPressed || input.ConfirmPressed ? 1 : 0;
        if (direction == 0) return GameShellCommand.None;
        if (_selectionIndex == OptionsMenu.BackIndex)
            return input.ConfirmPressed ? CloseOptions() : GameShellCommand.None;
        Settings = OptionsMenu.Adjust(Settings, _selectionIndex, direction);
        return GameShellCommand.SettingsChanged;
    }

    private GameShellCommand Cancel() => State switch
    {
        GameShellState.Title => GameShellCommand.Quit,
        GameShellState.ModeSelect => ReturnToTitle(),
        GameShellState.DifficultySelect => OpenSelection(GameShellState.ModeSelect, Current.Modes, _selectedMode),
        GameShellState.ShipSelect => OpenSelection(GameShellState.DifficultySelect, Current.Difficulties, _selectedDifficulty),
        GameShellState.Pause => ResumeFromMenu(),
        GameShellState.Result => ReturnToTitle(),
        GameShellState.Leaderboard => CloseLeaderboard(),
        GameShellState.Options => CloseOptions(),
        _ => GameShellCommand.None
    };

    private GameShellCommand ConfirmSelection() => State switch
    {
        GameShellState.Title => ConfirmTitle(),
        GameShellState.ModeSelect => ConfirmRunOption(Current.Modes, GameShellState.DifficultySelect),
        GameShellState.DifficultySelect => ConfirmRunOption(Current.Difficulties, GameShellState.ShipSelect),
        GameShellState.ShipSelect => ConfirmShip(),
        GameShellState.Pause => ConfirmPause(),
        GameShellState.Result => ConfirmResult(),
        GameShellState.Leaderboard => ReturnToTitle(),
        _ => GameShellCommand.None
    };

    private GameShellCommand ConfirmTitle()
    {
        if (_selectionIndex == 1)
        {
            _leaderboardReturnState = GameShellState.Title;
            State = GameShellState.Leaderboard;
            _selectionIndex = 0;
            return GameShellCommand.OpenLeaderboard;
        }
        if (_selectionIndex == 2)
        {
            OpenOptions(GameShellState.Title);
            return GameShellCommand.None;
        }
        if (_selectionIndex == 3) return GameShellCommand.Quit;
        return OpenSelection(GameShellState.ModeSelect, Current.Modes, _selectedMode);
    }

    private GameShellCommand ConfirmRunOption(
        IReadOnlyList<RunSelectionOption> options,
        GameShellState nextState)
    {
        var selected = options[_selectionIndex];
        if (!selected.IsEnabled(_unlocks)) return GameShellCommand.None;
        if (State == GameShellState.ModeSelect)
        {
            _selectedMode = selected;
            _lastModes[SelectedGameId] = selected.Id;
        }
        else
        {
            _selectedDifficulty = selected;
            _lastDifficulties[SelectedGameId] = selected.Id;
        }
        var nextOptions = nextState == GameShellState.DifficultySelect ? Current.Difficulties : Current.Ships;
        var nextSelected = nextState == GameShellState.DifficultySelect ? _selectedDifficulty : _selectedShip;
        OpenSelection(nextState, nextOptions, nextSelected);
        return GameShellCommand.RunSelectionChanged;
    }

    private GameShellCommand ConfirmShip()
    {
        var selected = Current.Ships[_selectionIndex];
        if (!selected.IsEnabled(_unlocks)) return GameShellCommand.None;
        _selectedShip = selected;
        _lastShips[SelectedGameId] = selected.Id;
        State = GameShellState.Playing;
        _selectionIndex = 0;
        return GameShellCommand.StartRun;
    }

    private GameShellCommand ConfirmPause()
    {
        switch (_selectionIndex)
        {
            case 0: return ResumeFromMenu();
            case 1:
                OpenOptions(GameShellState.Pause);
                return GameShellCommand.None;
            case 2:
                State = GameShellState.Playing;
                _selectionIndex = 0;
                return GameShellCommand.RetryRun;
            default: return ReturnToTitle();
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
        if (_selectionIndex == 1)
        {
            _leaderboardReturnState = GameShellState.Result;
            State = GameShellState.Leaderboard;
            _selectionIndex = 0;
            return GameShellCommand.OpenLeaderboard;
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

    private GameShellCommand CloseLeaderboard()
    {
        State = _leaderboardReturnState;
        _selectionIndex = 0;
        return State == GameShellState.Title
            ? GameShellCommand.ReturnToTitle
            : GameShellCommand.None;
    }

    private void OpenOptions(GameShellState returnState)
    {
        _optionsReturnState = returnState;
        State = GameShellState.Options;
        _selectionIndex = 0;
        IsAwaitingKeyBinding = false;
        _keyBindingRejected = false;
    }

    private GameShellCommand CloseOptions()
    {
        State = _optionsReturnState;
        _selectionIndex = 0;
        IsAwaitingKeyBinding = false;
        _keyBindingRejected = false;
        return GameShellCommand.SaveSettings;
    }

    private GameShellCommand OpenSelection(
        GameShellState state,
        IReadOnlyList<RunSelectionOption> options,
        RunSelectionOption selected)
    {
        State = state;
        _selectionIndex = Math.Max(0, options.ToList().FindIndex(option => option.Id == selected.Id));
        return GameShellCommand.None;
    }

    private IReadOnlyList<string> SelectionLabels(IReadOnlyList<RunSelectionOption> options) =>
        options.Select(option => option.IsEnabled(_unlocks)
            ? option.Id.ToUpperInvariant()
            : $"{option.Id.ToUpperInvariant()}  LOCKED").ToArray();

    private void RestoreSelections()
    {
        _selectedMode = Restore(
            Current.Modes,
            _lastModes.GetValueOrDefault(SelectedGameId) ?? Current.DefaultModeId);
        _selectedDifficulty = Restore(
            Current.Difficulties,
            _lastDifficulties.GetValueOrDefault(SelectedGameId) ?? Current.DefaultDifficultyId);
        _selectedShip = Restore(
            Current.Ships,
            _lastShips.GetValueOrDefault(SelectedGameId) ?? Current.DefaultShipId);
    }

    private RunSelectionOption Restore(IReadOnlyList<RunSelectionOption> options, string? selectedId) =>
        options.FirstOrDefault(option => option.Id == selectedId && option.IsEnabled(_unlocks)) ??
        options.FirstOrDefault(option => option.IsEnabled(_unlocks)) ?? options[0];

    private static GameRunOptions LegacyOptions(string gameId) => new(
        gameId,
        [new RunSelectionOption(ScoreCategoryKey.LegacySelection)],
        [new RunSelectionOption(ScoreCategoryKey.LegacySelection)],
        [new RunSelectionOption(ScoreCategoryKey.LegacySelection)]);
}
