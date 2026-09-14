using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using GoatShooooting.Core;
using GoatShooooting.Definitions;
using GoatShooooting.Platform;
using GoatShooooting.Runtime;

namespace GoatShooooting.Framework;

/// <summary>Thin MonoGame host around the renderer-independent production simulation.</summary>
public sealed class ShootingGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly GameInputState _input;
    private readonly GameShell _shell;
    private readonly IUserDataStore? _userDataStore;
    private readonly ILeaderboardService? _leaderboardService;
    private readonly IReplayStore? _replayStore;
    private readonly IReadOnlyDictionary<string, IVisualAssetCatalog> _visualAssetCatalogs;
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, ResolvedAudioAsset>> _audioAssetCatalogs;
    private readonly IReadOnlyDictionary<string, IDefinitionRepository> _definitionRepositories;
    private readonly PlayerProfileService _profileService = new();
    private readonly RunCompletionTracker _runCompletionTracker = new();
    private readonly RenderSystem _renderSystem = new();
    private readonly PresentationEffectSystem _presentation = new();
    private readonly FixedTickAccumulator _simulationClock = new();
    private ShootingSimulation _simulation;
    private FrameSnapshot _frameSnapshot;
    private PlayerProfile _profile;
    private string _gameId;
    private GameScreenLayout _layout;
    private SpriteBatch? _spriteBatch;
    private Texture2D? _pixel;
    private RenderTarget2D? _logicalCanvas;
    private GameAudio? _audio;
    private DisplaySettingsApplicator? _displaySettingsApplicator;
    private GameSettings _appliedSettings;
    private float _shakeRemaining;
    private float _vibrationRemaining;
    private float _leftMotorStrength;
    private float _rightMotorStrength;
    private bool _showControllerDisconnectedMessage;
    private string _currentRunId = Guid.NewGuid().ToString("N");
    private CompletedRunRecord? _lastRun;
    private ReplayRecorder? _replayRecorder;
    private ReplayPlaybackSession? _replayPlayback;
    private ReplayPlaybackController? _replayController;
    private bool _isReplayPlayback;
    private string? _replayError;
    private IVisualAssetCatalog? _visualAssets;
    private int _assetDefinitionReloadCount;
    private readonly string? _renderScreenshotPath;
    private readonly IStringCatalog _strings;
    private bool _renderScreenshotSaved;

    public ShootingGame(IDefinitionRepository definitionRepository)
        : this(definitionRepository, userDataStore: null, settings: new GameSettings())
    {
    }

    public ShootingGame(
        IDefinitionRepository definitionRepository,
        IUserDataStore? userDataStore,
        GameSettings settings)
        : this(
            new Dictionary<string, IDefinitionRepository>(StringComparer.Ordinal)
            {
                ["sample"] = definitionRepository
            },
            "sample",
            userDataStore,
            settings,
            new PlayerProfile(),
            leaderboardService: null,
            replayStore: null,
            visualAssetCatalogs: null,
            audioAssetCatalogs: null,
            stringCatalogs: null,
            renderScreenshotPath: null)
    {
    }

    public ShootingGame(
        IReadOnlyDictionary<string, IDefinitionRepository> definitionRepositories,
        string gameId,
        IUserDataStore? userDataStore,
        GameSettings settings,
        PlayerProfile profile,
        ILeaderboardService? leaderboardService = null,
        IReplayStore? replayStore = null,
        IReadOnlyDictionary<string, IVisualAssetCatalog>? visualAssetCatalogs = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, ResolvedAudioAsset>>? audioAssetCatalogs = null,
        IReadOnlyDictionary<string, IStringCatalog>? stringCatalogs = null,
        string? renderScreenshotPath = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(definitionRepositories);
        ArgumentNullException.ThrowIfNull(profile);
        if (!definitionRepositories.TryGetValue(gameId, out var definitionRepository))
        {
            throw new ArgumentException($"Game id '{gameId}' is not available.", nameof(gameId));
        }

        _definitionRepositories = definitionRepositories;
        _gameId = gameId;
        _profile = profile;
        _input = new GameInputState(settings.Input);
        _strings = stringCatalogs?.GetValueOrDefault(gameId) ??
            LocalizedStringCatalog.CreateBuiltIn(settings.Locale);
        _shell = new GameShell(
            settings,
            definitionRepositories.Select(pair => CreateRunOptions(pair.Key, pair.Value)),
            gameId,
            profile,
            _strings);
        _appliedSettings = settings;
        _userDataStore = userDataStore;
        _leaderboardService = leaderboardService;
        _replayStore = replayStore;
        _visualAssetCatalogs = visualAssetCatalogs ??
            new Dictionary<string, IVisualAssetCatalog>(StringComparer.Ordinal);
        _audioAssetCatalogs = audioAssetCatalogs ??
            new Dictionary<string, IReadOnlyDictionary<string, ResolvedAudioAsset>>(StringComparer.Ordinal);
        _renderScreenshotPath = string.IsNullOrWhiteSpace(renderScreenshotPath)
            ? null
            : Path.GetFullPath(renderScreenshotPath);
        _simulation = CreateSimulation(definitionRepository);
        _frameSnapshot = _simulation.CaptureFrame(_renderSystem);
        _layout = PrimitiveRenderLayout.CreateGameScreenLayout(_simulation.Definitions.Game);
        var displayMode = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
        var isBorderless = settings.Display.WindowMode == WindowMode.BorderlessFullscreen;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = isBorderless
                ? displayMode.Width
                : _layout.Window.Width * settings.Display.WindowScale,
            PreferredBackBufferHeight = isBorderless
                ? displayMode.Height
                : _layout.Window.Height * settings.Display.WindowScale,
            SynchronizeWithVerticalRetrace = settings.Display.VSync,
            HardwareModeSwitch = false,
            IsFullScreen = isBorderless
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = false;
        Window.Title = "goat-shooooting — TITLE";
    }

    protected override void Initialize()
    {
        base.Initialize();
        _displaySettingsApplicator = new DisplaySettingsApplicator(
            new MonoGameDisplaySettingsTarget(_graphics),
            _appliedSettings.Display);
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _logicalCanvas = CreateLogicalCanvas();
        _audio = new GameAudio(_appliedSettings.Audio);
        _audio.Load(_audioAssetCatalogs.GetValueOrDefault(_gameId) ??
            new Dictionary<string, ResolvedAudioAsset>(StringComparer.Ordinal));
        ApplyPresentationSettings(_appliedSettings.Gameplay);
        SelectVisualAssets();
        if (_renderScreenshotPath is not null) HandleShellCommand(_shell.StartAutomatedRun());
    }

    protected override void Update(GameTime gameTime)
    {
        _input.Update();
        if (_input.ToggleFullscreenPressed)
        {
            ToggleFullscreen();
        }

        if (_shell.State != GameShellState.Playing)
        {
            if (_shell.State == GameShellState.Result && _input.RetryPressed)
            {
                HandleShellCommand(_shell.RetryResult());
                UpdateWindowTitle();
                base.Update(gameTime);
                return;
            }

            if (_shell.State == GameShellState.Pause && _input.PausePressed)
            {
                _simulation.SetPaused(false);
                _simulationClock.Reset();
                _shell.Resume();
                UpdateWindowTitle();
                base.Update(gameTime);
                return;
            }

            var command = _shell.Update(_input);
            if (_input.UpPressed || _input.DownPressed || _input.LeftPressed || _input.RightPressed ||
                _input.ConfirmPressed || _input.CancelPressed)
                _audio?.Process([new AudioCueEvent(_simulation.RunState.Frame, 0, "se-menu")]);
            HandleShellCommand(command);
            UpdateWindowTitle();
            base.Update(gameTime);
            return;
        }

        if (_input.QuitRequested)
        {
            Exit();
            return;
        }

        if (_isReplayPlayback && _replayController is { } viewer)
        {
            if (_input.LeftPressed) viewer.Slower();
            if (_input.RightPressed) viewer.Faster();
            if (_input.ConfirmPressed) viewer.ToggleHitboxes();
            if (_input.PausePressed)
            {
                viewer.TogglePause();
                _simulationClock.Reset();
            }
            if (viewer.IsPaused)
            {
                UpdateWindowTitle();
                base.Update(gameTime);
                return;
            }
        }

        if (_input.GamePadDisconnectedThisFrame)
        {
            _showControllerDisconnectedMessage = true;
            _simulation.SetPaused(true);
            _simulationClock.Reset();
            _shell.Pause();
        }

        if (_input.IsGamePadConnected || _input.KeyboardInputDetected)
        {
            _showControllerDisconnectedMessage = false;
        }

        if (!_isReplayPlayback && _input.PausePressed)
        {
            _simulation.SetPaused(true);
            _simulationClock.Reset();
            _shell.Pause();
            UpdateWindowTitle();
            base.Update(gameTime);
            return;
        }

        var deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        var frameFeedback = default(SimulationFeedback);
        var elapsedSeconds = gameTime.ElapsedGameTime.TotalSeconds;
        if (_isReplayPlayback && _replayController is not null) elapsedSeconds *= _replayController.Speed;
        else if (_simulation.Configuration.IsPractice && _simulation.Configuration.SlowPractice) elapsedSeconds *= 0.5;
        try
        {
            _simulationClock.Advance(
                elapsedSeconds,
                CaptureSimulationInput,
                inputFrame =>
                {
                    if (_simulation.Status != SimulationStatus.Running) return;
                    if (_replayPlayback is not null) _replayPlayback.Step(_simulation);
                    else
                    {
                        var reloadCount = _simulation.DefinitionReloadCount;
                        _simulation.Tick(inputFrame);
                        if (_simulation.DefinitionReloadCount != reloadCount) BeginReplayRecording();
                        else _replayRecorder?.Record(inputFrame, _simulation);
                    }
                    frameFeedback += _simulation.Feedback;
                    _frameSnapshot = _simulation.CaptureFrame(_renderSystem);
                    _presentation.ObserveTick(_frameSnapshot, _simulation.Events.Events);
                    _audio?.Process(_simulation.Events.Events);
                });
        }
        catch (ReplayException exception)
        {
            _replayError = exception.Message;
            _simulationClock.Reset();
            _shell.SetResultReplay(null);
            _shell.ShowResult();
            UpdateWindowTitle();
            base.Update(gameTime);
            return;
        }
        ApplyLayoutChanges();
        UpdateVisualAssets();
        UpdateMusic();
        _audio?.Update(deltaTime);
        UpdateVibration(deltaTime, frameFeedback);
        _shakeRemaining = Math.Max(0, _shakeRemaining - deltaTime);
        if (frameFeedback.BombsUsed > 0)
        {
            _shakeRemaining = Math.Max(_shakeRemaining, 0.45f);
        }
        else if (frameFeedback.PlayerHits > 0)
        {
            _shakeRemaining = Math.Max(_shakeRemaining, 0.3f);
        }
        else if (frameFeedback.EnemiesDestroyed > 0)
        {
            _shakeRemaining = Math.Max(_shakeRemaining, 0.12f);
        }

        if (_simulation.IsPaused)
        {
            _shell.Pause();
        }
        else if (_simulation.Status != SimulationStatus.Running)
        {
            _simulationClock.Reset();
            RecordRunCompletion();
            _shell.ShowResult();
        }

        UpdateWindowTitle();
        base.Update(gameTime);
    }

    private InputFrame CaptureSimulationInput()
    {
        if (_renderScreenshotPath is null) return InputFrame.Capture(_input);
        var buttons = InputButtons.Fire | InputButtons.Focus;
        if (_simulation.RunState.Frame == 410) buttons |= InputButtons.Bomb;
        return new InputFrame(0, 0, buttons);
    }

    private void UpdateWindowTitle()
    {
        Window.Title = _showControllerDisconnectedMessage
            ? "goat-shooooting — CONTROLLER DISCONNECTED — reconnect or use keyboard"
            : _replayError is not null
            ? $"goat-shooooting — REPLAY ERROR — {_replayError}"
            : _visualAssets?.LastReloadError is { } assetError
            ? $"goat-shooooting — ASSET ERROR — {assetError}"
            : _audio?.LastError is { } audioError
            ? $"goat-shooooting — AUDIO FALLBACK — {audioError}"
            : _shell.State == GameShellState.Title
            ? $"goat-shooooting — TITLE — {_gameId}"
            : _shell.State is GameShellState.ModeSelect or GameShellState.DifficultySelect or
                GameShellState.ShipSelect or GameShellState.TrainingSetup
            ? $"goat-shooooting — {_shell.State.ToString().ToUpperInvariant()}"
            : _shell.State == GameShellState.Leaderboard
            ? "goat-shooooting — LEADERBOARD"
            : _shell.State == GameShellState.Options
            ? _shell.IsAwaitingKeyBinding
                ? "goat-shooooting — OPTIONS — PRESS A KEY"
                : "goat-shooooting — OPTIONS"
            : _simulation.DefinitionReloadError is not null
            ? $"goat-shooooting — DEFINITION ERROR — {_simulation.DefinitionReloadError}"
            : _isReplayPlayback && _replayController is { } viewer
            ? $"goat-shooooting — REPLAY {viewer.Speed:F2}X{(viewer.IsPaused ? " — PAUSED" : string.Empty)}"
            : _simulation.IsPaused
            ? $"goat-shooooting — PAUSED — LIVES {_simulation.Player.Get<LivesComponent>().Remaining} — BOMBS {_simulation.Player.Get<BombComponent>().Remaining} — P/START to resume"
            : _simulation.Status switch
            {
                SimulationStatus.GameOver => $"goat-shooooting — GAME OVER — SCORE {_simulation.Telemetry.Score} — R/ENTER/A to retry",
                SimulationStatus.StageClear => $"goat-shooooting — ALL STAGES CLEAR — SCORE {_simulation.Telemetry.Score} — R/ENTER/A to retry",
                _ when _simulation.Phase == StagePhase.Opening =>
                    $"goat-shooooting — STAGE {_simulation.StageNumber:D2} — {_simulation.CurrentStage.Title}",
                _ when _simulation.Phase == StagePhase.Results =>
                    $"goat-shooooting — STAGE CLEAR — STAGE SCORE {_simulation.LastStageScore} — TOTAL {_simulation.Telemetry.Score}",
                _ => $"goat-shooooting — STAGE {_simulation.StageNumber:D2} — LIVES {_simulation.Player.Get<LivesComponent>().Remaining} — BOMBS {_simulation.Player.Get<BombComponent>().Remaining} — SCORE {_simulation.Telemetry.Score}"
            };
    }

    private void HandleShellCommand(GameShellCommand command)
    {
        switch (command)
        {
            case GameShellCommand.StartRun:
                if (_definitionRepositories.TryGetValue(_shell.SelectedGameId, out var repository))
                {
                    _simulation = CreateSimulation(repository);
                    _currentRunId = Guid.NewGuid().ToString("N");
                    _profile = _profileService.SelectRunConfiguration(_profile, _shell.SelectedCategory);
                    _userDataStore?.SaveProfile(_profile);
                    ApplyLayoutChanges();
                }
                BeginReplayRecording();
                _simulationClock.Reset();
                _runCompletionTracker.StartRun();
                _showControllerDisconnectedMessage = false;
                ResetPresentation();
                break;
            case GameShellCommand.RetryRun:
                _simulation.Restart();
                _currentRunId = Guid.NewGuid().ToString("N");
                _simulationClock.Reset();
                _runCompletionTracker.StartRun();
                _showControllerDisconnectedMessage = false;
                if (!_simulation.Configuration.IsPractice) BeginReplayRecording();
                ResetPresentation();
                break;
            case GameShellCommand.StartTraining:
                if (_definitionRepositories.TryGetValue(_shell.SelectedGameId, out var trainingRepository))
                {
                    _simulation = CreateSimulation(
                        trainingRepository,
                        RunSelectionConfiguration.CreateTraining(_shell, seed: 0));
                    _gameId = _shell.SelectedGameId;
                    ApplyLayoutChanges();
                }
                ResetReplayState();
                _simulationClock.Reset();
                _runCompletionTracker.StartRun();
                _showControllerDisconnectedMessage = false;
                ResetPresentation();
                break;
            case GameShellCommand.PlayReplay:
                StartReplayPlayback();
                break;
            case GameShellCommand.ResumeRun:
                _simulation.SetPaused(false);
                _simulationClock.Reset();
                _showControllerDisconnectedMessage = false;
                break;
            case GameShellCommand.SettingsChanged:
                ApplySettings(_shell.Settings);
                break;
            case GameShellCommand.GameSelectionChanged:
                SelectGame(_shell.SelectedGameId);
                break;
            case GameShellCommand.RunSelectionChanged:
                break;
            case GameShellCommand.OpenLeaderboard:
                _shell.SetLeaderboardEntries(_leaderboardService?.GetEntries(_shell.SelectedCategory) ?? []);
                break;
            case GameShellCommand.SaveSettings:
                _userDataStore?.SaveSettings(_appliedSettings);
                break;
            case GameShellCommand.Quit:
                Exit();
                break;
            case GameShellCommand.None:
            case GameShellCommand.ReturnToTitle:
            default:
                break;
        }
    }

    private void SelectGame(string gameId)
    {
        if (!_definitionRepositories.TryGetValue(gameId, out var repository))
        {
            return;
        }

        _gameId = gameId;
        _simulation = CreateSimulation(repository);
        _simulationClock.Reset();
        _runCompletionTracker.StartRun();
        _profile = _profileService.SelectGame(_profile, gameId);
        _userDataStore?.SaveProfile(_profile);
        ApplyLayoutChanges();
        SelectVisualAssets();
        _audio?.Load(_audioAssetCatalogs.GetValueOrDefault(_gameId) ??
            new Dictionary<string, ResolvedAudioAsset>(StringComparer.Ordinal));
        ResetPresentation();
    }

    private void SelectVisualAssets()
    {
        _visualAssets = _visualAssetCatalogs.GetValueOrDefault(_gameId);
        if (_visualAssets is null || GraphicsDevice is null) return;
        _visualAssets.Load(GraphicsDevice, _simulation.Definitions);
        _assetDefinitionReloadCount = _simulation.DefinitionReloadCount;
    }

    private void UpdateVisualAssets()
    {
        if (_visualAssets is null) return;
        if (_assetDefinitionReloadCount != _simulation.DefinitionReloadCount)
        {
            _visualAssets.Load(GraphicsDevice, _simulation.Definitions);
            _assetDefinitionReloadCount = _simulation.DefinitionReloadCount;
            return;
        }
        _visualAssets.PollChanges(GraphicsDevice, _simulation.Definitions);
    }

    private void RecordRunCompletion()
    {
        if (_simulation.Configuration.IsPractice || _isReplayPlayback)
        {
            _shell.SetResultReplay(null);
            return;
        }
        var completion = _runCompletionTracker.Observe(_simulation.Status, _simulation.Telemetry.Score);
        if (completion is null)
        {
            return;
        }

        string? replayPath = null;
        if (_replayRecorder is not null && _replayStore is not null)
        {
            try
            {
                replayPath = _replayStore.Save(_currentRunId, _replayRecorder.Complete(_simulation));
            }
            catch (ReplayException exception)
            {
                _replayError = $"Replay could not be saved: {exception.Message}";
            }
            _replayRecorder = null;
        }
        var run = new CompletedRunRecord
        {
            RunId = _currentRunId,
            Category = _shell.SelectedCategory,
            Score = completion.Value.Score,
            Cleared = completion.Value.Cleared,
            Continued = _simulation.RunState.Continued,
            BestStage = _simulation.StageNumber,
            MaximumChain = _simulation.RunState.MaximumChain,
            Grazes = _simulation.Telemetry.PlayerGrazes,
            Misses = _simulation.Telemetry.PlayerDeaths,
            Bombs = _simulation.Telemetry.BombsUsed,
            Continues = _simulation.Telemetry.ContinuesUsed,
            PlayTimeFrames = _simulation.RunState.Frame,
            Timestamp = DateTimeOffset.UtcNow,
            ScoreBreakdown = new Dictionary<string, long>(
                _simulation.RunState.ScoreBreakdown,
                StringComparer.Ordinal),
            ReplayPath = replayPath
        };
        _shell.SetResultReplay(replayPath);
        _lastRun = run;
        _profile = _profileService.RecordCompletedRun(_profile, run);
        _ = _leaderboardService?.Submit(run);
        _userDataStore?.SaveProfile(_profile);
    }

    private ShootingSimulation CreateSimulation(IDefinitionRepository repository) => new(
        repository,
        _input,
        RunSelectionConfiguration.Create(_shell, seed: 0));

    private ShootingSimulation CreateSimulation(
        IDefinitionRepository repository,
        RunConfiguration configuration) => new(repository, _input, configuration);

    private void BeginReplayRecording()
    {
        ResetReplayState();
        if (_replayStore is null) return;
        _replayRecorder = new ReplayRecorder(
            _simulation.Configuration,
            DefinitionContentHasher.Compute(_simulation.Definitions),
            DateTimeOffset.UtcNow);
    }

    private void ResetReplayState()
    {
        _replayRecorder = null;
        _replayPlayback = null;
        _replayController = null;
        _isReplayPlayback = false;
        _replayError = null;
    }

    private void StartReplayPlayback()
    {
        ResetReplayState();
        if (_replayStore is null || string.IsNullOrWhiteSpace(_shell.SelectedReplayPath) ||
            !_definitionRepositories.TryGetValue(_shell.SelectedGameId, out var repository))
        {
            _replayError = "The selected replay is not available.";
            _shell.ShowResult();
            return;
        }
        var load = _replayStore.Load(
            _shell.SelectedReplayPath,
            DefinitionContentHasher.Compute(repository.Load()));
        if (!load.Success)
        {
            _replayError = load.Error ?? "The replay could not be loaded.";
            _shell.ShowResult();
            return;
        }
        var replay = load.Replay!;
        if (!string.Equals(replay.Header.Configuration.GameId, _shell.SelectedGameId, StringComparison.Ordinal))
        {
            _replayError = "Replay game does not match the selected content pack.";
            _shell.ShowResult();
            return;
        }
        _simulation = CreateSimulation(repository, replay.Header.Configuration);
        _replayPlayback = new ReplayPlaybackSession(replay);
        _replayController = new ReplayPlaybackController();
        _isReplayPlayback = true;
        _simulationClock.Reset();
        _runCompletionTracker.StartRun();
        ApplyLayoutChanges();
        ResetPresentation();
    }

    private void ResetPresentation()
    {
        _presentation.Reset();
        _frameSnapshot = _simulation.CaptureFrame(_renderSystem);
    }

    private static GameRunOptions CreateRunOptions(string gameId, IDefinitionRepository repository)
    {
        var definitions = repository.Load();
        var modes = definitions.RuleSets.Count == 0
            ? new[] { new RunSelectionOption(ScoreCategoryKey.LegacySelection) }
            : definitions.RuleSets.Values.OrderBy(static definition => definition.Id, StringComparer.Ordinal)
                .Select(definition => new RunSelectionOption(
                definition.Id, definition.Id, definition.IsAvailable, definition.UnlockId)).ToArray();
        var difficulties = definitions.Difficulties.Count == 0
            ? new[] { new RunSelectionOption(ScoreCategoryKey.LegacySelection) }
            : definitions.Difficulties.Values.OrderBy(static definition => definition.Id, StringComparer.Ordinal)
                .Select(definition => new RunSelectionOption(
                definition.Id, definition.Id, definition.IsAvailable, definition.UnlockId)).ToArray();
        var ships = definitions.Ships.Values.OrderBy(static definition => definition.Id, StringComparer.Ordinal)
            .Select(definition => new RunSelectionOption(
            definition.Id, definition.Id, definition.IsAvailable, definition.UnlockId)).ToArray();
        var trainingLocations = definitions.Stages.Values
            .OrderBy(static stage => stage.Id, StringComparer.Ordinal)
            .SelectMany(stage => CreateTrainingLocations(definitions, stage))
            .ToArray();
        return new GameRunOptions(
            gameId,
            modes,
            difficulties,
            ships,
            string.IsNullOrWhiteSpace(definitions.Game.DefaultRuleSetId)
                ? ScoreCategoryKey.LegacySelection
                : definitions.Game.DefaultRuleSetId,
            definitions.Game.DifficultyIds.FirstOrDefault() ?? ScoreCategoryKey.LegacySelection,
            !string.IsNullOrWhiteSpace(definitions.Game.PlayerId)
                ? definitions.Game.PlayerId
                : definitions.Game.ShipIds.FirstOrDefault(),
            trainingLocations);
    }

    private static IEnumerable<TrainingLocationOption> CreateTrainingLocations(
        DefinitionCatalog definitions,
        StageDefinition stage)
    {
        yield return new TrainingLocationOption(stage.Id);
        var bossIds = stage.Events.Where(static item => item.IsBoss && !string.IsNullOrWhiteSpace(item.BossId))
            .Select(static item => item.BossId!)
            .Concat(stage.Objectives.Where(static item => !string.IsNullOrWhiteSpace(item.BossId))
                .Select(static item => item.BossId!))
            .Distinct(StringComparer.Ordinal);
        foreach (var bossId in bossIds)
        {
            foreach (var checkpoint in definitions.GetBoss(bossId).Phases
                         .Select(static phase => phase.CheckpointId)
                         .Where(static checkpoint => !string.IsNullOrWhiteSpace(checkpoint))
                         .Distinct(StringComparer.Ordinal))
                yield return new TrainingLocationOption(stage.Id, checkpoint);
        }
    }

    private bool ApplySettings(GameSettings requested)
    {
        var previous = _appliedSettings;
        if (!_input.TryApplySettings(requested.Input, out var normalizedInput))
        {
            _shell.ReplaceSettings(previous);
            return false;
        }

        var normalized = requested with { Input = normalizedInput };
        if (_displaySettingsApplicator is not null &&
            normalized.Display != previous.Display &&
            !_displaySettingsApplicator.TryApply(normalized.Display, _layout.Window.Width, _layout.Window.Height))
        {
            _input.TryApplySettings(previous.Input, out _);
            _shell.ReplaceSettings(previous);
            return false;
        }

        _appliedSettings = normalized;
        _shell.ReplaceSettings(normalized);
        _audio?.Apply(normalized.Audio);
        ApplyPresentationSettings(normalized.Gameplay);
        if (normalized.Audio.MusicVolume != previous.Audio.MusicVolume)
            _audio?.Preview("music", _simulation.RunState.Frame);
        else if (normalized.Audio.EffectsVolume != previous.Audio.EffectsVolume)
            _audio?.Preview("effect", _simulation.RunState.Frame);
        else if (normalized.Audio.VoiceVolume != previous.Audio.VoiceVolume)
            _audio?.Preview("voice", _simulation.RunState.Frame);
        if (!normalized.Gameplay.ControllerVibration)
        {
            StopVibration();
        }

        return true;
    }

    private void ApplyPresentationSettings(GameplaySettings settings) =>
        _presentation.Apply(new PresentationSettings
        {
            ParticleDensity = settings.ParticleDensity,
            FlashIntensity = settings.FlashIntensity,
            ShakeIntensity = 1,
            MaximumEffects = _presentation.Capacity
        });

    private void UpdateMusic()
    {
        var cueId = _frameSnapshot.Boss is { DefinitionId.Length: > 0 } boss &&
            _simulation.Definitions.Bosses.TryGetValue(boss.DefinitionId, out var definition)
            ? definition.BgmAudioId ?? _simulation.CurrentStage.BgmAudioId
            : _simulation.CurrentStage.BgmAudioId;
        _audio?.SetMusic(cueId, _simulation.RunState.Frame);
    }

    private void ToggleFullscreen()
    {
        var windowMode = _appliedSettings.Display.WindowMode == WindowMode.Windowed
            ? WindowMode.BorderlessFullscreen
            : WindowMode.Windowed;
        var requested = _appliedSettings with
        {
            Display = _appliedSettings.Display with { WindowMode = windowMode }
        };
        if (ApplySettings(requested))
        {
            _userDataStore?.SaveSettings(_appliedSettings);
        }
    }

    private void UpdateVibration(float deltaTime, SimulationFeedback feedback)
    {
        _vibrationRemaining = Math.Max(0, _vibrationRemaining - deltaTime);
        var pulse = FeedbackVibration.GetPulse(
            feedback,
            _appliedSettings.Gameplay.ControllerVibration);
        if (pulse.Duration > 0)
        {
            _vibrationRemaining = pulse.Duration;
            _leftMotorStrength = pulse.LeftMotor;
            _rightMotorStrength = pulse.RightMotor;
        }

        if (_vibrationRemaining > 0 && _input.IsGamePadConnected)
        {
            GamePad.SetVibration(PlayerIndex.One, _leftMotorStrength, _rightMotorStrength);
        }
        else
        {
            StopVibration();
        }
    }

    private void StopVibration()
    {
        _vibrationRemaining = 0;
        _leftMotorStrength = 0;
        _rightMotorStrength = 0;
        GamePad.SetVibration(PlayerIndex.One, 0, 0);
    }

    protected override void Draw(GameTime gameTime)
    {
        var logicalCanvas = _logicalCanvas ?? throw new InvalidOperationException("Content has not been loaded.");
        GraphicsDevice.SetRenderTarget(logicalCanvas);
        if (_shell.State is GameShellState.Title or GameShellState.ModeSelect or
            GameShellState.DifficultySelect or GameShellState.ShipSelect or GameShellState.Leaderboard or
            GameShellState.TrainingSetup or GameShellState.Information)
        {
            DrawShellMenu(clearBackground: true);
            PresentLogicalCanvas(logicalCanvas);
            base.Draw(gameTime);
            return;
        }

        GraphicsDevice.Clear(_simulation.IsPaused ? new Color(20, 20, 28) : _simulation.Status switch
        {
            SimulationStatus.GameOver => new Color(38, 8, 16),
            SimulationStatus.StageClear => new Color(8, 38, 24),
            _ => new Color(8, 13, 26)
        });
        var spriteBatch = _spriteBatch ?? throw new InvalidOperationException("Content has not been loaded.");
        var pixel = _pixel ?? throw new InvalidOperationException("Content has not been loaded.");

        var feedbackShake = _shakeRemaining > 0 ? 5f * (_shakeRemaining / 0.3f) : 0;
        var shakeMagnitude = Math.Max(feedbackShake, _presentation.CameraShake * 8) *
            _appliedSettings.Gameplay.ScreenShakeStrength;
        var shakeOffset = shakeMagnitude > 0
            ? new Vector2(
                (Random.Shared.NextSingle() * 2 - 1) * shakeMagnitude,
                (Random.Shared.NextSingle() * 2 - 1) * shakeMagnitude)
            : Vector2.Zero;

        var cameraTransform = Matrix.CreateTranslation(
            _layout.Playfield.X + shakeOffset.X,
            shakeOffset.Y,
            0);
        spriteBatch.Begin(
            samplerState: SamplerState.PointClamp,
            transformMatrix: cameraTransform);
        var presentationAlpha = _presentation.HitStopRemaining > 0 ? 0 : _simulationClock.InterpolationAlpha;
        var animationSeconds = (_simulation.RunState.Frame + presentationAlpha) /
            SimulationTiming.TicksPerSecond;
        DrawBackground(spriteBatch, animationSeconds);
        DrawEffects(spriteBatch, pixel, PresentationBlendMode.Alpha);
        spriteBatch.End();

        spriteBatch.Begin(
            blendState: BlendState.Additive,
            samplerState: SamplerState.PointClamp,
            transformMatrix: cameraTransform);
        DrawEffects(spriteBatch, pixel, PresentationBlendMode.Additive);
        spriteBatch.End();

        spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: cameraTransform);
        var items = _frameSnapshot.Items;
        foreach (var item in items
                     .OrderBy(item => GetRenderLayer(item, animationSeconds))
                     .ThenBy(static item => item.EntityId))
        {
            if (item.Kind is RenderKind.PlayerHitbox or RenderKind.GrazeRing &&
                ((_isReplayPlayback && !(_replayController?.ShowHitboxes ?? false)) ||
                 (_simulation.Configuration.IsPractice && !_simulation.Configuration.ShowHitboxes)))
                continue;
            var color = item.IsFlashing ? Color.White : item.Kind switch
            {
                RenderKind.Player => new Color(68, 210, 255),
                RenderKind.Enemy => new Color(255, 92, 92),
                RenderKind.PlayerBullet => new Color(255, 235, 84),
                RenderKind.EnemyBullet => GetEnemyBulletColor(),
                RenderKind.Explosion => new Color(255, 180, 50, (int)(255 * (1 - item.EffectProgress))),
                RenderKind.Option => new Color(120, 235, 255),
                RenderKind.Laser => new Color(100, 245, 255, 190),
                RenderKind.LockMarker => new Color(255, 80, 210, 180),
                RenderKind.PlayerHitbox => new Color(255, 255, 255, 210),
                RenderKind.GrazeRing => new Color(85, 225, 255, 120),
                RenderKind.Item => new Color(110, 255, 130),
                _ => Color.White
            };
            color = ApplyTint(color, item.Tint);
            var interpolated = PrimitiveRenderLayout.Interpolate(item, presentationAlpha);
            var bounds = PrimitiveRenderLayout.ToInterpolatedRectangle(item, presentationAlpha);
            if (item.Kind == RenderKind.EnemyBullet && _appliedSettings.Gameplay.BulletOutline)
                DrawCircleOutline(spriteBatch, pixel, interpolated, item.Radius + 2, new Color(5, 8, 18, 245));
            if (item.Kind is RenderKind.PlayerHitbox or RenderKind.GrazeRing)
            {
                DrawCircleOutline(spriteBatch, pixel, interpolated, item.Radius, color);
                continue;
            }
            if (item.Kind == RenderKind.LockMarker)
            {
                DrawLockMarker(spriteBatch, pixel, bounds, color);
                continue;
            }
            if (item.Kind == RenderKind.Item)
                DrawDiamondMarker(spriteBatch, pixel, interpolated, item.Radius + 4, Color.White);
            if (TryResolveVisual(item, animationSeconds, out var frame))
            {
                var width = frame.DisplayWidth ?? (item.Size.X > 0 ? item.Size.X : item.Radius * 2);
                var height = frame.DisplayHeight ?? (item.Size.Y > 0 ? item.Size.Y : item.Radius * 2);
                var effects = frame.Effects |
                    (item.FlipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None) |
                    (item.FlipY ? SpriteEffects.FlipVertically : SpriteEffects.None);
                spriteBatch.Draw(
                    frame.Texture,
                    new Vector2(interpolated.X, interpolated.Y),
                    frame.Source,
                    color,
                    item.Rotation,
                    frame.Origin,
                    new Vector2(width / frame.Source.Width, height / frame.Source.Height) * item.Scale,
                    effects,
                    0);
            }
            else if (item.Rotation == 0)
            {
                spriteBatch.Draw(pixel, bounds, color);
            }
            else
            {
                spriteBatch.Draw(
                    pixel,
                    new Vector2(interpolated.X, interpolated.Y),
                    null,
                    color,
                    item.Rotation,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(bounds.Width, bounds.Height),
                    SpriteEffects.None,
                    0);
            }

            if (item.Kind == RenderKind.Enemy && item.HealthFraction < 1)
            {
                var barWidth = Math.Max(1, (int)MathF.Round(bounds.Width * item.HealthFraction));
                spriteBatch.Draw(pixel, new Rectangle(bounds.X, bounds.Y - 5, barWidth, 3), Color.LimeGreen);
            }
        }

        spriteBatch.End();

        spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        if (_layout.LeftPanel is { } leftPanel)
        {
            DrawSidePanel(spriteBatch, pixel, leftPanel);
        }

        if (_layout.RightPanel is { } rightPanel)
        {
            DrawSidePanel(spriteBatch, pixel, rightPanel);
        }

        if (_presentation.ScreenFlash > 0)
            spriteBatch.Draw(pixel, _layout.Playfield, Color.White * Math.Min(0.5f, _presentation.ScreenFlash * 0.5f));

        DrawGameplayHud(spriteBatch, pixel);

        if (_simulation.Status == SimulationStatus.Running && _simulation.Phase != StagePhase.Playing)
        {
            DrawStagePresentation(spriteBatch, pixel);
        }

        spriteBatch.End();
        if (_shell.State is GameShellState.Pause or GameShellState.Options or GameShellState.Result)
        {
            DrawShellMenu(clearBackground: false);
        }

        PresentLogicalCanvas(logicalCanvas);
        if (_renderScreenshotPath is not null && !_renderScreenshotSaved && _simulation.RunState.Frame >= 360)
        {
            var directory = Path.GetDirectoryName(_renderScreenshotPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            using var stream = File.Create(_renderScreenshotPath);
            logicalCanvas.SaveAsPng(stream, logicalCanvas.Width, logicalCanvas.Height);
            _renderScreenshotSaved = true;
            Exit();
        }
        base.Draw(gameTime);
    }

    private void DrawBackground(SpriteBatch spriteBatch, double animationSeconds)
    {
        if (_visualAssets is null || string.IsNullOrWhiteSpace(_simulation.CurrentStage.BackgroundId) ||
            !_visualAssets.TryGetBackground(_simulation.CurrentStage.BackgroundId, out var background))
            return;
        var elapsedSeconds = _simulation.RunState.Frame / (double)SimulationTiming.TicksPerSecond;
        foreach (var layer in background.Layers.OrderBy(static item => item.Layer))
        {
            if (!_visualAssets.TryGetFrame(layer.AssetId, animationSeconds, out var frame)) continue;
            var scale = _layout.Playfield.Width / (float)frame.Source.Width;
            var tileWidth = Math.Max(1, (int)MathF.Ceiling(frame.Source.Width * scale));
            var tileHeight = Math.Max(1, (int)MathF.Ceiling(frame.Source.Height * scale));
            var offsetX = PositiveModulo((float)(elapsedSeconds * layer.ScrollX * layer.Parallax), tileWidth);
            var offsetY = PositiveModulo((float)(elapsedSeconds * layer.ScrollY * layer.Parallax), tileHeight);
            var tint = Color.White * (layer.Opacity * _appliedSettings.Gameplay.BackgroundBrightness);
            for (var x = (int)offsetX - tileWidth; x < _layout.Playfield.Width; x += tileWidth)
            {
                for (var y = (int)offsetY - tileHeight; y < _layout.Playfield.Height; y += tileHeight)
                {
                    spriteBatch.Draw(frame.Texture, new Rectangle(x, y, tileWidth, tileHeight), frame.Source, tint);
                }
            }
        }
    }

    private int GetRenderLayer(RenderItem item, double animationSeconds) =>
        TryResolveVisual(item, animationSeconds, out var frame) ? item.Layer + frame.Layer : item.Layer;

    private bool TryResolveVisual(RenderItem item, double animationSeconds, out VisualAssetFrame frame)
    {
        var visualId = item.AnimationId ?? item.VisualId;
        if (visualId is not null && _simulation.Definitions.Visuals.TryGetValue(visualId, out var visual))
            visualId = visual.AssetId;
        if (_visualAssets is not null && visualId is not null)
            return _visualAssets.TryGetFrame(visualId, animationSeconds, out frame);
        frame = default;
        return false;
    }

    private static Color ApplyTint(Color color, uint tint) => new(
        color.R * ((tint >> 24) & 0xff) / 255,
        color.G * ((tint >> 16) & 0xff) / 255,
        color.B * ((tint >> 8) & 0xff) / 255,
        color.A * (tint & 0xff) / 255);

    private Color GetEnemyBulletColor() => _appliedSettings.Gameplay.BulletPalette switch
    {
        "deuteranopia" => new Color(250, 205, 45),
        "high-contrast" => Color.White,
        _ => new Color(255, 140, 60)
    };

    private static float PositiveModulo(float value, int divisor)
    {
        var result = value % divisor;
        return result < 0 ? result + divisor : result;
    }

    private void DrawEffects(SpriteBatch spriteBatch, Texture2D pixel, PresentationBlendMode blend)
    {
        for (var index = 0; index < _presentation.ActiveCount; index++)
        {
            var effect = _presentation.GetEffect(index);
            if (effect.Blend != blend) continue;
            var radius = Math.Max(1, effect.Radius);
            var color = ApplyTint(Color.White, effect.Tint) * (1 - effect.Progress);
            if (effect.Kind == PresentationEffectKind.Trail)
            {
                spriteBatch.Draw(
                    pixel,
                    new Rectangle(
                        (int)MathF.Round(effect.Position.X - radius),
                        (int)MathF.Round(effect.Position.Y - (radius * 2)),
                        Math.Max(1, (int)MathF.Round(radius * 2)),
                        Math.Max(2, (int)MathF.Round(radius * 4))),
                    color);
            }
            else
            {
                DrawCircleOutline(spriteBatch, pixel, effect.Position, radius, color);
            }
        }
    }

    private void DrawGameplayHud(SpriteBatch spriteBatch, Texture2D pixel)
    {
        var hud = PrimitiveRenderLayout.CreatePresentationHudLayout(_layout);
        if (_layout.LeftPanel is null && _layout.RightPanel is null)
            spriteBatch.Draw(pixel, hud.Statistics, new Color(5, 12, 25, 205));
        var highScore = Math.Max(
            _frameSnapshot.Score,
            _profile.GetStats(_shell.SelectedCategory)?.BestScore ??
            _profile.HighScores.GetValueOrDefault(_gameId));
        var hudScale = _appliedSettings.Gameplay.HudScale < 1 ? 1 : 2;
        var lineSpacing = hudScale == 1 ? 11 : 16;
        var lines = new[]
        {
            ($"{_strings.Get("hud.high")} {highScore:D8}", new Color(190, 205, 225)),
            ($"{_strings.Get("hud.score")} {_frameSnapshot.Score:D8}", new Color(255, 235, 84)),
            ($"{_strings.Get("hud.chain")} {_frameSnapshot.Chain:D4}", Color.White),
            ($"{_strings.Get("hud.multiplier")} {_frameSnapshot.Multiplier:F2}", new Color(255, 190, 90)),
            ($"{_strings.Get("hud.power")} {_frameSnapshot.Power:D3}/{_frameSnapshot.MaximumPower:D3}", new Color(110, 255, 130)),
            ($"{_strings.Get("hud.gauge")} {_frameSnapshot.Gauge:D3}/{_frameSnapshot.MaximumGauge:D3}", new Color(180, 110, 255)),
            ($"{_strings.Get("hud.rank")} {_frameSnapshot.Rank:F3}", new Color(255, 135, 135)),
            ($"{_strings.Get("hud.stage")} {_frameSnapshot.StageNumber:D2}", new Color(160, 185, 210)),
            ($"{_strings.Get("hud.lives")} {_frameSnapshot.Lives:D2}", new Color(68, 210, 255)),
            ($"{_strings.Get("hud.bombs")} {_frameSnapshot.Bombs:D2}", new Color(255, 180, 50))
        };
        for (var index = 0; index < lines.Length; index++)
            DrawHudText(spriteBatch, pixel, lines[index].Item1, hud.Statistics.Left + 12,
                hud.Statistics.Top + 12 + (index * lineSpacing), lines[index].Item2, hudScale);
        var deviceText = _input.ActiveDevice == ActiveInputDevice.GamePad
            ? $"{_strings.Get("glyph.gamepad")} A/B"
            : $"{_strings.Get("glyph.keyboard")} {_appliedSettings.Input.Confirm}/{_appliedSettings.Input.Cancel}";
        DrawHudText(spriteBatch, pixel, deviceText, hud.Statistics.Left + 12,
            hud.Statistics.Top + 18 + (lines.Length * lineSpacing), new Color(130, 155, 185), 1);

        if (_frameSnapshot.Boss is not { } boss) return;
        spriteBatch.Draw(pixel, hud.Boss, new Color(4, 8, 18, 225));
        DrawHudText(spriteBatch, pixel, boss.Name, hud.Boss.Left + 8, hud.Boss.Top + 5, Color.White);
        var timerText = $"{boss.RemainingTime:F1}";
        foreach (var rectangle in PrimitiveRenderLayout.ToPixelTextRectangles(
                     timerText, hud.Boss.Right - 8, hud.Boss.Top + 5, 1))
            spriteBatch.Draw(pixel, rectangle, boss.Warning ? new Color(255, 90, 70) : Color.White);
        var bar = new Rectangle(hud.Boss.Left + 8, hud.Boss.Bottom - 11, hud.Boss.Width - 16, 5);
        spriteBatch.Draw(pixel, bar, new Color(45, 52, 70));
        spriteBatch.Draw(pixel, new Rectangle(bar.X, bar.Y,
            Math.Max(0, (int)MathF.Round(bar.Width * boss.HealthFraction)), bar.Height),
            boss.Warning ? new Color(255, 75, 55) : new Color(255, 105, 180));
        DrawCenteredPixelText(spriteBatch, pixel, boss.PhaseName, hud.Boss.Center.X, hud.Boss.Top + 18, 1,
            new Color(190, 205, 225));
        if (boss.Warning)
        {
            spriteBatch.Draw(pixel, new Rectangle(hud.Warning.Left, hud.Warning.Top, hud.Warning.Width, 3),
                new Color(255, 70, 45));
            spriteBatch.Draw(pixel, new Rectangle(hud.Warning.Left, hud.Warning.Bottom - 3, hud.Warning.Width, 3),
                new Color(255, 70, 45));
            DrawCenteredPixelText(spriteBatch, pixel, _strings.Get("hud.warning"), hud.Warning.Center.X,
                hud.Warning.Center.Y - 7, 2,
                Color.White);
        }
    }

    private static void DrawCircleOutline(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        Vector2 center,
        float radius,
        Color color)
    {
        const int segments = 24;
        for (var index = 0; index < segments; index++)
        {
            var angle = index * MathF.Tau / segments;
            spriteBatch.Draw(pixel, new Rectangle(
                (int)MathF.Round(center.X + (MathF.Cos(angle) * radius)) - 1,
                (int)MathF.Round(center.Y + (MathF.Sin(angle) * radius)) - 1,
                3,
                3), color);
        }
    }

    private static void DrawLockMarker(SpriteBatch spriteBatch, Texture2D pixel, Rectangle bounds, Color color)
    {
        const int thickness = 2;
        var arm = Math.Max(3, Math.Min(bounds.Width, bounds.Height) / 3);
        spriteBatch.Draw(pixel, new Rectangle(bounds.Left, bounds.Top, arm, thickness), color);
        spriteBatch.Draw(pixel, new Rectangle(bounds.Left, bounds.Top, thickness, arm), color);
        spriteBatch.Draw(pixel, new Rectangle(bounds.Right - arm, bounds.Top, arm, thickness), color);
        spriteBatch.Draw(pixel, new Rectangle(bounds.Right - thickness, bounds.Top, thickness, arm), color);
        spriteBatch.Draw(pixel, new Rectangle(bounds.Left, bounds.Bottom - thickness, arm, thickness), color);
        spriteBatch.Draw(pixel, new Rectangle(bounds.Left, bounds.Bottom - arm, thickness, arm), color);
        spriteBatch.Draw(pixel, new Rectangle(bounds.Right - arm, bounds.Bottom - thickness, arm, thickness), color);
        spriteBatch.Draw(pixel, new Rectangle(bounds.Right - thickness, bounds.Bottom - arm, thickness, arm), color);
    }

    private static void DrawDiamondMarker(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        Vector2 center,
        float radius,
        Color color)
    {
        var extent = Math.Max(2, (int)MathF.Round(radius));
        for (var offset = -extent; offset <= extent; offset++)
        {
            var inset = Math.Abs(offset);
            spriteBatch.Draw(pixel, new Rectangle(
                (int)MathF.Round(center.X) + offset,
                (int)MathF.Round(center.Y) - extent + inset,
                2,
                2), color);
            spriteBatch.Draw(pixel, new Rectangle(
                (int)MathF.Round(center.X) + offset,
                (int)MathF.Round(center.Y) + extent - inset,
                2,
                2), color);
        }
    }

    private void DrawShellMenu(bool clearBackground)
    {
        if (clearBackground)
        {
            GraphicsDevice.Clear(new Color(5, 9, 20));
        }

        var spriteBatch = _spriteBatch ?? throw new InvalidOperationException("Content has not been loaded.");
        var pixel = _pixel ?? throw new InvalidOperationException("Content has not been loaded.");
        var window = _layout.Window;
        var centerX = window.Center.X;
        var centerY = window.Center.Y;
        var isOptions = _shell.State == GameShellState.Options;
        var isTraining = _shell.State == GameShellState.TrainingSetup;
        var isDetailed = _shell.State is GameShellState.Result or GameShellState.Leaderboard or
            GameShellState.Information;
        var panelWidth = isOptions || isDetailed || isTraining ? Math.Min(window.Width - 48, 680) : 420;
        var panelHeight = isOptions || isDetailed || isTraining ? Math.Min(window.Height - 48, 560) : 300;
        var panelTop = centerY - (panelHeight / 2);
        var title = _shell.State switch
        {
            GameShellState.Title => "GOAT-SHOOOOTING",
            GameShellState.ModeSelect => "SELECT MODE",
            GameShellState.DifficultySelect => "SELECT DIFFICULTY",
            GameShellState.ShipSelect => "SELECT SHIP",
            GameShellState.Pause => "PAUSED",
            GameShellState.Result when _simulation.Status == SimulationStatus.StageClear => "ALL STAGES CLEAR",
            GameShellState.Result => "GAME OVER",
            GameShellState.Leaderboard => "LOCAL LEADERBOARD",
            GameShellState.TrainingSetup => "TRAINING SETUP",
            GameShellState.Information => _shell.InformationTitle,
            _ => "OPTIONS"
        };

        spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        spriteBatch.Draw(
            pixel,
            new Rectangle(centerX - (panelWidth / 2), panelTop, panelWidth, panelHeight),
            new Color(10, 20, 40, clearBackground ? 255 : 242));
        spriteBatch.Draw(
            pixel,
            new Rectangle(centerX - (panelWidth / 2), panelTop, panelWidth, 3),
            new Color(68, 210, 255));
        DrawCenteredPixelText(spriteBatch, pixel, title, centerX, panelTop + 28, isOptions ? 2 : 3, Color.White);

        if (_shell.State == GameShellState.Title)
        {
            var highScore = _profile.GetStats(_shell.SelectedCategory)?.BestScore ??
                _profile.HighScores.GetValueOrDefault(_gameId);
            DrawCenteredPixelText(
                spriteBatch,
                pixel,
                $"GAME  {_gameId.ToUpperInvariant()}  LEFT RIGHT CHANGE",
                centerX,
                panelTop + 76,
                1,
                new Color(68, 210, 255));
            DrawCenteredPixelText(
                spriteBatch,
                pixel,
                $"HIGH SCORE {highScore:D8}",
                centerX,
                panelTop + 94,
                1,
                new Color(255, 235, 84));
        }

        if (_shell.State == GameShellState.Result && _lastRun is { } result)
        {
            var resultLines = new[]
            {
                $"{result.Category.GameId}  {result.Category.RuleSetId}  {result.Category.DifficultyId}  {result.Category.ShipId}",
                $"STAGE {result.BestStage:D2}  SCORE {result.Score:D8}  {(result.Cleared ? "CLEAR" : "INCOMPLETE")}",
                $"MAX CHAIN {result.MaximumChain}  GRAZE {result.Grazes}",
                $"MISS {result.Misses}  BOMB {result.Bombs}  CONTINUE {result.Continues}",
                $"PLAY TIME {result.PlayTimeFrames / (double)SimulationTiming.TicksPerSecond:F1} SEC"
            };
            for (var index = 0; index < resultLines.Length; index++)
            {
                DrawCenteredPixelText(spriteBatch, pixel, resultLines[index], centerX, panelTop + 72 + (index * 18), 1, Color.White);
            }
        }

        if (_shell.State == GameShellState.Leaderboard)
        {
            var entries = _leaderboardService?.GetEntries(_shell.SelectedCategory) ?? [];
            for (var index = 0; index < entries.Count && index < 10; index++)
            {
                var entry = entries[index];
                DrawCenteredPixelText(
                    spriteBatch,
                    pixel,
                    $"{index + 1:D2}  {entry.Score:D8}  ST {entry.BestStage:D2}  " +
                    $"{(entry.Cleared ? "CLEAR" : "---")}  " +
                    $"{(string.IsNullOrWhiteSpace(entry.ReplayPath) ? string.Empty : "REPLAY")}",
                    centerX,
                    panelTop + 72 + (index * 22),
                    1,
                    index == _shell.SelectionIndex ? new Color(255, 235, 84) : Color.White);
            }
        }

        if (_shell.State == GameShellState.Information)
        {
            var lines = WrapPixelText(_shell.InformationBody, panelWidth - 64, 1);
            for (var index = 0; index < lines.Count; index++)
                DrawCenteredPixelText(spriteBatch, pixel, lines[index], centerX, panelTop + 86 + (index * 20), 1,
                    new Color(210, 222, 238));
            DrawCenteredPixelText(spriteBatch, pixel, "LEFT RIGHT PAGE", centerX, panelTop + panelHeight - 64, 1,
                new Color(68, 210, 255));
        }

        var items = _shell.State == GameShellState.Leaderboard
            ? new[] { _strings.Get("menu.back") }
            : _shell.MenuItems;
        var itemSpacing = isOptions || isTraining ? 23 : 46;
        var itemsTop = isOptions || isTraining
            ? panelTop + 78
            : _shell.State == GameShellState.Result
            ? panelTop + 210
            : _shell.State == GameShellState.Leaderboard
            ? panelTop + panelHeight - 80
            : _shell.State == GameShellState.Information
            ? panelTop + panelHeight - 42
            : _shell.State == GameShellState.Title
            ? centerY - 24
            : centerY - ((items.Count - 1) * itemSpacing / 2);
        const int maximumVisibleOptions = 19;
        var firstIndex = isOptions
            ? Math.Clamp(_shell.SelectionIndex - (maximumVisibleOptions / 2), 0,
                Math.Max(0, items.Count - maximumVisibleOptions))
            : 0;
        var lastIndex = isOptions ? Math.Min(items.Count, firstIndex + maximumVisibleOptions) : items.Count;
        for (var index = firstIndex; index < lastIndex; index++)
        {
            var value = isOptions
                ? index == _shell.SelectionIndex
                    ? _shell.SelectedValue
                    : OptionsMenu.GetValue(_shell.Settings, index)
                : isTraining && index == _shell.SelectionIndex
                ? _shell.SelectedValue
                : string.Empty;
            var text = string.IsNullOrEmpty(value) ? items[index] : $"{items[index]}  {value}";
            DrawMenuOption(
                spriteBatch,
                pixel,
                text,
                centerX,
                itemsTop + ((index - firstIndex) * itemSpacing),
                _shell.State == GameShellState.Leaderboard
                    ? _shell.SelectionIndex == _shell.MenuItems.Count - 1
                    : index == _shell.SelectionIndex,
                isOptions || isTraining ? 1 : 2,
                panelWidth - 48);
        }

        DrawCenteredPixelText(
            spriteBatch,
            pixel,
            _shell.IsAwaitingKeyBinding
                ? "PRESS A KEY  ESC OR B CANCEL"
                : _input.ActiveDevice == ActiveInputDevice.GamePad
                ? $"{_strings.Get("glyph.gamepad")}  D PAD SELECT  A CONFIRM  B BACK"
                : $"{_strings.Get("glyph.keyboard")}  {_appliedSettings.Input.Confirm} CONFIRM  " +
                    $"{_appliedSettings.Input.Cancel} BACK",
            centerX,
            panelTop + panelHeight - 30,
            1,
            new Color(160, 185, 210));
        spriteBatch.End();
    }

    private static IReadOnlyList<string> WrapPixelText(string text, int width, int scale)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            if (PrimitiveRenderLayout.MeasurePixelText(candidate, scale).X <= width)
            {
                current = candidate;
                continue;
            }
            if (current.Length > 0) lines.Add(current);
            current = word;
        }
        if (current.Length > 0) lines.Add(current);
        return lines;
    }

    private static void DrawMenuOption(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        string text,
        int centerX,
        int top,
        bool selected,
        int scale,
        int width)
    {
        if (selected)
        {
            spriteBatch.Draw(
                pixel,
                new Rectangle(centerX - (width / 2), top - 8, width, (7 * scale) + 14),
                new Color(25, 68, 100));
        }

        DrawCenteredPixelText(
            spriteBatch,
            pixel,
            text,
            centerX,
            top,
            scale,
            selected ? new Color(255, 235, 84) : new Color(160, 185, 210));
    }

    private void ApplyLayoutChanges()
    {
        var nextLayout = PrimitiveRenderLayout.CreateGameScreenLayout(_simulation.Definitions.Game);
        if (nextLayout == _layout)
        {
            return;
        }

        _layout = nextLayout;
        _logicalCanvas?.Dispose();
        _logicalCanvas = CreateLogicalCanvas();
        _displaySettingsApplicator?.TryApply(
            _appliedSettings.Display,
            _layout.Window.Width,
            _layout.Window.Height);
    }

    private RenderTarget2D CreateLogicalCanvas() => new(
        GraphicsDevice,
        _layout.Window.Width,
        _layout.Window.Height,
        mipMap: false,
        SurfaceFormat.Color,
        DepthFormat.None,
        preferredMultiSampleCount: 0,
        RenderTargetUsage.PreserveContents);

    private void PresentLogicalCanvas(RenderTarget2D logicalCanvas)
    {
        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(Color.Black);
        var spriteBatch = _spriteBatch ?? throw new InvalidOperationException("Content has not been loaded.");
        var destination = DisplaySettingsApplicator.CalculateLetterbox(
            logicalCanvas.Width,
            logicalCanvas.Height,
            GraphicsDevice.PresentationParameters.BackBufferWidth,
            GraphicsDevice.PresentationParameters.BackBufferHeight);
        spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        spriteBatch.Draw(logicalCanvas, destination, Color.White);
        spriteBatch.End();
    }

    private int GetScoreScale(string scoreText)
    {
        var scoreRegionWidth = _simulation.Definitions.Game.ScorePosition switch
        {
            "left-panel" => _layout.LeftPanel?.Width,
            "right-panel" => _layout.RightPanel?.Width,
            _ => _layout.Playfield.Width
        };
        var availableWidth = scoreRegionWidth ?? _layout.Playfield.Width;
        return PrimitiveRenderLayout.MeasurePixelText(scoreText).X > availableWidth - 32
            ? 1
            : 2;
    }

    private static void DrawSidePanel(SpriteBatch spriteBatch, Texture2D pixel, Rectangle panel)
    {
        spriteBatch.Draw(pixel, panel, new Color(12, 22, 42));
        spriteBatch.Draw(pixel, new Rectangle(panel.Left, panel.Top, 2, panel.Height), new Color(55, 80, 115));
        spriteBatch.Draw(pixel, new Rectangle(panel.Right - 2, panel.Top, 2, panel.Height), new Color(55, 80, 115));
    }

    private static void DrawHudText(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        string text,
        int left,
        int top,
        Color color,
        int scale = 2)
    {
        var right = left + PrimitiveRenderLayout.MeasurePixelText(text, scale).X;
        foreach (var rectangle in PrimitiveRenderLayout.ToPixelTextRectangles(text, right, top, scale))
        {
            spriteBatch.Draw(pixel, rectangle, color);
        }
    }

    private void DrawStagePresentation(SpriteBatch spriteBatch, Texture2D pixel)
    {
        var playfield = _layout.Playfield;
        var centerX = playfield.Center.X;
        var centerY = playfield.Center.Y;
        spriteBatch.Draw(pixel, playfield, new Color(3, 7, 16, 220));
        spriteBatch.Draw(pixel, new Rectangle(playfield.Left + 48, centerY, playfield.Width - 96, 2),
            new Color(68, 210, 255));
        spriteBatch.Draw(pixel, new Rectangle(centerX - 1, centerY - 118, 2, 236),
            new Color(68, 210, 255, 100));

        if (_simulation.Phase == StagePhase.Opening)
        {
            var openingTitle = string.IsNullOrWhiteSpace(_simulation.CurrentStage.Title)
                ? _simulation.CurrentStage.Id
                : _simulation.CurrentStage.Title;
            DrawCenteredPixelText(
                spriteBatch,
                pixel,
                $"STAGE {_simulation.StageNumber:D2}",
                centerX,
                centerY - 92,
                2,
                new Color(160, 185, 210));
            DrawCenteredPixelText(
                spriteBatch,
                pixel,
                openingTitle,
                centerX,
                centerY - 42,
                GetCenteredTextScale(openingTitle, playfield.Width - 64, 3),
                Color.White);
            if (!string.IsNullOrWhiteSpace(_simulation.CurrentStage.Subtitle))
            {
                DrawCenteredPixelText(
                    spriteBatch,
                    pixel,
                    _simulation.CurrentStage.Subtitle,
                    centerX,
                    centerY + 30,
                    1,
                    new Color(255, 235, 84));
            }

            return;
        }

        DrawCenteredPixelText(spriteBatch, pixel, "STAGE CLEAR", centerX, centerY - 80, 3, Color.White);
        DrawCenteredPixelText(
            spriteBatch,
            pixel,
            $"STAGE SCORE {_simulation.LastStageScore:D8}",
            centerX,
            centerY - 12,
            2,
            new Color(255, 235, 84));
        DrawCenteredPixelText(
            spriteBatch,
            pixel,
            $"TOTAL SCORE {_simulation.Telemetry.Score:D8}",
            centerX,
            centerY + 28,
            2,
            new Color(68, 210, 255));
        if (!string.IsNullOrWhiteSpace(_simulation.CurrentStage.NextStageId))
        {
            DrawCenteredPixelText(
                spriteBatch,
                pixel,
                "NEXT STAGE",
                centerX,
                centerY + 88,
                1,
                new Color(160, 185, 210));
        }
    }

    private static void DrawCenteredPixelText(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        string text,
        int centerX,
        int top,
        int scale,
        Color color)
    {
        var right = centerX + (PrimitiveRenderLayout.MeasurePixelText(text, scale).X / 2);
        foreach (var rectangle in PrimitiveRenderLayout.ToPixelTextRectangles(text, right, top, scale))
        {
            spriteBatch.Draw(pixel, rectangle, color);
        }
    }

    private static int GetCenteredTextScale(string text, int availableWidth, int preferredScale)
    {
        var displayText = string.IsNullOrWhiteSpace(text) ? "STAGE" : text;
        for (var scale = preferredScale; scale > 1; scale--)
        {
            if (PrimitiveRenderLayout.MeasurePixelText(displayText, scale).X <= availableWidth)
            {
                return scale;
            }
        }

        return 1;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopVibration();
            _pixel?.Dispose();
            _spriteBatch?.Dispose();
            _logicalCanvas?.Dispose();
            _audio?.Dispose();
            foreach (var catalog in _visualAssetCatalogs.Values.Distinct()) catalog.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class MonoGameDisplaySettingsTarget : IDisplaySettingsTarget
{
    private readonly GraphicsDeviceManager _graphics;

    public MonoGameDisplaySettingsTarget(GraphicsDeviceManager graphics)
    {
        _graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
    }

    public int DesktopWidth => GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Width;
    public int DesktopHeight => GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Height;
    public int BackBufferWidth
    {
        get => _graphics.PreferredBackBufferWidth;
        set => _graphics.PreferredBackBufferWidth = value;
    }

    public int BackBufferHeight
    {
        get => _graphics.PreferredBackBufferHeight;
        set => _graphics.PreferredBackBufferHeight = value;
    }

    public bool IsFullScreen
    {
        get => _graphics.IsFullScreen;
        set => _graphics.IsFullScreen = value;
    }

    public bool HardwareModeSwitch
    {
        get => _graphics.HardwareModeSwitch;
        set => _graphics.HardwareModeSwitch = value;
    }

    public bool VSync
    {
        get => _graphics.SynchronizeWithVerticalRetrace;
        set => _graphics.SynchronizeWithVerticalRetrace = value;
    }

    public void ApplyChanges() => _graphics.ApplyChanges();
}
