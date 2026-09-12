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

    private sealed class MenuInput : IMenuInput
    {
        public MenuInput(
            bool up = false,
            bool down = false,
            bool left = false,
            bool right = false,
            bool confirm = false,
            bool cancel = false)
        {
            UpPressed = up;
            DownPressed = down;
            LeftPressed = left;
            RightPressed = right;
            ConfirmPressed = confirm;
            CancelPressed = cancel;
        }

        public bool UpPressed { get; }
        public bool DownPressed { get; }
        public bool LeftPressed { get; }
        public bool RightPressed { get; }
        public bool ConfirmPressed { get; }
        public bool CancelPressed { get; }
    }
}
