using Machina.Fonts;
using Machina.Fonts.Generation.Typography;

namespace Machina.Fonts.Tooling.Tests;

internal static class CrimsonTextFixtureFont
{
    public static readonly FontFaceId Face = new("CrimsonText-Regular");

    public static string FontPath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", "CrimsonText-Regular.ttf");

    public static TypographyGlyphOutlineSource CreateSource()
    {
        return new TypographyGlyphOutlineSource(new Dictionary<FontFaceId, TypographyFontFaceSource>
        {
            [Face] = new(Face, FontPath),
        });
    }
}
