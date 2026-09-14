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
    Leaderboard,
    TrainingSetup,
    Information
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
    StartTraining,
    PlayReplay,
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
    string? DefaultShipId = null,
    IReadOnlyList<TrainingLocationOption>? TrainingLocations = null);

public sealed record TrainingLocationOption(string StageId, string? CheckpointId = null)
{
    public string Label => string.IsNullOrWhiteSpace(CheckpointId)
        ? StageId.ToUpperInvariant()
        : $"{StageId.ToUpperInvariant()} / {CheckpointId.ToUpperInvariant()}";
}

public sealed record TrainingSetupSelection(
    string StageId,
    string? CheckpointId,
    int Power,
    int Lives,
    int Bombs,
    double Rank,
    int Gauge,
    bool Invincible,
    bool SlowPractice,
    bool ShowHitboxes);

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

    public static RunConfiguration CreateTraining(GameShell shell, long seed)
    {
        ArgumentNullException.ThrowIfNull(shell);
        var training = shell.TrainingSelection;
        return new RunConfiguration(
            shell.SelectedGameId,
            seed,
            shell.SelectedRuleSetConfigurationId,
            shell.SelectedDifficultyConfigurationId,
            shell.SelectedShipConfigurationId,
            training.StageId,
            training.CheckpointId,
            isPractice: true,
            training.Power,
            training.Lives,
            training.Bombs,
            training.Rank,
            training.Gauge,
            training.Invincible ? float.MaxValue : null,
            training.SlowPractice,
            training.ShowHitboxes);
    }
}

/// <summary>Window-independent production menu state machine.</summary>
public sealed class GameShell
{
    private static readonly string[] TrainingItems =
        ["LOCATION", "POWER", "LIVES", "BOMBS", "RANK", "GAUGE", "INVINCIBLE", "SLOW", "HITBOX", "START", "BACK"];
    private readonly IReadOnlyDictionary<string, GameRunOptions> _runOptions;
    private readonly string[] _gameIds;
    private readonly HashSet<string> _unlocks;
    private readonly Dictionary<string, string> _lastModes;
    private readonly Dictionary<string, string> _lastDifficulties;
    private readonly Dictionary<string, string> _lastShips;
    private readonly IStringCatalog _strings;
    private GameShellState _optionsReturnState;
    private int _selectionIndex;
    private bool _keyBindingRejected;
    private GameShellState _leaderboardReturnState = GameShellState.Title;
    private RunSelectionOption _selectedMode = null!;
    private RunSelectionOption _selectedDifficulty = null!;
    private RunSelectionOption _selectedShip = null!;
    private IReadOnlyList<CompletedRunRecord> _leaderboardEntries = Array.Empty<CompletedRunRecord>();
    private string? _resultReplayPath;
    private int _trainingLocationIndex;
    private int _trainingPower;
    private int _trainingLives = 3;
    private int _trainingBombs = 3;
    private int _trainingRank;
    private int _trainingGauge;
    private bool _trainingInvincible;
    private bool _trainingSlow;
    private bool _trainingHitboxes = true;
    private int _informationPage;

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
        PlayerProfile profile,
        IStringCatalog? strings = null)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ArgumentNullException.ThrowIfNull(runOptions);
        ArgumentNullException.ThrowIfNull(profile);
        _strings = strings ?? LocalizedStringCatalog.CreateBuiltIn(settings.Locale);
        _strings.SetLocale(settings.Locale);
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
    public string? SelectedReplayPath { get; private set; }
    public TrainingSetupSelection TrainingSelection
    {
        get
        {
            var locations = TrainingLocations;
            var location = locations[Math.Clamp(_trainingLocationIndex, 0, locations.Count - 1)];
            return new TrainingSetupSelection(
                location.StageId,
                location.CheckpointId,
                _trainingPower,
                _trainingLives,
                _trainingBombs,
                _trainingRank / 10d,
                _trainingGauge,
                _trainingInvincible,
                _trainingSlow,
                _trainingHitboxes);
        }
    }
    public IReadOnlyList<string> MenuItems => State switch
    {
        GameShellState.Title =>
        [Text("menu.start"),
            Text("menu.training"),
            Text("menu.leaderboard"),
            Text("menu.options"),
            Text("menu.information"),
            Text("menu.quit")],
        GameShellState.ModeSelect => SelectionLabels(Current.Modes),
        GameShellState.DifficultySelect => SelectionLabels(Current.Difficulties),
        GameShellState.ShipSelect => SelectionLabels(Current.Ships),
        GameShellState.Pause => [Text("menu.resume"), Text("menu.options"), Text("menu.retry"), Text("menu.title")],
        GameShellState.Result => _resultReplayPath is null
            ? [Text("menu.retry"), Text("menu.leaderboard"), Text("menu.title")]
            : [Text("menu.retry"), Text("menu.playReplay"), Text("menu.leaderboard"), Text("menu.title")],
        GameShellState.Leaderboard => LeaderboardItems,
        GameShellState.TrainingSetup => TrainingItems,
        GameShellState.Options => OptionsMenu.GetLabels(_strings),
        GameShellState.Information => [Text("menu.back")],
        _ => []
    };

    public string InformationTitle => Text($"info.{InformationPageId}.title");
    public string InformationBody => Text($"info.{InformationPageId}.body");

    public string SelectedValue => State switch
    {
        GameShellState.Options => IsAwaitingKeyBinding
            ? _keyBindingRejected ? "KEY IN USE" : "PRESS A KEY"
            : OptionsMenu.GetValue(Settings, _selectionIndex),
        GameShellState.TrainingSetup => GetTrainingValue(_selectionIndex),
        _ => string.Empty
    };

    public GameShellCommand Update(IMenuInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (State == GameShellState.Playing) return GameShellCommand.None;
        if (State == GameShellState.Options && IsAwaitingKeyBinding) return UpdateKeyBinding(input);
        if (input.CancelPressed) return Cancel();
        if (State == GameShellState.Information)
        {
            if (input.LeftPressed) _informationPage = Wrap(_informationPage - 1, 4);
            if (input.RightPressed) _informationPage = Wrap(_informationPage + 1, 4);
            return input.ConfirmPressed ? ReturnToTitle() : GameShellCommand.None;
        }

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
        if (State == GameShellState.TrainingSetup) return UpdateTraining(input);
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

    public void ReplaceSettings(GameSettings settings)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _strings.SetLocale(settings.Locale);
    }

    public void SetLeaderboardEntries(IReadOnlyList<CompletedRunRecord> entries) =>
        _leaderboardEntries = entries ?? throw new ArgumentNullException(nameof(entries));

    public void SetResultReplay(string? replayPath) => _resultReplayPath = replayPath;

    public GameShellCommand StartAutomatedRun()
    {
        State = GameShellState.Playing;
        _selectionIndex = 0;
        return GameShellCommand.StartRun;
    }

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
        GameShellState.TrainingSetup => ReturnToTitle(),
        GameShellState.Options => CloseOptions(),
        GameShellState.Information => ReturnToTitle(),
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
        GameShellState.Leaderboard => ConfirmLeaderboard(),
        GameShellState.TrainingSetup => ConfirmTraining(),
        _ => GameShellCommand.None
    };

    private GameShellCommand ConfirmTitle()
    {
        if (_selectionIndex == 1)
        {
            State = GameShellState.TrainingSetup;
            _selectionIndex = 0;
            return GameShellCommand.None;
        }
        if (_selectionIndex == 2)
        {
            _leaderboardReturnState = GameShellState.Title;
            State = GameShellState.Leaderboard;
            _selectionIndex = 0;
            return GameShellCommand.OpenLeaderboard;
        }
        if (_selectionIndex == 3)
        {
            OpenOptions(GameShellState.Title);
            return GameShellCommand.None;
        }
        if (_selectionIndex == 4)
        {
            State = GameShellState.Information;
            _selectionIndex = 0;
            _informationPage = 0;
            return GameShellCommand.None;
        }
        if (_selectionIndex == 5) return GameShellCommand.Quit;
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
        if (_resultReplayPath is not null && _selectionIndex == 1)
        {
            SelectedReplayPath = _resultReplayPath;
            State = GameShellState.Playing;
            _selectionIndex = 0;
            return GameShellCommand.PlayReplay;
        }
        var leaderboardIndex = _resultReplayPath is null ? 1 : 2;
        if (_selectionIndex == leaderboardIndex)
        {
            _leaderboardReturnState = GameShellState.Result;
            State = GameShellState.Leaderboard;
            _selectionIndex = 0;
            return GameShellCommand.OpenLeaderboard;
        }
        return ReturnToTitle();
    }

    private GameShellCommand ConfirmLeaderboard()
    {
        if (_selectionIndex >= _leaderboardEntries.Count) return CloseLeaderboard();
        var replayPath = _leaderboardEntries[_selectionIndex].ReplayPath;
        if (string.IsNullOrWhiteSpace(replayPath)) return GameShellCommand.None;
        SelectedReplayPath = replayPath;
        State = GameShellState.Playing;
        _selectionIndex = 0;
        return GameShellCommand.PlayReplay;
    }

    private GameShellCommand ConfirmTraining()
    {
        if (_selectionIndex == TrainingItems.Length - 2)
        {
            State = GameShellState.Playing;
            _selectionIndex = 0;
            return GameShellCommand.StartTraining;
        }
        if (_selectionIndex == TrainingItems.Length - 1) return ReturnToTitle();
        return GameShellCommand.None;
    }

    private GameShellCommand UpdateTraining(IMenuInput input)
    {
        var direction = input.LeftPressed ? -1 : input.RightPressed ? 1 : 0;
        if (direction != 0)
        {
            switch (_selectionIndex)
            {
                case 0:
                    _trainingLocationIndex = Wrap(_trainingLocationIndex + direction, TrainingLocations.Count);
                    break;
                case 1: _trainingPower = Math.Clamp(_trainingPower + (direction * 10), 0, 100); break;
                case 2: _trainingLives = Math.Clamp(_trainingLives + direction, 1, 9); break;
                case 3: _trainingBombs = Math.Clamp(_trainingBombs + direction, 0, 9); break;
                case 4: _trainingRank = Math.Clamp(_trainingRank + direction, 0, 10); break;
                case 5: _trainingGauge = Math.Clamp(_trainingGauge + (direction * 10), 0, 100); break;
                case 6: _trainingInvincible = !_trainingInvincible; break;
                case 7: _trainingSlow = !_trainingSlow; break;
                case 8: _trainingHitboxes = !_trainingHitboxes; break;
            }
        }
        return input.ConfirmPressed ? ConfirmTraining() : GameShellCommand.None;
    }

    private string GetTrainingValue(int index) => index switch
    {
        0 => TrainingLocations[_trainingLocationIndex].Label,
        1 => _trainingPower.ToString(System.Globalization.CultureInfo.InvariantCulture),
        2 => _trainingLives.ToString(System.Globalization.CultureInfo.InvariantCulture),
        3 => _trainingBombs.ToString(System.Globalization.CultureInfo.InvariantCulture),
        4 => (_trainingRank / 10d).ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
        5 => _trainingGauge.ToString(System.Globalization.CultureInfo.InvariantCulture),
        6 => _trainingInvincible ? "ON" : "OFF",
        7 => _trainingSlow ? "ON" : "OFF",
        8 => _trainingHitboxes ? "ON" : "OFF",
        _ => string.Empty
    };

    private IReadOnlyList<TrainingLocationOption> TrainingLocations =>
        Current.TrainingLocations is { Count: > 0 } locations
            ? locations
            : [new TrainingLocationOption("stage")];

    private IReadOnlyList<string> LeaderboardItems =>
        _leaderboardEntries
            .Select((entry, index) => string.IsNullOrWhiteSpace(entry.ReplayPath)
                ? $"{index + 1:D2}  {entry.Score:D8}"
                : $"PLAY {index + 1:D2}  {entry.Score:D8}")
            .Append(Text("menu.back"))
            .ToArray();

    private static int Wrap(int value, int count) => (value + count) % count;

    private string InformationPageId => _informationPage switch
    {
        0 => "controls",
        1 => "scoring",
        2 => "credits",
        _ => "licenses"
    };

    private string Text(string key) => _strings.Get(key);

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
        _trainingLocationIndex = 0;
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
