using Microsoft.Xna.Framework;
using GoatShooooting.Definitions;
using GoatShooooting.Runtime;

namespace GoatShooooting.Framework;

public readonly record struct GameScreenLayout(
    Rectangle Window,
    Rectangle Playfield,
    Rectangle? LeftPanel,
    Rectangle? RightPanel);

public readonly record struct ScoreHudAnchor(int Right, int Top);

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
        ['A'] = "01110100011000111111100011000110001",
        ['B'] = "11110100011000111110100011000111110",
        ['C'] = "01110100011000010000100001000101110",
        ['D'] = "11110100011000110001100011000111110",
        ['E'] = "11111100001000011110100001000011111",
        ['F'] = "11111100001000011110100001000010000",
        ['G'] = "01110100011000010111100011000101110",
        ['H'] = "10001100011000111111100011000110001",
        ['I'] = "11111001000010000100001000010011111",
        ['J'] = "00111000100001000010100100110001100",
        ['K'] = "10001100101010011000101001001010001",
        ['L'] = "10000100001000010000100001000011111",
        ['M'] = "10001110111010110001100011000110001",
        ['N'] = "10001110011010110011100011000110001",
        ['O'] = "01110100011000110001100011000101110",
        ['P'] = "11110100011000111110100001000010000",
        ['Q'] = "01110100011000110001101011001001101",
        ['R'] = "11110100011000111110101001001010001",
        ['S'] = "01111100001000001110000010000111110",
        ['T'] = "11111001000010000100001000010000100",
        ['U'] = "10001100011000110001100011000101110",
        ['V'] = "10001100011000110001010100010000100",
        ['W'] = "10001100011000110101101011101110001",
        ['X'] = "10001100010101000100010101000110001",
        ['Y'] = "10001100010101000100001000010000100",
        ['Z'] = "11111000010001000100010001000011111",
        ['-'] = "00000000000000011111000000000000000",
        ['.'] = "00000000000000000000000000011000110",
        ['?'] = "01110100010001000100001000000000100"
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

    public static GameScreenLayout CreateGameScreenLayout(GameDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var playfield = string.Equals(definition.ScreenLayout, "donpachi", StringComparison.Ordinal)
            ? new Rectangle(definition.HudPanelWidth, 0, definition.Width, definition.Height)
            : new Rectangle(0, 0, definition.Width, definition.Height);

        return definition.ScreenLayout switch
        {
            "full" => new GameScreenLayout(
                playfield,
                playfield,
                null,
                null),
            "touhou" => new GameScreenLayout(
                new Rectangle(0, 0, definition.Width + definition.HudPanelWidth, definition.Height),
                playfield,
                null,
                new Rectangle(definition.Width, 0, definition.HudPanelWidth, definition.Height)),
            "donpachi" => new GameScreenLayout(
                new Rectangle(0, 0, definition.Width + (definition.HudPanelWidth * 2), definition.Height),
                playfield,
                new Rectangle(0, 0, definition.HudPanelWidth, definition.Height),
                new Rectangle(
                    definition.HudPanelWidth + definition.Width,
                    0,
                    definition.HudPanelWidth,
                    definition.Height)),
            _ => throw new ArgumentException(
                $"Unsupported screen layout '{definition.ScreenLayout}'.",
                nameof(definition))
        };
    }

    public static ScoreHudAnchor GetScoreAnchor(
        GameDefinition definition,
        GameScreenLayout layout,
        string scoreText,
        int scale = 2)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var scoreSize = MeasurePixelText(scoreText, scale);
        return definition.ScorePosition switch
        {
            "playfield-top-left" => new ScoreHudAnchor(
                layout.Playfield.Left + 16 + scoreSize.X,
                56),
            "playfield-top-right" => new ScoreHudAnchor(layout.Playfield.Right - 16, 16),
            "left-panel" => new ScoreHudAnchor(
                layout.LeftPanel?.Right - 16 ?? throw MissingPanel(definition.ScorePosition),
                24),
            "right-panel" => new ScoreHudAnchor(
                layout.RightPanel?.Right - 16 ?? throw MissingPanel(definition.ScorePosition),
                24),
            _ => throw new ArgumentException(
                $"Unsupported score position '{definition.ScorePosition}'.",
                nameof(definition))
        };
    }

    public static Point MeasurePixelText(string text, int scale = 2)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        var width = text.Length == 0
            ? 0
            : (text.Length * (GlyphWidth + 1) * scale) - scale;
        return new Point(width, GlyphHeight * scale);
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
        var left = right - MeasurePixelText(text, scale).X;
        var rectangles = new List<Rectangle>();
        for (var characterIndex = 0; characterIndex < text.Length; characterIndex++)
        {
            var character = char.ToUpperInvariant(text[characterIndex]);
            var glyph = Glyphs.TryGetValue(character, out var supportedGlyph)
                ? supportedGlyph
                : Glyphs['?'];

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

    private static InvalidOperationException MissingPanel(string scorePosition) => new(
        $"Score position '{scorePosition}' refers to a panel that is not available in the screen layout.");
}
