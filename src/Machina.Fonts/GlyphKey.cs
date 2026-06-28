using System.Text;

namespace Machina.Fonts;

public enum MachinaFontWeight
{
    Regular = 400,
    Bold = 700,
}

public enum MachinaFontSlant
{
    Upright = 0,
    Italic = 1,
    Oblique = 2,
}

public readonly record struct GlyphKey
{
    public GlyphKey(
        FontFaceId face,
        int codepoint,
        double emSize,
        MachinaFontWeight weight,
        MachinaFontSlant slant)
    {
        ValidateCodepoint(codepoint);
        ValidateEmSize(emSize);

        Face = face;
        Codepoint = codepoint;
        EmSize = emSize;
        Weight = weight;
        Slant = slant;
    }

    public FontFaceId Face { get; }

    public int Codepoint { get; }

    public double EmSize { get; }

    public MachinaFontWeight Weight { get; }

    public MachinaFontSlant Slant { get; }

    public static GlyphKey FromChar(
        FontFaceId face,
        char value,
        double emSize,
        MachinaFontWeight weight = MachinaFontWeight.Regular,
        MachinaFontSlant slant = MachinaFontSlant.Upright)
    {
        if (char.IsSurrogate(value))
        {
            throw new ArgumentException("Use FromRune or FromCodepoint for supplementary codepoints.", nameof(value));
        }

        return FromCodepoint(face, value, emSize, weight, slant);
    }

    public static GlyphKey FromRune(
        FontFaceId face,
        Rune rune,
        double emSize,
        MachinaFontWeight weight = MachinaFontWeight.Regular,
        MachinaFontSlant slant = MachinaFontSlant.Upright)
    {
        return FromCodepoint(face, rune.Value, emSize, weight, slant);
    }

    public static GlyphKey FromCodepoint(
        FontFaceId face,
        int codepoint,
        double emSize,
        MachinaFontWeight weight = MachinaFontWeight.Regular,
        MachinaFontSlant slant = MachinaFontSlant.Upright)
    {
        return new GlyphKey(face, codepoint, emSize, weight, slant);
    }

    public static bool IsValidCodepoint(int codepoint)
    {
        if (codepoint < 0 || codepoint > 0x10FFFF)
        {
            return false;
        }

        return codepoint < 0xD800 || codepoint > 0xDFFF;
    }

    private static void ValidateCodepoint(int codepoint)
    {
        if (!IsValidCodepoint(codepoint))
        {
            throw new ArgumentOutOfRangeException(nameof(codepoint), "Codepoint must be a valid Unicode scalar value.");
        }
    }

    private static void ValidateEmSize(double emSize)
    {
        if (!double.IsFinite(emSize) || emSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(emSize), "Em size must be finite and greater than zero.");
        }
    }
}
