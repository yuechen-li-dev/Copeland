using System.Text;

namespace Machina.Fonts.ReferenceRendering;

public sealed record DistanceFieldTextRun(string Text, IReadOnlyList<GlyphKey> GlyphKeys)
{
    public static DistanceFieldTextRun Create(
        string text,
        FontFaceId face,
        double emSize,
        MachinaFontWeight weight,
        MachinaFontSlant slant)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<GlyphKey> glyphKeys = [];
        foreach (Rune rune in text.EnumerateRunes())
        {
            glyphKeys.Add(GlyphKey.FromRune(face, rune, emSize, weight, slant));
        }

        return new DistanceFieldTextRun(text, glyphKeys);
    }
}
