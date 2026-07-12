using Xunit;
using System.Globalization;
using Machina.Fonts.Toml;

namespace Machina.Fonts.Tests.Toml;

public sealed class FontAtlasTomlWriterTests
{
    [Fact]
    public void Writer_EmitsCanonicalSectionOrder()
    {
        string text = FontAtlasTomlWriter.Write(FontAtlasTomlTestData.CreateDocument());
        Assert.True(text.IndexOf("[atlas]", StringComparison.Ordinal) < text.IndexOf("[font]", StringComparison.Ordinal));
        Assert.True(text.IndexOf("[font]", StringComparison.Ordinal) < text.IndexOf("[metrics]", StringComparison.Ordinal));
        Assert.True(text.IndexOf("[metrics]", StringComparison.Ordinal) < text.IndexOf("[msdf]", StringComparison.Ordinal));
        Assert.True(text.IndexOf("[msdf]", StringComparison.Ordinal) < text.IndexOf("[[page]]", StringComparison.Ordinal));
        Assert.True(text.IndexOf("[[page]]", StringComparison.Ordinal) < text.IndexOf("[[glyph]]", StringComparison.Ordinal));
    }

    [Fact]
    public void Writer_SortsPagesByIndex()
    {
        FontAtlasTomlDocument document = FontAtlasTomlTestData.CreateDocument() with { Pages = [new FontAtlasPageToml { Index = 2, Image = "b.png", Width = 1, Height = 1, ContentHash = "h" }, new FontAtlasPageToml { Index = 1, Image = "a.png", Width = 1, Height = 1, ContentHash = "h" }] };
        string text = FontAtlasTomlWriter.Write(document);
        Assert.True(text.IndexOf("index = 1", StringComparison.Ordinal) < text.IndexOf("index = 2", StringComparison.Ordinal));
    }

    [Fact]
    public void Writer_SortsGlyphsDeterministically()
    {
        FontAtlasGlyphToml z = FontAtlasTomlTestData.CreateDocument().Glyphs[0] with { Codepoint = 90, Char = "Z" };
        FontAtlasGlyphToml a = FontAtlasTomlTestData.CreateDocument().Glyphs[0] with { Codepoint = 65, Char = "A" };
        FontAtlasTomlDocument document = FontAtlasTomlTestData.CreateDocument() with { Glyphs = [z, a] };
        string text = FontAtlasTomlWriter.Write(document);
        Assert.True(text.IndexOf("codepoint = 65", StringComparison.Ordinal) < text.IndexOf("codepoint = 90", StringComparison.Ordinal));
    }

    [Fact]
    public void Writer_UsesInvariantNumericFormatting()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            string text = FontAtlasTomlWriter.Write(FontAtlasTomlTestData.CreateDocument());
            Assert.Contains("u0 = 0.01171875", text, StringComparison.Ordinal);
            Assert.DoesNotContain("0,01171875", text, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Writer_DoesNotEmitGeneratedTimestamp()
    {
        Assert.DoesNotContain("timestamp", FontAtlasTomlWriter.Write(FontAtlasTomlTestData.CreateDocument()), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Writer_EmitsPrintableCharWhenPresent()
    {
        Assert.Contains("char = \"A\"", FontAtlasTomlWriter.Write(FontAtlasTomlTestData.CreateDocument()), StringComparison.Ordinal);
    }

    [Fact]
    public void FontAtlasTomlWriter_EmitsPlacementFields()
    {
        string text = FontAtlasTomlWriter.Write(FontAtlasTomlTestData.CreateDocument());

        Assert.Contains("plane_left =", text, StringComparison.Ordinal);
        Assert.Contains("plane_top =", text, StringComparison.Ordinal);
        Assert.Contains("plane_right =", text, StringComparison.Ordinal);
        Assert.Contains("plane_bottom =", text, StringComparison.Ordinal);
        Assert.Contains("pixel_range =", text, StringComparison.Ordinal);
        Assert.Contains("projection_scale =", text, StringComparison.Ordinal);
    }
}
