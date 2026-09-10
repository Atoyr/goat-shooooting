using Microsoft.Xna.Framework;
using GoatShooooting.Runtime;

namespace GoatShooooting.Framework;

public static class PrimitiveRenderLayout
{
    public static Rectangle ToRectangle(RenderItem item)
    {
        var diameter = Math.Max(1, (int)MathF.Round(item.Radius * 2));
        return new Rectangle(
            (int)MathF.Round(item.Position.X - item.Radius),
            (int)MathF.Round(item.Position.Y - item.Radius),
            diameter,
            diameter);
    }
}
