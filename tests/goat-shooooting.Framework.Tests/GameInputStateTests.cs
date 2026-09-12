using GoatShooooting.Framework;
using GoatShooooting.Platform;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace GoatShooooting.Framework.Tests;

public sealed class GameInputStateTests
{
    [Fact]
    public void GamePadMapsDocumentedGameplayAndMenuControls()
    {
        var input = new GameInputState();
        var state = CreateGamePadState(Buttons.DPadLeft, Buttons.DPadUp, Buttons.A, Buttons.Y, Buttons.Start);

        input.Apply([], state, isGamePadConnected: true);

        Assert.Equal(-1, input.MoveX);
        Assert.Equal(-1, input.MoveY);
        Assert.True(input.Fire);
        Assert.True(input.Bomb);
        Assert.True(input.Retry);
        Assert.True(input.Pause);
        Assert.True(input.UpPressed);
        Assert.True(input.LeftPressed);
        Assert.True(input.ConfirmPressed);
        Assert.Equal(ActiveInputDevice.GamePad, input.ActiveDevice);
    }

    [Fact]
    public void StickUsesCircularDeadZoneAndScreenCoordinates()
    {
        var input = new GameInputState();

        input.Apply([], CreateGamePadState(leftStick: new Vector2(0.1f, 0.1f)), isGamePadConnected: true);
        Assert.Equal(0, input.MoveX);
        Assert.Equal(0, input.MoveY);

        input.Apply([], CreateGamePadState(leftStick: new Vector2(0.6f, 0.8f)), isGamePadConnected: true);
        Assert.True(input.MoveX > 0);
        Assert.True(input.MoveY < 0);
    }

    [Fact]
    public void KeyboardAndGamePadCanBeCombinedInOneFrame()
    {
        var input = new GameInputState();

        input.Apply(
            new[] { Keys.W, Keys.Z },
            CreateGamePadState(Buttons.DPadRight, Buttons.B),
            isGamePadConnected: true);

        Assert.Equal(1, input.MoveX);
        Assert.Equal(-1, input.MoveY);
        Assert.True(input.Fire);
        Assert.True(input.Bomb);
        Assert.Equal(ActiveInputDevice.GamePad, input.ActiveDevice);
    }

    [Fact]
    public void MenuButtonsAndDirectionsAreEdgeDetected()
    {
        var input = new GameInputState();
        var state = CreateGamePadState(Buttons.DPadDown, Buttons.A);

        input.Apply([], state, isGamePadConnected: true);
        Assert.True(input.DownPressed);
        Assert.True(input.ConfirmPressed);

        input.Apply([], state, isGamePadConnected: true);
        Assert.False(input.DownPressed);
        Assert.False(input.ConfirmPressed);

        input.Apply([], CreateGamePadState(), isGamePadConnected: true);
        input.Apply([], state, isGamePadConnected: true);
        Assert.True(input.DownPressed);
        Assert.True(input.ConfirmPressed);
    }

    [Fact]
    public void LastDeviceChangesOnlyWhenThatDeviceHasInput()
    {
        var input = new GameInputState();

        input.Apply([], CreateGamePadState(Buttons.X), isGamePadConnected: true);
        Assert.Equal(ActiveInputDevice.GamePad, input.ActiveDevice);

        input.Apply([], CreateGamePadState(), isGamePadConnected: true);
        Assert.Equal(ActiveInputDevice.GamePad, input.ActiveDevice);

        input.Apply(new[] { Keys.Z }, CreateGamePadState(), isGamePadConnected: true);
        Assert.Equal(ActiveInputDevice.Keyboard, input.ActiveDevice);
    }

    [Fact]
    public void DisconnectIsReportedOnceAndKeyboardRemainsAvailable()
    {
        var input = new GameInputState();
        input.Apply([], CreateGamePadState(), isGamePadConnected: true);

        input.Apply([], default, isGamePadConnected: false);
        Assert.True(input.GamePadDisconnectedThisFrame);
        Assert.False(input.IsGamePadConnected);

        input.Apply(new[] { Keys.Space }, default, isGamePadConnected: false);
        Assert.False(input.GamePadDisconnectedThisFrame);
        Assert.True(input.KeyboardInputDetected);
        Assert.True(input.Fire);
        Assert.Equal(ActiveInputDevice.Keyboard, input.ActiveDevice);
    }

    [Fact]
    public void InputSettingsNormalizeUnknownKeysAndApplyRecognizedKeys()
    {
        var input = new GameInputState();
        var accepted = input.TryApplySettings(
            new InputSettings { Fire = "F", Bomb = "NotARealKey" },
            out var normalized);

        input.Apply(new[] { Keys.F, Keys.X }, default, isGamePadConnected: false);

        Assert.True(accepted);
        Assert.Equal("F", normalized.Fire);
        Assert.Equal("X", normalized.Bomb);
        Assert.True(input.Fire);
        Assert.True(input.Bomb);
    }

    [Fact]
    public void InputSettingsRejectMatchingConfirmAndCancelBindings()
    {
        var input = new GameInputState();

        var accepted = input.TryApplySettings(
            new InputSettings { Confirm = "Enter", Cancel = "Enter" },
            out var retained);

        Assert.False(accepted);
        Assert.Equal(new InputSettings(), retained);
        input.Apply(new[] { Keys.Enter }, default, isGamePadConnected: false);
        Assert.True(input.ConfirmPressed);
        Assert.False(input.CancelPressed);
    }

    [Fact]
    public void NewlyPressedKeyReportsEachPhysicalPressOnce()
    {
        var input = new GameInputState();

        input.Apply(new[] { Keys.F }, default, isGamePadConnected: false);
        Assert.Equal("F", input.NewlyPressedKey);

        input.Apply(new[] { Keys.F }, default, isGamePadConnected: false);
        Assert.Null(input.NewlyPressedKey);

        input.Apply([], default, isGamePadConnected: false);
        input.Apply(new[] { Keys.F }, default, isGamePadConnected: false);
        Assert.Equal("F", input.NewlyPressedKey);
    }

    [Fact]
    public void GamePadCanStartAndQuitStartupMenuWithoutMouse()
    {
        var input = new GameInputState();
        var menu = new StartupMenu();

        input.Apply([], CreateGamePadState(Buttons.A), isGamePadConnected: true);
        Assert.Equal(
            StartupMenuAction.StartGame,
            menu.Update(input.UpPressed, input.DownPressed, input.ConfirmPressed));

        input = new GameInputState();
        menu = new StartupMenu();
        input.Apply([], CreateGamePadState(Buttons.DPadDown), isGamePadConnected: true);
        Assert.Equal(StartupMenuAction.None, menu.Update(input.UpPressed, input.DownPressed, input.ConfirmPressed));
        input.Apply([], CreateGamePadState(Buttons.DPadDown, Buttons.A), isGamePadConnected: true);
        Assert.Equal(StartupMenuAction.Quit, menu.Update(input.UpPressed, input.DownPressed, input.ConfirmPressed));
    }

    private static GamePadState CreateGamePadState(params Buttons[] buttons) =>
        CreateGamePadState(Vector2.Zero, buttons);

    private static GamePadState CreateGamePadState(Vector2 leftStick, params Buttons[] buttons) =>
        new(leftStick, Vector2.Zero, 0, 0, buttons);
}
