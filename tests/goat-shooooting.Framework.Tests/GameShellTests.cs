using GoatShooooting.Framework;
using GoatShooooting.Platform;
using Xunit;

namespace GoatShooooting.Framework.Tests;

public sealed class GameShellTests
{
    [Fact]
    public void TitlePauseOptionsResultAndTitleTransitionsAreAvailable()
    {
        var shell = new GameShell(new GameSettings());

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
        Assert.Equal(GameShellCommand.ReturnToTitle, shell.Update(new MenuInput(confirm: true)));
        Assert.Equal(GameShellState.Title, shell.State);
    }

    [Fact]
    public void OptionsChangesSettingsAndWrapsSelection()
    {
        var shell = new GameShell(new GameSettings());
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
    public void ConfirmOnInputOptionCapturesTheNextKeyboardKey()
    {
        var shell = OpenOptionsAtInputIndex(12);

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
        var shell = OpenOptionsAtInputIndex(12);
        shell.Update(new MenuInput(confirm: true));

        Assert.Equal(GameShellCommand.None, shell.Update(new MenuInput(cancel: true, newlyPressedKey: "Escape")));

        Assert.False(shell.IsAwaitingKeyBinding);
        Assert.Equal("Z", shell.Settings.Input.Fire);
        Assert.Equal(GameShellState.Options, shell.State);
    }

    [Fact]
    public void ConflictingConfirmAndCancelBindingKeepsWaiting()
    {
        var shell = OpenOptionsAtInputIndex(17);
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
        shell.Update(new MenuInput(confirm: true));
        for (var index = 0; index < inputIndex; index++)
        {
            shell.Update(new MenuInput(down: true));
        }

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
