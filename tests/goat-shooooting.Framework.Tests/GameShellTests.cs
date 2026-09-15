using GoatShooooting.Framework;
using GoatShooooting.Platform;
using Xunit;

namespace GoatShooooting.Framework.Tests;

public sealed class GameShellTests
{
    [Fact]
    public void PauseMenuOffersResumeOptionsRetryAndReturnToTitle()
    {
        var resumeShell = StartRun(new GameShell(new GameSettings()));
        resumeShell.Pause();
        Assert.Equal(["RESUME", "OPTIONS", "RETRY", "TITLE"], resumeShell.MenuItems);
        Assert.Equal(GameShellCommand.ResumeRun, resumeShell.Update(new MenuInput(confirm: true)));

        var optionsShell = StartRun(new GameShell(new GameSettings()));
        optionsShell.Pause();
        optionsShell.Update(new MenuInput(down: true));
        Assert.Equal(GameShellCommand.None, optionsShell.Update(new MenuInput(confirm: true)));
        Assert.Equal(GameShellState.Options, optionsShell.State);

        var retryShell = StartRun(new GameShell(new GameSettings()));
        retryShell.Pause();
        retryShell.Update(new MenuInput(down: true));
        retryShell.Update(new MenuInput(down: true));
        Assert.Equal(GameShellCommand.RetryRun, retryShell.Update(new MenuInput(confirm: true)));

        var titleShell = StartRun(new GameShell(new GameSettings()));
        titleShell.Pause();
        titleShell.Update(new MenuInput(down: true));
        titleShell.Update(new MenuInput(down: true));
        titleShell.Update(new MenuInput(down: true));
        Assert.Equal(GameShellCommand.ReturnToTitle, titleShell.Update(new MenuInput(confirm: true)));
        Assert.Equal(GameShellState.Title, titleShell.State);
    }

    [Fact]
    public void TitlePauseOptionsResultAndTitleTransitionsAreAvailable()
    {
        var shell = new GameShell(new GameSettings());

        Assert.Equal(GameShellCommand.None, shell.Update(new MenuInput(confirm: true)));
        Assert.Equal(GameShellState.ModeSelect, shell.State);
        Assert.Equal(GameShellCommand.RunSelectionChanged, shell.Update(new MenuInput(confirm: true)));
        Assert.Equal(GameShellState.DifficultySelect, shell.State);
        Assert.Equal(GameShellCommand.RunSelectionChanged, shell.Update(new MenuInput(confirm: true)));
        Assert.Equal(GameShellState.ShipSelect, shell.State);
        Assert.Equal(GameShellCommand.StartRun, shell.Update(new MenuInput(confirm: true)));
        Assert.Equal(GameShellState.Playing, shell.State);

        shell.Pause();
        Assert.Equal(GameShellState.Pause, shell.State);
        shell.Update(new MenuInput(down: true));
        Assert.Equal(GameShellCommand.None, shell.Update(new MenuInput(confirm: true)));
        Assert.Equal(GameShellState.Options, shell.State);

        Assert.Equal(GameShellCommand.SaveSettings, shell.Update(new MenuInput(cancel: true)));
        Assert.Equal(GameShellState.Pause, shell.State);
        Assert.Equal(GameShellCommand.ResumeRun, shell.Update(new MenuInput(cancel: true)));
        Assert.Equal(GameShellState.Playing, shell.State);

        shell.ShowResult();
        Assert.Equal(GameShellCommand.RetryRun, shell.Update(new MenuInput(confirm: true)));
        Assert.Equal(GameShellState.Playing, shell.State);
        shell.ShowResult();
        shell.Update(new MenuInput(down: true));
        shell.Update(new MenuInput(down: true));
        Assert.Equal(GameShellCommand.ReturnToTitle, shell.Update(new MenuInput(confirm: true)));
        Assert.Equal(GameShellState.Title, shell.State);
    }

    [Fact]
    public void OptionsChangesSettingsAndWrapsSelection()
    {
        var shell = new GameShell(new GameSettings());
        shell.Update(new MenuInput(down: true));
        shell.Update(new MenuInput(down: true));
        shell.Update(new MenuInput(down: true));
        shell.Update(new MenuInput(confirm: true));
        Assert.Equal(GameShellState.Options, shell.State);

        Assert.Equal(GameShellCommand.SettingsChanged, shell.Update(new MenuInput(right: true)));
        Assert.Equal(WindowMode.BorderlessFullscreen, shell.Settings.Display.WindowMode);

        shell.Update(new MenuInput(down: true));
        Assert.Equal(GameShellCommand.SettingsChanged, shell.Update(new MenuInput(right: true)));
        Assert.Equal(2, shell.Settings.Display.WindowScale);

        shell.Update(new MenuInput(up: true));
        Assert.Equal(0, shell.SelectionIndex);
    }

    [Fact]
    public void OptionsExposeAudioLocaleAndAccessibilitySettings()
    {
        var shell = new GameShell(new GameSettings());
        for (var index = 0; index < 3; index++) shell.Update(new MenuInput(down: true));
        shell.Update(new MenuInput(confirm: true));

        for (var index = 0; index < 3; index++) shell.Update(new MenuInput(down: true));
        shell.Update(new MenuInput(right: true));
        Assert.Equal("ja", shell.Settings.Locale);
        shell.Update(new MenuInput(down: true));
        shell.Update(new MenuInput(down: true));
        shell.Update(new MenuInput(left: true));
        Assert.Equal(0.7f, shell.Settings.Audio.MusicVolume);
        for (var index = 0; index < 7; index++) shell.Update(new MenuInput(down: true));
        shell.Update(new MenuInput(left: true));
        Assert.Equal(0.9f, shell.Settings.Gameplay.ParticleDensity);
        shell.Update(new MenuInput(down: true));
        shell.Update(new MenuInput(left: true));
        Assert.Equal(0.9f, shell.Settings.Gameplay.BackgroundBrightness);
        shell.Update(new MenuInput(down: true));
        shell.Update(new MenuInput(right: true));
        Assert.False(shell.Settings.Gameplay.BulletOutline);
    }

    [Fact]
    public void AccessibilityPresetCanBeAppliedWithOneMenuAction()
    {
        var shell = OpenOptionsAtInputIndex(9);

        Assert.Equal("APPLY", shell.SelectedValue);
        Assert.Equal(GameShellCommand.SettingsChanged, shell.Update(new MenuInput(confirm: true)));

        Assert.Equal("ACTIVE", shell.SelectedValue);
        Assert.Equal(0, shell.Settings.Gameplay.ScreenShakeStrength);
        Assert.Equal("high-contrast", shell.Settings.Gameplay.BulletPalette);
        Assert.True(shell.Settings.Gameplay.BulletOutline);
    }

    [Fact]
    public void TitleCyclesAvailableGamesFromTheStartItem()
    {
        var shell = new GameShell(new GameSettings(), ["sample", "gauntlet"], "sample");

        var command = shell.Update(new MenuInput(right: true));

        Assert.Equal(GameShellCommand.GameSelectionChanged, command);
        Assert.Equal("gauntlet", shell.SelectedGameId);
        Assert.Equal(GameShellCommand.GameSelectionChanged, shell.Update(new MenuInput(right: true)));
        Assert.Equal("sample", shell.SelectedGameId);
    }

    [Fact]
    public void RunSelectionsRestoreProfileValuesRejectLockedChoicesAndSupportBackNavigation()
    {
        var options = new GameRunOptions(
            "sample",
            [new("normal", "normal"), new("sprint", "sprint", UnlockId: "mode:sprint")],
            [new("novice", "novice"), new("expert", "expert", IsAvailable: false)],
            [new("swift", "swift"), new("heavy", "heavy")]);
        var profile = new PlayerProfile
        {
            LastRuleSetIds = new() { ["sample"] = "normal" },
            LastDifficultyIds = new() { ["sample"] = "novice" },
            LastShipIds = new() { ["sample"] = "heavy" }
        };
        var shell = new GameShell(new GameSettings(), [options], "sample", profile);

        shell.Update(new MenuInput(confirm: true));
        shell.Update(new MenuInput(down: true));
        Assert.Contains("LOCKED", shell.MenuItems[shell.SelectionIndex], StringComparison.Ordinal);
        Assert.Equal(GameShellCommand.None, shell.Update(new MenuInput(confirm: true)));
        Assert.Equal(GameShellState.ModeSelect, shell.State);
        shell.Update(new MenuInput(up: true));
        shell.Update(new MenuInput(confirm: true));
        Assert.Equal(GameShellState.DifficultySelect, shell.State);
        Assert.Equal(GameShellCommand.None, shell.Update(new MenuInput(cancel: true)));
        Assert.Equal(GameShellState.ModeSelect, shell.State);
        shell.Update(new MenuInput(confirm: true));
        shell.Update(new MenuInput(confirm: true));

        Assert.Equal(GameShellState.ShipSelect, shell.State);
        Assert.Equal(1, shell.SelectionIndex);
        Assert.Equal("heavy", shell.SelectedShipId);
        Assert.Equal(GameShellCommand.StartRun, shell.Update(new MenuInput(confirm: true)));
        Assert.Equal(new ScoreCategoryKey("sample", "normal", "novice", "heavy"), shell.SelectedCategory);
        var configuration = RunSelectionConfiguration.Create(shell, 42);
        Assert.Equal("normal", configuration.RuleSetId);
        Assert.Equal("novice", configuration.DifficultyId);
        Assert.Equal("heavy", configuration.ShipId);
        Assert.Equal(42, configuration.Seed);
    }

    [Fact]
    public void UnlockAllowsPreviouslyLockedModeAndLeaderboardReturnsToItsCaller()
    {
        var options = new GameRunOptions(
            "sample",
            [new("normal", "normal"), new("sprint", "sprint", UnlockId: "mode:sprint")],
            [new("arcade", "arcade")],
            [new("ship", "ship")]);
        var profile = new PlayerProfile
        {
            Unlocks = new(StringComparer.Ordinal) { "mode:sprint" },
            LastRuleSetIds = new() { ["sample"] = "sprint" }
        };
        var shell = new GameShell(new GameSettings(), [options], "sample", profile);

        shell.Update(new MenuInput(confirm: true));
        Assert.Equal(1, shell.SelectionIndex);
        Assert.DoesNotContain("LOCKED", shell.MenuItems[1], StringComparison.Ordinal);
        shell.Update(new MenuInput(confirm: true));
        shell.Update(new MenuInput(confirm: true));
        shell.Update(new MenuInput(confirm: true));
        shell.ShowResult();
        shell.Update(new MenuInput(down: true));
        Assert.Equal(GameShellCommand.OpenLeaderboard, shell.Update(new MenuInput(confirm: true)));
        Assert.Equal(GameShellState.Leaderboard, shell.State);
        Assert.Equal(GameShellCommand.None, shell.Update(new MenuInput(cancel: true)));
        Assert.Equal(GameShellState.Result, shell.State);
    }

    [Fact]
    public void TrainingSetupProducesPracticeConfigurationWithSelectedOverrides()
    {
        var options = new GameRunOptions(
            "sample",
            [new("arcade", "arcade")],
            [new("normal", "normal")],
            [new("ship", "ship")],
            TrainingLocations:
            [new TrainingLocationOption("stage-1"), new TrainingLocationOption("stage-1", "boss-phase-2")]);
        var shell = new GameShell(new GameSettings(), [options], "sample", new PlayerProfile());

        shell.Update(new MenuInput(down: true));
        shell.Update(new MenuInput(confirm: true));
        Assert.Equal(GameShellState.TrainingSetup, shell.State);
        shell.Update(new MenuInput(right: true));
        shell.Update(new MenuInput(down: true));
        shell.Update(new MenuInput(right: true));
        for (var index = 0; index < 8; index++) shell.Update(new MenuInput(down: true));

        Assert.Equal(GameShellCommand.StartTraining, shell.Update(new MenuInput(confirm: true)));
        var configuration = RunSelectionConfiguration.CreateTraining(shell, 99);
        Assert.True(configuration.IsPractice);
        Assert.Equal("stage-1", configuration.StartStageId);
        Assert.Equal("boss-phase-2", configuration.CheckpointId);
        Assert.Equal(10, configuration.InitialPower);
        Assert.Equal(99, configuration.Seed);
    }

    [Fact]
    public void LeaderboardAndResultReplayEntriesProducePlaybackCommand()
    {
        var shell = new GameShell(new GameSettings());
        shell.SetLeaderboardEntries(
        [
            new CompletedRunRecord { RunId = "run", Score = 123, ReplayPath = "run.replay.json" }
        ]);
        shell.Update(new MenuInput(down: true));
        shell.Update(new MenuInput(down: true));
        Assert.Equal(GameShellCommand.OpenLeaderboard, shell.Update(new MenuInput(confirm: true)));
        Assert.Equal(GameShellCommand.PlayReplay, shell.Update(new MenuInput(confirm: true)));
        Assert.Equal("run.replay.json", shell.SelectedReplayPath);

        var resultShell = StartRun(new GameShell(new GameSettings()));
        resultShell.ShowResult();
        resultShell.SetResultReplay("result.replay.json");
        resultShell.Update(new MenuInput(down: true));
        Assert.Equal(GameShellCommand.PlayReplay, resultShell.Update(new MenuInput(confirm: true)));
        Assert.Equal("result.replay.json", resultShell.SelectedReplayPath);
    }

    [Fact]
    public void ConfirmOnInputOptionCapturesTheNextKeyboardKey()
    {
        var shell = OpenOptionsAtInputIndex(22);

        Assert.Equal(GameShellCommand.None, shell.Update(new MenuInput(confirm: true, newlyPressedKey: "Enter")));
        Assert.True(shell.IsAwaitingKeyBinding);
        Assert.Equal("PRESS A KEY", shell.SelectedValue);

        Assert.Equal(
            GameShellCommand.SettingsChanged,
            shell.Update(new MenuInput(newlyPressedKey: "F")));
        Assert.False(shell.IsAwaitingKeyBinding);
        Assert.Equal("F", shell.Settings.Input.Fire);
    }

    [Fact]
    public void CancelLeavesAKeyBindingUnchanged()
    {
        var shell = OpenOptionsAtInputIndex(22);
        shell.Update(new MenuInput(confirm: true));

        Assert.Equal(GameShellCommand.None, shell.Update(new MenuInput(cancel: true, newlyPressedKey: "Escape")));

        Assert.False(shell.IsAwaitingKeyBinding);
        Assert.Equal("Z", shell.Settings.Input.Fire);
        Assert.Equal(GameShellState.Options, shell.State);
    }

    [Fact]
    public void ConflictingConfirmAndCancelBindingKeepsWaiting()
    {
        var shell = OpenOptionsAtInputIndex(27);
        shell.Update(new MenuInput(confirm: true));

        Assert.Equal(GameShellCommand.None, shell.Update(new MenuInput(newlyPressedKey: "Escape")));

        Assert.True(shell.IsAwaitingKeyBinding);
        Assert.Equal("KEY IN USE", shell.SelectedValue);
        Assert.Equal("Enter", shell.Settings.Input.Confirm);
    }

    private static GameShell OpenOptionsAtInputIndex(int inputIndex)
    {
        var shell = new GameShell(new GameSettings());
        shell.Update(new MenuInput(down: true));
        shell.Update(new MenuInput(down: true));
        shell.Update(new MenuInput(down: true));
        shell.Update(new MenuInput(confirm: true));
        for (var index = 0; index < inputIndex; index++)
        {
            shell.Update(new MenuInput(down: true));
        }

        return shell;
    }

    private static GameShell StartRun(GameShell shell)
    {
        shell.Update(new MenuInput(confirm: true));
        shell.Update(new MenuInput(confirm: true));
        shell.Update(new MenuInput(confirm: true));
        shell.Update(new MenuInput(confirm: true));
        return shell;
    }

    private sealed class MenuInput : IMenuInput
    {
        public MenuInput(
            bool up = false,
            bool down = false,
            bool left = false,
            bool right = false,
            bool confirm = false,
            bool cancel = false,
            string? newlyPressedKey = null)
        {
            UpPressed = up;
            DownPressed = down;
            LeftPressed = left;
            RightPressed = right;
            ConfirmPressed = confirm;
            CancelPressed = cancel;
            NewlyPressedKey = newlyPressedKey;
        }

        public bool UpPressed { get; }
        public bool DownPressed { get; }
        public bool LeftPressed { get; }
        public bool RightPressed { get; }
        public bool ConfirmPressed { get; }
        public bool CancelPressed { get; }
        public string? NewlyPressedKey { get; }
    }
}
