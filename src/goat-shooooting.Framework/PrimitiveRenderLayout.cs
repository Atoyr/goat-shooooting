using Microsoft.Xna.Framework;
using GoatShooooting.Runtime;

namespace GoatShooooting.Framework;

public static class PrimitiveRenderLayout
{
    private const int GlyphWidth = 5;
    private const int GlyphHeight = 7;
    private static readonly IReadOnlyDictionary<char, string> Glyphs = new Dictionary<char, string>
    {
        [' '] = "00000000000000000000000000000000000",
        ['0'] = "01110100011001110101110011000101110",
        ['1'] = "00100011000010000100001000010001110",
        ['2'] = "01110100010000100010001000100011111",
        ['3'] = "11110000010000101110000010000111110",
        ['4'] = "00010001100101010010111110001000010",
        ['5'] = "11111100001111000001000011000101110",
        ['6'] = "00110010001000011110100011000101110",
        ['7'] = "11111000010001000100010000100001000",
        ['8'] = "01110100011000101110100011000101110",
        ['9'] = "01110100011000101111000010001001100",
        ['S'] = "01111100001000001110000010000111110",
        ['C'] = "01110100011000010000100001000101110",
        ['O'] = "01110100011000110001100011000101110",
        ['R'] = "11110100011000111110101001001010001",
        ['E'] = "11111100001000011110100001000011111"
    };

    public static Rectangle ToRectangle(RenderItem item)
    {
        var diameter = Math.Max(1, (int)MathF.Round(item.Radius * 2));
        return new Rectangle(
            (int)MathF.Round(item.Position.X - item.Radius),
            (int)MathF.Round(item.Position.Y - item.Radius),
            diameter,
            diameter);
    }

    public static IReadOnlyList<Rectangle> ToPixelTextRectangles(
        string text,
        int right,
        int top,
        int scale = 2)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        var glyphAdvance = (GlyphWidth + 1) * scale;
        var left = right - Math.Max(0, (text.Length * glyphAdvance) - scale);
        var rectangles = new List<Rectangle>();
        for (var characterIndex = 0; characterIndex < text.Length; characterIndex++)
        {
            if (!Glyphs.TryGetValue(char.ToUpperInvariant(text[characterIndex]), out var glyph))
            {
                throw new ArgumentException(
                    $"Character '{text[characterIndex]}' cannot be rendered by the pixel font.",
                    nameof(text));
            }

            for (var pixelIndex = 0; pixelIndex < glyph.Length; pixelIndex++)
            {
                if (glyph[pixelIndex] == '0')
                {
                    continue;
                }

                rectangles.Add(new Rectangle(
                    left + (characterIndex * glyphAdvance) + ((pixelIndex % GlyphWidth) * scale),
                    top + ((pixelIndex / GlyphWidth) * scale),
                    scale,
                    scale));
            }
        }

        return rectangles;
    }
}
