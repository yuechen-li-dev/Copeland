using Machina.Fonts.ReferenceRendering;

namespace Machina.Fonts.Tooling;

internal static class DiagnosticDrawing
{
    public static RgbaImage CreateFilledImage(int width, int height, Rgba32 color)
    {
        return new RgbaImage(width, height, Enumerable.Repeat(color, width * height).ToArray());
    }

    public static RgbaImage Clone(RgbaImage source)
    {
        return new RgbaImage(source.Width, source.Height, source.Pixels);
    }

    public static void DrawRectangle(RgbaImage image, FontDiagnosticBounds? bounds, Rgba32 color, double opacity = 1d)
    {
        if (bounds is null)
        {
            return;
        }

        DrawHorizontalSegment(image, bounds.Left, bounds.Right, bounds.Top, color, opacity);
        DrawHorizontalSegment(image, bounds.Left, bounds.Right, bounds.Bottom, color, opacity);
        DrawVerticalSegment(image, bounds.Left, bounds.Top, bounds.Bottom, color, opacity);
        DrawVerticalSegment(image, bounds.Right, bounds.Top, bounds.Bottom, color, opacity);
    }

    public static void DrawHorizontalSegment(RgbaImage image, int left, int right, int y, Rgba32 color, double opacity = 1d)
    {
        if ((uint)y >= (uint)image.Height)
        {
            return;
        }

        int clampedLeft = Math.Max(0, left);
        int clampedRight = Math.Min(image.Width - 1, right);
        for (int x = clampedLeft; x <= clampedRight; x++)
        {
            BlendPixel(image, x, y, color, opacity);
        }
    }

    public static void DrawVerticalSegment(RgbaImage image, int x, int top, int bottom, Rgba32 color, double opacity = 1d)
    {
        if ((uint)x >= (uint)image.Width)
        {
            return;
        }

        int clampedTop = Math.Max(0, top);
        int clampedBottom = Math.Min(image.Height - 1, bottom);
        for (int y = clampedTop; y <= clampedBottom; y++)
        {
            BlendPixel(image, x, y, color, opacity);
        }
    }

    public static void DrawLabel(RgbaImage image, int left, int top, string text, Rgba32 color, double opacity = 1d)
    {
        int cursorX = left;
        foreach (char character in text)
        {
            if (!Glyphs.TryGetValue(character, out string[]? rows))
            {
                cursorX += 4;
                continue;
            }

            for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                int y = top + rowIndex;
                if ((uint)y >= (uint)image.Height)
                {
                    continue;
                }

                string row = rows[rowIndex];
                for (int columnIndex = 0; columnIndex < row.Length; columnIndex++)
                {
                    int x = cursorX + columnIndex;
                    if ((uint)x >= (uint)image.Width)
                    {
                        continue;
                    }

                    if (row[columnIndex] == '1')
                    {
                        BlendPixel(image, x, y, color, opacity);
                    }
                }
            }

            cursorX += 4;
        }
    }

    public static void BlendPixel(RgbaImage image, int x, int y, Rgba32 color, double opacity)
    {
        if ((uint)x >= (uint)image.Width || (uint)y >= (uint)image.Height)
        {
            return;
        }

        double clampedOpacity = Math.Clamp(opacity, 0d, 1d);
        Rgba32 background = image.GetPixel(x, y);
        image.SetPixel(x, y, Blend(background, color, clampedOpacity));
    }

    public static Rgba32 Blend(Rgba32 background, Rgba32 foreground, double opacity)
    {
        double effectiveForegroundAlpha = (foreground.A / 255d) * Math.Clamp(opacity, 0d, 1d);
        double backgroundAlpha = background.A / 255d;
        double outputAlpha = effectiveForegroundAlpha + (backgroundAlpha * (1d - effectiveForegroundAlpha));

        if (outputAlpha <= 0d)
        {
            return Rgba32.Transparent;
        }

        byte r = ToByte(((foreground.R / 255d) * effectiveForegroundAlpha) + ((background.R / 255d) * backgroundAlpha * (1d - effectiveForegroundAlpha)), outputAlpha);
        byte g = ToByte(((foreground.G / 255d) * effectiveForegroundAlpha) + ((background.G / 255d) * backgroundAlpha * (1d - effectiveForegroundAlpha)), outputAlpha);
        byte b = ToByte(((foreground.B / 255d) * effectiveForegroundAlpha) + ((background.B / 255d) * backgroundAlpha * (1d - effectiveForegroundAlpha)), outputAlpha);
        byte a = (byte)Math.Round(outputAlpha * 255d, MidpointRounding.AwayFromZero);

        return new Rgba32(r, g, b, a);
    }

    public static Rgba32 ApplyTint(Rgba32 source, Rgba32 tint, double opacity)
    {
        return Blend(source, new Rgba32(tint.R, tint.G, tint.B, source.A), opacity);
    }

    private static byte ToByte(double premultipliedChannel, double outputAlpha)
    {
        double straight = premultipliedChannel / outputAlpha;
        double clamped = Math.Clamp(straight, 0d, 1d);
        return (byte)Math.Round(clamped * 255d, MidpointRounding.AwayFromZero);
    }

    private static readonly IReadOnlyDictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
    {
        ['0'] = ["111", "101", "101", "101", "111"],
        ['1'] = ["010", "110", "010", "010", "111"],
        ['2'] = ["111", "001", "111", "100", "111"],
        ['3'] = ["111", "001", "111", "001", "111"],
        ['4'] = ["101", "101", "111", "001", "001"],
        ['5'] = ["111", "100", "111", "001", "111"],
        ['6'] = ["111", "100", "111", "101", "111"],
        ['7'] = ["111", "001", "001", "001", "001"],
        ['8'] = ["111", "101", "111", "101", "111"],
        ['9'] = ["111", "101", "111", "001", "111"],
        ['-'] = ["000", "000", "111", "000", "000"],
        [' '] = ["000", "000", "000", "000", "000"],
        ['a'] = ["000", "011", "001", "111", "111"],
        ['b'] = ["100", "110", "101", "101", "110"],
        ['c'] = ["000", "011", "100", "100", "011"],
        ['d'] = ["001", "011", "101", "101", "011"],
        ['e'] = ["000", "011", "111", "100", "011"],
        ['f'] = ["011", "010", "111", "010", "010"],
        ['g'] = ["011", "101", "011", "001", "110"],
        ['h'] = ["100", "110", "101", "101", "101"],
        ['i'] = ["010", "000", "010", "010", "010"],
        ['j'] = ["001", "000", "001", "101", "010"],
        ['k'] = ["100", "101", "110", "101", "101"],
        ['l'] = ["110", "010", "010", "010", "111"],
        ['m'] = ["000", "110", "111", "111", "101"],
        ['n'] = ["000", "110", "101", "101", "101"],
        ['o'] = ["000", "010", "101", "101", "010"],
        ['p'] = ["000", "110", "101", "110", "100"],
        ['q'] = ["000", "011", "101", "011", "001"],
        ['r'] = ["000", "101", "110", "100", "100"],
        ['s'] = ["000", "011", "110", "001", "110"],
        ['t'] = ["010", "111", "010", "010", "011"],
        ['u'] = ["000", "101", "101", "101", "011"],
        ['v'] = ["000", "101", "101", "101", "010"],
        ['w'] = ["000", "101", "111", "111", "010"],
        ['x'] = ["000", "101", "010", "010", "101"],
        ['y'] = ["000", "101", "011", "001", "110"],
        ['z'] = ["000", "111", "010", "100", "111"],
        ['A'] = ["010", "101", "111", "101", "101"],
        ['B'] = ["110", "101", "110", "101", "110"],
        ['C'] = ["011", "100", "100", "100", "011"],
        ['D'] = ["110", "101", "101", "101", "110"],
        ['E'] = ["111", "100", "110", "100", "111"],
        ['F'] = ["111", "100", "110", "100", "100"],
        ['G'] = ["011", "100", "101", "101", "011"],
        ['H'] = ["101", "101", "111", "101", "101"],
        ['I'] = ["111", "010", "010", "010", "111"],
        ['J'] = ["001", "001", "001", "101", "010"],
        ['K'] = ["101", "101", "110", "101", "101"],
        ['L'] = ["100", "100", "100", "100", "111"],
        ['M'] = ["101", "111", "111", "101", "101"],
        ['N'] = ["101", "111", "111", "111", "101"],
        ['O'] = ["010", "101", "101", "101", "010"],
        ['P'] = ["110", "101", "110", "100", "100"],
        ['Q'] = ["010", "101", "101", "011", "001"],
        ['R'] = ["110", "101", "110", "101", "101"],
        ['S'] = ["011", "100", "010", "001", "110"],
        ['T'] = ["111", "010", "010", "010", "010"],
        ['U'] = ["101", "101", "101", "101", "111"],
        ['V'] = ["101", "101", "101", "101", "010"],
        ['W'] = ["101", "101", "111", "111", "101"],
        ['X'] = ["101", "101", "010", "101", "101"],
        ['Y'] = ["101", "101", "010", "010", "010"],
        ['Z'] = ["111", "001", "010", "100", "111"],
    };
}
