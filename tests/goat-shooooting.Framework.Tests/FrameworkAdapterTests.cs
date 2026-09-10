using System.Numerics;
using Microsoft.Xna.Framework.Input;
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

        input.Apply(new[] { Keys.A, Keys.Up, Keys.Space, Keys.R, Keys.Escape });

        Assert.Equal(-1, input.MoveX);
        Assert.Equal(-1, input.MoveY);
        Assert.True(input.Fire);
        Assert.True(input.Retry);
        Assert.True(input.QuitRequested);
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
}
