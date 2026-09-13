using System.Numerics;
using Microsoft.Xna.Framework.Input;
using GoatShooooting.Definitions;
using GoatShooooting.Framework;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Framework.Tests;

public sealed class FrameworkAdapterTests
{
    [Fact]
    public void KeyboardAdapterMapsDocumentedControls()
    {
        var input = new KeyboardInputState();

        input.Apply(new[] { Keys.A, Keys.Up, Keys.Space, Keys.X, Keys.R, Keys.P, Keys.Escape });

        Assert.Equal(-1, input.MoveX);
        Assert.Equal(-1, input.MoveY);
        Assert.True(input.Fire);
        Assert.True(input.Bomb);
        Assert.True(input.Retry);
        Assert.True(input.Pause);
        Assert.True(input.QuitRequested);
        Assert.True(input.MenuUpPressed);
        Assert.True(input.MenuConfirmPressed);
    }

    [Fact]
    public void KeyboardAdapterReportsMenuInputOnlyOnNewPress()
    {
        var input = new KeyboardInputState();

        input.Apply(new[] { Keys.Down, Keys.Enter });
        Assert.True(input.MenuDownPressed);
        Assert.True(input.MenuConfirmPressed);

        input.Apply(new[] { Keys.Down, Keys.Enter });
        Assert.False(input.MenuDownPressed);
        Assert.False(input.MenuConfirmPressed);

        input.Apply([]);
        input.Apply(new[] { Keys.Down, Keys.Enter });
        Assert.True(input.MenuDownPressed);
        Assert.True(input.MenuConfirmPressed);
    }

    [Fact]
    public void StartupMenuSelectsQuitAndConfirmsIt()
    {
        var menu = new StartupMenu();

        Assert.Equal(StartupMenuAction.None, menu.Update(false, true, false));
        Assert.Equal(StartupMenuSelection.Quit, menu.Selection);
        Assert.Equal(StartupMenuAction.Quit, menu.Update(false, false, true));
        Assert.True(menu.IsOpen);
    }

    [Fact]
    public void StartupMenuStartsGameWithDefaultSelection()
    {
        var menu = new StartupMenu();

        Assert.Equal(StartupMenuAction.StartGame, menu.Update(false, false, true));
        Assert.False(menu.IsOpen);
        Assert.Equal(StartupMenuAction.None, menu.Update(false, true, true));
    }

    [Fact]
    public void RenderLayoutCentersPrimitiveOnSimulationPosition()
    {
        var item = new RenderItem(1, RenderKind.Enemy, new Vector2(50, 30), 10, 1);

        var rectangle = PrimitiveRenderLayout.ToRectangle(item);

        Assert.Equal(40, rectangle.X);
        Assert.Equal(20, rectangle.Y);
        Assert.Equal(20, rectangle.Width);
        Assert.Equal(20, rectangle.Height);
    }

    [Fact]
    public void RenderInterpolationUsesPriorAndCurrentTickWithoutChangingEither()
    {
        var item = new RenderItem(
            1,
            RenderKind.Item,
            new Vector2(20, 40),
            4,
            1,
            PreviousPosition: new Vector2(10, 20));

        var position = PrimitiveRenderLayout.Interpolate(item, 0.5f);
        var rectangle = PrimitiveRenderLayout.ToInterpolatedRectangle(item, 0.5f);

        Assert.Equal(new Vector2(15, 30), position);
        Assert.Equal(11, rectangle.X);
        Assert.Equal(26, rectangle.Y);
        Assert.Equal(new Vector2(20, 40), item.Position);
    }

    [Fact]
    public void AnimationResolverLoopsAndClampsAtFrameBoundaries()
    {
        var manifest = new VisualAssetManifest
        {
            Sprites = [new() { Id = "a" }, new() { Id = "b" }],
            Animations =
            [
                new() { Id = "loop", Frames = ["a", "b"], FrameDurationSeconds = 0.25 },
                new() { Id = "once", Frames = ["a", "b"], FrameDurationSeconds = 0.25, Loop = false }
            ]
        };

        Assert.Equal("a", VisualAssetAnimationResolver.ResolveSpriteId(manifest, "loop", 0));
        Assert.Equal("b", VisualAssetAnimationResolver.ResolveSpriteId(manifest, "loop", 0.25));
        Assert.Equal("a", VisualAssetAnimationResolver.ResolveSpriteId(manifest, "loop", 0.5));
        Assert.Equal("b", VisualAssetAnimationResolver.ResolveSpriteId(manifest, "once", 99));
        Assert.Null(VisualAssetAnimationResolver.ResolveSpriteId(manifest, "missing", 0));
    }

    [Fact]
    public void PixelTextLayoutRightAlignsScoreInsideHud()
    {
        var rectangles = PrimitiveRenderLayout.ToPixelTextRectangles("SCORE 00000123", 624, 16);

        Assert.NotEmpty(rectangles);
        Assert.Equal(16, rectangles.Min(static rectangle => rectangle.Y));
        Assert.Equal(624, rectangles.Max(static rectangle => rectangle.Right));
        Assert.All(rectangles, static rectangle =>
        {
            Assert.Equal(2, rectangle.Width);
            Assert.Equal(2, rectangle.Height);
        });
    }

    [Fact]
    public void PixelTextLayoutSupportsStageTitlesAndFallsBackForOtherCharacters()
    {
        var title = PrimitiveRenderLayout.ToPixelTextRectangles("THE SILENT HORIZON", 400, 100, 3);
        var localized = PrimitiveRenderLayout.ToPixelTextRectangles("ステージ", 400, 140, 2);

        Assert.NotEmpty(title);
        Assert.NotEmpty(localized);
    }

    [Fact]
    public void TouhouLayoutPlacesPlayfieldLeftAndHudPanelRight()
    {
        var definition = new GameDefinition
        {
            Width = 800,
            Height = 720,
            ScreenLayout = "touhou",
            HudPanelWidth = 220,
            ScorePosition = "right-panel"
        };

        var layout = PrimitiveRenderLayout.CreateGameScreenLayout(definition);
        var scoreAnchor = PrimitiveRenderLayout.GetScoreAnchor(
            definition,
            layout,
            "SCORE 00000100");

        Assert.Equal(new Microsoft.Xna.Framework.Rectangle(0, 0, 1020, 720), layout.Window);
        Assert.Equal(new Microsoft.Xna.Framework.Rectangle(0, 0, 800, 720), layout.Playfield);
        Assert.Null(layout.LeftPanel);
        Assert.Equal(new Microsoft.Xna.Framework.Rectangle(800, 0, 220, 720), layout.RightPanel);
        Assert.Equal(new ScoreHudAnchor(1004, 24), scoreAnchor);
    }

    [Fact]
    public void DonpachiLayoutPlacesPlayfieldBetweenTwoHudPanels()
    {
        var definition = new GameDefinition
        {
            Width = 640,
            Height = 800,
            ScreenLayout = "donpachi",
            HudPanelWidth = 200,
            ScorePosition = "left-panel"
        };

        var layout = PrimitiveRenderLayout.CreateGameScreenLayout(definition);
        var scoreAnchor = PrimitiveRenderLayout.GetScoreAnchor(
            definition,
            layout,
            "SCORE 00000100");

        Assert.Equal(new Microsoft.Xna.Framework.Rectangle(0, 0, 1040, 800), layout.Window);
        Assert.Equal(new Microsoft.Xna.Framework.Rectangle(200, 0, 640, 800), layout.Playfield);
        Assert.Equal(new Microsoft.Xna.Framework.Rectangle(0, 0, 200, 800), layout.LeftPanel);
        Assert.Equal(new Microsoft.Xna.Framework.Rectangle(840, 0, 200, 800), layout.RightPanel);
        Assert.Equal(new ScoreHudAnchor(184, 24), scoreAnchor);
    }
}
