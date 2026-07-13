using System.Collections.ObjectModel;
using Aurelian.Rendering.Contracts.Resolved2D;

namespace Aurelian.Rendering.Raster;

internal static class DeterministicBitmapTextRenderer
{
    private const int GlyphWidth = 5;
    private const int GlyphHeight = 7;
    private const int GlyphGap = 1;
    private const char FallbackGlyph = '?';

    private static readonly ReadOnlyDictionary<char, byte[]> Glyphs = new(CreateGlyphs());

    public static void Draw(RasterBuffer buffer, PositionedTextOperation operation, PixelBounds currentClip)
    {
        if (operation.Text.Length == 0 || operation.Bounds.IsEmptyOrNegative || currentClip.IsEmpty)
        {
            return;
        }

        if (operation.Face != Resolved2DTextFace.ReadableBitmap5x7)
        {
            throw new InvalidOperationException($"Text face '{operation.Face}' is not supported by the deterministic raster backend.");
        }

        var scale = (int)operation.Size;
        var advance = (GlyphWidth + GlyphGap) * scale;
        var textWidth = operation.Text.Length == 0 ? 0 : (operation.Text.Length * advance) - (GlyphGap * scale);
        var textHeight = GlyphHeight * scale;
        var drawX = ResolveAlignedX(operation.Bounds, textWidth, operation.AlignX);
        var drawY = ResolveAlignedY(operation.Bounds, textHeight, operation.AlignY);
        PixelBounds textClip = PixelBounds.Intersect(currentClip, PixelBounds.FromRectangle(operation.Bounds));
        var textRight = operation.Bounds.X + operation.Bounds.Width;

        foreach (char rawCharacter in operation.Text)
        {
            if (drawX >= textRight)
            {
                break;
            }

            char character = NormalizeCharacter(rawCharacter);
            if (character != ' ')
            {
                DrawGlyph(buffer, drawX, drawY, character, scale, operation.Color, textClip);
            }

            drawX += advance;
        }
    }

    private static int ResolveAlignedX(Resolved2DRectangle bounds, int textWidth, Resolved2DTextAlignX alignment)
    {
        var left = (int)Math.Floor(bounds.X);
        var width = (int)Math.Floor(bounds.Width);

        return alignment switch
        {
            Resolved2DTextAlignX.Left => left,
            Resolved2DTextAlignX.Center => left + ((width - textWidth) / 2),
            Resolved2DTextAlignX.Right => left + width - textWidth,
            _ => throw new InvalidOperationException($"Unsupported horizontal text alignment '{alignment}'.")
        };
    }

    private static int ResolveAlignedY(Resolved2DRectangle bounds, int textHeight, Resolved2DTextAlignY alignment)
    {
        var top = (int)Math.Floor(bounds.Y);
        var height = (int)Math.Floor(bounds.Height);

        return alignment switch
        {
            Resolved2DTextAlignY.Top => top,
            Resolved2DTextAlignY.Center => top + ((height - textHeight) / 2),
            Resolved2DTextAlignY.Bottom => top + height - textHeight,
            _ => throw new InvalidOperationException($"Unsupported vertical text alignment '{alignment}'.")
        };
    }

    private static void DrawGlyph(
        RasterBuffer buffer,
        int originX,
        int originY,
        char character,
        int scale,
        Resolved2DRgbaColor color,
        PixelBounds clip)
    {
        byte[] rows = ResolveGlyph(character);

        for (var row = 0; row < GlyphHeight; row++)
        {
            byte rowBits = rows[row];
            for (var column = 0; column < GlyphWidth; column++)
            {
                var bit = 1 << (GlyphWidth - 1 - column);
                if ((rowBits & bit) == 0)
                {
                    continue;
                }

                buffer.FillRectangle(
                    new Resolved2DRectangle(originX + (column * scale), originY + (row * scale), scale, scale),
                    color,
                    clip);
            }
        }
    }

    private static byte[] ResolveGlyph(char character)
    {
        return Glyphs.TryGetValue(character, out byte[]? glyph) ? glyph : Glyphs[FallbackGlyph];
    }

    private static char NormalizeCharacter(char character)
    {
        return char.IsWhiteSpace(character) ? ' ' : char.ToUpperInvariant(character);
    }

    private static Dictionary<char, byte[]> CreateGlyphs()
    {
        return new Dictionary<char, byte[]>
        {
            [' '] = Rows("00000", "00000", "00000", "00000", "00000", "00000", "00000"),
            ['A'] = Rows("01110", "10001", "10001", "11111", "10001", "10001", "10001"),
            ['B'] = Rows("11110", "10001", "10001", "11110", "10001", "10001", "11110"),
            ['C'] = Rows("01111", "10000", "10000", "10000", "10000", "10000", "01111"),
            ['D'] = Rows("11110", "10001", "10001", "10001", "10001", "10001", "11110"),
            ['E'] = Rows("11111", "10000", "10000", "11110", "10000", "10000", "11111"),
            ['F'] = Rows("11111", "10000", "10000", "11110", "10000", "10000", "10000"),
            ['G'] = Rows("01111", "10000", "10000", "10111", "10001", "10001", "01111"),
            ['H'] = Rows("10001", "10001", "10001", "11111", "10001", "10001", "10001"),
            ['I'] = Rows("11111", "00100", "00100", "00100", "00100", "00100", "11111"),
            ['J'] = Rows("00111", "00010", "00010", "00010", "10010", "10010", "01100"),
            ['K'] = Rows("10001", "10010", "10100", "11000", "10100", "10010", "10001"),
            ['L'] = Rows("10000", "10000", "10000", "10000", "10000", "10000", "11111"),
            ['M'] = Rows("10001", "11011", "10101", "10101", "10001", "10001", "10001"),
            ['N'] = Rows("10001", "10001", "11001", "10101", "10011", "10001", "10001"),
            ['O'] = Rows("01110", "10001", "10001", "10001", "10001", "10001", "01110"),
            ['P'] = Rows("11110", "10001", "10001", "11110", "10000", "10000", "10000"),
            ['Q'] = Rows("01110", "10001", "10001", "10001", "10101", "10010", "01101"),
            ['R'] = Rows("11110", "10001", "10001", "11110", "10100", "10010", "10001"),
            ['S'] = Rows("01111", "10000", "10000", "01110", "00001", "00001", "11110"),
            ['T'] = Rows("11111", "00100", "00100", "00100", "00100", "00100", "00100"),
            ['U'] = Rows("10001", "10001", "10001", "10001", "10001", "10001", "01110"),
            ['V'] = Rows("10001", "10001", "10001", "10001", "10001", "01010", "00100"),
            ['W'] = Rows("10001", "10001", "10001", "10101", "10101", "10101", "01010"),
            ['X'] = Rows("10001", "10001", "01010", "00100", "01010", "10001", "10001"),
            ['Y'] = Rows("10001", "10001", "01010", "00100", "00100", "00100", "00100"),
            ['Z'] = Rows("11111", "00001", "00010", "00100", "01000", "10000", "11111"),
            ['0'] = Rows("01110", "10001", "10011", "10101", "11001", "10001", "01110"),
            ['1'] = Rows("00100", "01100", "00100", "00100", "00100", "00100", "01110"),
            ['2'] = Rows("01110", "10001", "00001", "00010", "00100", "01000", "11111"),
            ['3'] = Rows("11110", "00001", "00001", "01110", "00001", "00001", "11110"),
            ['4'] = Rows("00010", "00110", "01010", "10010", "11111", "00010", "00010"),
            ['5'] = Rows("11111", "10000", "10000", "11110", "00001", "00001", "11110"),
            ['6'] = Rows("01110", "10000", "10000", "11110", "10001", "10001", "01110"),
            ['7'] = Rows("11111", "00001", "00010", "00100", "01000", "01000", "01000"),
            ['8'] = Rows("01110", "10001", "10001", "01110", "10001", "10001", "01110"),
            ['9'] = Rows("01110", "10001", "10001", "01111", "00001", "00001", "01110"),
            [':'] = Rows("00000", "00100", "00100", "00000", "00100", "00100", "00000"),
            ['.'] = Rows("00000", "00000", "00000", "00000", "00000", "00100", "00100"),
            [','] = Rows("00000", "00000", "00000", "00000", "00100", "00100", "01000"),
            ['-'] = Rows("00000", "00000", "00000", "01110", "00000", "00000", "00000"),
            ['•'] = Rows("00000", "00100", "01110", "01110", "01110", "00100", "00000"),
            ['_'] = Rows("00000", "00000", "00000", "00000", "00000", "00000", "11111"),
            ['+'] = Rows("00000", "00100", "00100", "11111", "00100", "00100", "00000"),
            ['/'] = Rows("00001", "00010", "00100", "01000", "10000", "00000", "00000"),
            ['!'] = Rows("00100", "00100", "00100", "00100", "00100", "00000", "00100"),
            ['?'] = Rows("01110", "10001", "00001", "00010", "00100", "00000", "00100"),
            ['('] = Rows("00010", "00100", "01000", "01000", "01000", "00100", "00010"),
            [')'] = Rows("01000", "00100", "00010", "00010", "00010", "00100", "01000"),
            ['['] = Rows("01110", "01000", "01000", "01000", "01000", "01000", "01110"),
            [']'] = Rows("01110", "00010", "00010", "00010", "00010", "00010", "01110"),
            ['\''] = Rows("00100", "00100", "00000", "00000", "00000", "00000", "00000"),
            ['"'] = Rows("01010", "01010", "00000", "00000", "00000", "00000", "00000"),
            ['#'] = Rows("01010", "11111", "01010", "01010", "11111", "01010", "00000")
        };
    }

    private static byte[] Rows(params string[] rows)
    {
        if (rows.Length != GlyphHeight)
        {
            throw new ArgumentException($"Expected {GlyphHeight} rows per glyph.", nameof(rows));
        }

        var result = new byte[GlyphHeight];
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            result[rowIndex] = Convert.ToByte(rows[rowIndex], 2);
        }

        return result;
    }
}
