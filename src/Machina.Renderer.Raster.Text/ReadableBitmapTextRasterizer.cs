using System.Collections.ObjectModel;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Renderer.Raster.Colors;
using Machina.Renderer.Raster.Rasterization;
using Machina.Renderer.Raster.Surface;

namespace Machina.Renderer.Raster.Text;

public sealed class ReadableBitmapTextRasterizer : ITextRasterizer
{
    private const int GlyphWidth = 5;
    private const int GlyphHeight = 7;
    private const int GlyphGap = 1;
    private const char FallbackGlyph = '?';

    private static readonly ReadOnlyDictionary<char, byte[]> Glyphs = new(CreateGlyphs());

    public void DrawText(RasterSurface surface, Rect rect, string text, TextStyle style, Rgba32 color, Rect? clip = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(style);

        var scale = GetScale(style.Size);
        var advance = (GlyphWidth + GlyphGap) * scale;
        var (textWidth, textHeight) = Measure(text, style.Size);
        var drawX = ResolveAlignedX(rect, textWidth, style.AlignX);
        var drawY = ResolveAlignedY(rect, textHeight, style.AlignY);
        var rectRight = rect.X + rect.Width;
        var effectiveClip = ResolveEffectiveClip(rect, clip);

        foreach (var rawCharacter in text)
        {
            if (drawX >= rectRight)
            {
                break;
            }

            var character = NormalizeCharacter(rawCharacter);
            if (character != ' ')
            {
                DrawGlyph(surface, drawX, drawY, character, scale, color, effectiveClip);
            }

            drawX += advance;
        }
    }

    private static Rect ResolveEffectiveClip(Rect rect, Rect? clip)
    {
        if (clip is not { } clipValue)
        {
            return rect;
        }

        var left = Math.Max(rect.X, clipValue.X);
        var top = Math.Max(rect.Y, clipValue.Y);
        var right = Math.Min(rect.X + rect.Width, clipValue.X + clipValue.Width);
        var bottom = Math.Min(rect.Y + rect.Height, clipValue.Y + clipValue.Height);
        var width = Math.Max(0, right - left);
        var height = Math.Max(0, bottom - top);
        return new Rect(left, top, width, height);
    }

    public static (int Width, int Height) MeasureText(string text, TextStyle style)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(style);
        return Measure(text, style.Size);
    }

    private static (int Width, int Height) Measure(string text, TextSize size)
    {
        if (text.Length == 0)
        {
            return (0, 0);
        }

        var scale = GetScale(size);
        var advance = (GlyphWidth + GlyphGap) * scale;
        var width = (advance * text.Length) - (GlyphGap * scale);
        var height = GlyphHeight * scale;
        return (width, height);
    }

    private static int ResolveAlignedX(Rect rect, int textWidth, TextAlignX alignX)
    {
        var left = (int)Math.Floor(rect.X);
        var width = (int)Math.Floor(rect.Width);
        return alignX switch
        {
            TextAlignX.Left => left,
            TextAlignX.Center => left + ((width - textWidth) / 2),
            TextAlignX.Right => left + width - textWidth,
            _ => left,
        };
    }

    private static int ResolveAlignedY(Rect rect, int textHeight, TextAlignY alignY)
    {
        var top = (int)Math.Floor(rect.Y);
        var height = (int)Math.Floor(rect.Height);
        return alignY switch
        {
            TextAlignY.Top => top,
            TextAlignY.Center => top + ((height - textHeight) / 2),
            TextAlignY.Bottom => top + height - textHeight,
            _ => top,
        };
    }

    private static void DrawGlyph(RasterSurface surface, int originX, int originY, char character, int scale, Rgba32 color, Rect? clip)
    {
        var glyphRows = ResolveGlyph(character);

        for (var row = 0; row < GlyphHeight; row++)
        {
            var rowBits = glyphRows[row];
            for (var col = 0; col < GlyphWidth; col++)
            {
                var bit = 1 << (GlyphWidth - 1 - col);
                if ((rowBits & bit) == 0)
                {
                    continue;
                }

                var pixelX = originX + (col * scale);
                var pixelY = originY + (row * scale);
                Rasterizer.FillRect(surface, new Rect(pixelX, pixelY, scale, scale), color, clip);
            }
        }
    }

    private static byte[] ResolveGlyph(char character)
    {
        if (Glyphs.TryGetValue(character, out var glyph))
        {
            return glyph;
        }

        return Glyphs[FallbackGlyph];
    }

    private static char NormalizeCharacter(char character)
    {
        if (char.IsWhiteSpace(character))
        {
            return ' ';
        }

        return char.ToUpperInvariant(character);
    }

    private static int GetScale(TextSize size)
    {
        return size switch
        {
            TextSize.Sm => 1,
            TextSize.Md => 2,
            TextSize.H1 => 3,
            _ => 2,
        };
    }

    private static Dictionary<char, byte[]> CreateGlyphs()
    {
        return new Dictionary<char, byte[]>
        {
            [' '] = Rows("00000","00000","00000","00000","00000","00000","00000"),
            ['A'] = Rows("01110","10001","10001","11111","10001","10001","10001"),
            ['B'] = Rows("11110","10001","10001","11110","10001","10001","11110"),
            ['C'] = Rows("01111","10000","10000","10000","10000","10000","01111"),
            ['D'] = Rows("11110","10001","10001","10001","10001","10001","11110"),
            ['E'] = Rows("11111","10000","10000","11110","10000","10000","11111"),
            ['F'] = Rows("11111","10000","10000","11110","10000","10000","10000"),
            ['G'] = Rows("01111","10000","10000","10111","10001","10001","01111"),
            ['H'] = Rows("10001","10001","10001","11111","10001","10001","10001"),
            ['I'] = Rows("11111","00100","00100","00100","00100","00100","11111"),
            ['J'] = Rows("00111","00010","00010","00010","10010","10010","01100"),
            ['K'] = Rows("10001","10010","10100","11000","10100","10010","10001"),
            ['L'] = Rows("10000","10000","10000","10000","10000","10000","11111"),
            ['M'] = Rows("10001","11011","10101","10101","10001","10001","10001"),
            ['N'] = Rows("10001","10001","11001","10101","10011","10001","10001"),
            ['O'] = Rows("01110","10001","10001","10001","10001","10001","01110"),
            ['P'] = Rows("11110","10001","10001","11110","10000","10000","10000"),
            ['Q'] = Rows("01110","10001","10001","10001","10101","10010","01101"),
            ['R'] = Rows("11110","10001","10001","11110","10100","10010","10001"),
            ['S'] = Rows("01111","10000","10000","01110","00001","00001","11110"),
            ['T'] = Rows("11111","00100","00100","00100","00100","00100","00100"),
            ['U'] = Rows("10001","10001","10001","10001","10001","10001","01110"),
            ['V'] = Rows("10001","10001","10001","10001","10001","01010","00100"),
            ['W'] = Rows("10001","10001","10001","10101","10101","10101","01010"),
            ['X'] = Rows("10001","10001","01010","00100","01010","10001","10001"),
            ['Y'] = Rows("10001","10001","01010","00100","00100","00100","00100"),
            ['Z'] = Rows("11111","00001","00010","00100","01000","10000","11111"),
            ['0'] = Rows("01110","10001","10011","10101","11001","10001","01110"),
            ['1'] = Rows("00100","01100","00100","00100","00100","00100","01110"),
            ['2'] = Rows("01110","10001","00001","00010","00100","01000","11111"),
            ['3'] = Rows("11110","00001","00001","01110","00001","00001","11110"),
            ['4'] = Rows("00010","00110","01010","10010","11111","00010","00010"),
            ['5'] = Rows("11111","10000","10000","11110","00001","00001","11110"),
            ['6'] = Rows("01110","10000","10000","11110","10001","10001","01110"),
            ['7'] = Rows("11111","00001","00010","00100","01000","01000","01000"),
            ['8'] = Rows("01110","10001","10001","01110","10001","10001","01110"),
            ['9'] = Rows("01110","10001","10001","01111","00001","00001","01110"),
            [':'] = Rows("00000","00100","00100","00000","00100","00100","00000"),
            ['.'] = Rows("00000","00000","00000","00000","00000","00100","00100"),
            [','] = Rows("00000","00000","00000","00000","00100","00100","01000"),
            ['-'] = Rows("00000","00000","00000","01110","00000","00000","00000"),
            ['_'] = Rows("00000","00000","00000","00000","00000","00000","11111"),
            ['+'] = Rows("00000","00100","00100","11111","00100","00100","00000"),
            ['/'] = Rows("00001","00010","00100","01000","10000","00000","00000"),
            ['!'] = Rows("00100","00100","00100","00100","00100","00000","00100"),
            ['?'] = Rows("01110","10001","00001","00010","00100","00000","00100"),
            ['('] = Rows("00010","00100","01000","01000","01000","00100","00010"),
            [')'] = Rows("01000","00100","00010","00010","00010","00100","01000"),
            ['['] = Rows("01110","01000","01000","01000","01000","01000","01110"),
            [']'] = Rows("01110","00010","00010","00010","00010","00010","01110"),
            ['\''] = Rows("00100","00100","00000","00000","00000","00000","00000"),
            ['"'] = Rows("01010","01010","00000","00000","00000","00000","00000"),
            ['#'] = Rows("01010","11111","01010","01010","11111","01010","00000"),
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
