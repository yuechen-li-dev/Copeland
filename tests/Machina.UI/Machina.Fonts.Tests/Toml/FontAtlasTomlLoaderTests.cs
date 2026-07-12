using Xunit;
using Machina.Fonts.Toml;
using Machina.Fonts;

namespace Machina.Fonts.Tests.Toml;

public sealed class FontAtlasTomlLoaderTests
{
    [Fact]
    public void Loader_LoadsValidFontAtlasToml()
    {
        FontAtlasTomlLoadResult result = FontAtlasTomlLoader.LoadString(FontAtlasTomlWriter.Write(FontAtlasTomlTestData.CreateDocument()));
        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.NotNull(result.Snapshot);
    }

    [Theory]
    [InlineData("format = 1", "format = 2", FontAtlasTomlDiagnosticCode.UnsupportedFormat)]
    [InlineData("kind = \"machina-font-atlas\"", "kind = \"bad\"", FontAtlasTomlDiagnosticCode.InvalidKind)]
    public void Loader_RejectsHeaderErrors(string original, string replacement, FontAtlasTomlDiagnosticCode code)
    {
        string text = FontAtlasTomlWriter.Write(FontAtlasTomlTestData.CreateDocument()).Replace(original, replacement, StringComparison.Ordinal);
        FontAtlasTomlLoadResult result = FontAtlasTomlLoader.LoadString(text);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Fact]
    public void Loader_ReportsMissingRequiredField()
    {
        FontAtlasTomlDocument document = FontAtlasTomlTestData.CreateDocument() with { Font = FontAtlasTomlTestData.CreateDocument().Font with { Source = string.Empty } };
        FontAtlasTomlLoadResult result = FontAtlasTomlLoader.LoadString(FontAtlasTomlWriter.Write(document));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == FontAtlasTomlDiagnosticCode.MissingRequiredField);
    }

    [Fact]
    public void Loader_ReportsParseError()
    {
        FontAtlasTomlLoadResult result = FontAtlasTomlLoader.LoadString("[atlas\nformat = 1");
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == FontAtlasTomlDiagnosticCode.ParseError || diagnostic.Code == FontAtlasTomlDiagnosticCode.BindError);
    }

    [Fact]
    public void Loader_ReportsDuplicatePage()
    {
        FontAtlasTomlDocument document = FontAtlasTomlTestData.CreateDocument() with { Pages = [FontAtlasTomlTestData.CreateDocument().Pages[0], FontAtlasTomlTestData.CreateDocument().Pages[0] with { Image = "other.png" }] };
        AssertCode(document, FontAtlasTomlDiagnosticCode.DuplicatePage);
    }

    [Fact]
    public void Loader_ReportsDuplicateGlyph()
    {
        FontAtlasTomlDocument document = FontAtlasTomlTestData.CreateDocument() with { Glyphs = [FontAtlasTomlTestData.CreateDocument().Glyphs[0], FontAtlasTomlTestData.CreateDocument().Glyphs[0] with { X = 99 }] };
        AssertCode(document, FontAtlasTomlDiagnosticCode.DuplicateGlyph);
    }

    [Fact]
    public void Loader_ReportsMissingPage()
    {
        FontAtlasTomlDocument document = FontAtlasTomlTestData.CreateDocument() with { Glyphs = [FontAtlasTomlTestData.CreateDocument().Glyphs[0] with { Page = 4 }] };
        AssertCode(document, FontAtlasTomlDiagnosticCode.MissingPage);
    }

    [Fact]
    public void Loader_ReportsGlyphOutOfBounds()
    {
        FontAtlasTomlDocument document = FontAtlasTomlTestData.CreateDocument() with { Glyphs = [FontAtlasTomlTestData.CreateDocument().Glyphs[0] with { X = 1000, Width = 40 }] };
        AssertCode(document, FontAtlasTomlDiagnosticCode.GlyphOutOfBounds);
    }

    [Fact]
    public void Loader_ReportsCharCodepointMismatch()
    {
        FontAtlasTomlDocument document = FontAtlasTomlTestData.CreateDocument() with { Glyphs = [FontAtlasTomlTestData.CreateDocument().Glyphs[0] with { Char = "B" }] };
        AssertCode(document, FontAtlasTomlDiagnosticCode.CharCodepointMismatch);
    }

    [Fact]
    public void Loader_WarnsOrReportsUvMismatch()
    {
        FontAtlasTomlDocument document = FontAtlasTomlTestData.CreateDocument() with { Glyphs = [FontAtlasTomlTestData.CreateDocument().Glyphs[0] with { U1 = 0.9 }] };
        AssertCode(document, FontAtlasTomlDiagnosticCode.UvMismatch);
    }

    [Fact]
    public void FontAtlasTomlLoader_RestoresPlacementFields()
    {
        FontAtlasTomlLoadResult result = FontAtlasTomlLoader.LoadString(FontAtlasTomlWriter.Write(FontAtlasTomlTestData.CreateDocument()));

        GlyphAtlasEntry glyph = result.Snapshot!.Glyphs.Values.Single();
        Assert.Equal(1d, glyph.Placement.PlaneLeft);
        Assert.Equal(-34d, glyph.Placement.PlaneTop);
        Assert.Equal(41d, glyph.Placement.PlaneRight);
        Assert.Equal(10d, glyph.Placement.PlaneBottom);
        Assert.Equal(4d, glyph.Placement.PixelRange);
        Assert.Equal(1d, glyph.Placement.ProjectionScale);
    }

    private static void AssertCode(FontAtlasTomlDocument document, FontAtlasTomlDiagnosticCode code)
    {
        FontAtlasTomlLoadResult result = FontAtlasTomlLoader.LoadString(FontAtlasTomlWriter.Write(document));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
    }
}
