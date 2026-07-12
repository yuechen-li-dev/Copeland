using Machina.Fonts;
using Machina.Fonts.Generation.Typography;

namespace Machina.Fonts.Tests.Generation.Typography;

internal static class TypographyFixtureFont
{
    public static readonly FontFaceId Face = new("SpaceMono-Regular");

    public static string FontPath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", "SpaceMono-Regular.ttf");

    public static string LicensePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", "SpaceMono-Regular.LICENSE.txt");

    public static string ReadmePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", "README.md");

    public static TypographyGlyphOutlineSource CreateSource()
    {
        return new TypographyGlyphOutlineSource(new Dictionary<FontFaceId, TypographyFontFaceSource>
        {
            [Face] = new(Face, FontPath),
        });
    }
}
