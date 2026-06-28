using Xunit;
using Machina.Fonts.Toml;

namespace Machina.Fonts.Tests.Toml;

public sealed class FontAtlasTomlValidatorTests
{
    [Fact]
    public void Validator_RejectsNegativeDimensions()
    {
        FontAtlasTomlDocument document = FontAtlasTomlTestData.CreateDocument() with { Pages = [FontAtlasTomlTestData.CreateDocument().Pages[0] with { Width = -1 }] };
        Assert.Contains(FontAtlasTomlValidator.Validate(document), diagnostic => diagnostic.Code == FontAtlasTomlDiagnosticCode.InvalidValue);
    }

    [Fact]
    public void Validator_RejectsInvalidGlyphKey()
    {
        FontAtlasTomlDocument document = FontAtlasTomlTestData.CreateDocument() with { Glyphs = [FontAtlasTomlTestData.CreateDocument().Glyphs[0] with { Weight = 999 }] };
        Assert.Contains(FontAtlasTomlValidator.Validate(document), diagnostic => diagnostic.Code == FontAtlasTomlDiagnosticCode.InvalidGlyphKey);
    }

    [Fact]
    public void Validator_RejectsInvalidMetrics()
    {
        FontAtlasTomlDocument document = FontAtlasTomlTestData.CreateDocument() with { Metrics = FontAtlasTomlTestData.CreateDocument().Metrics with { LineHeight = 0 } };
        Assert.Contains(FontAtlasTomlValidator.Validate(document), diagnostic => diagnostic.KeyPath == "metrics.line_height");
    }

    [Fact]
    public void Validator_ReportsHashMissing()
    {
        FontAtlasTomlDocument document = FontAtlasTomlTestData.CreateDocument() with { Pages = [FontAtlasTomlTestData.CreateDocument().Pages[0] with { ContentHash = string.Empty }] };
        Assert.Contains(FontAtlasTomlValidator.Validate(document), diagnostic => diagnostic.Code == FontAtlasTomlDiagnosticCode.HashMissing);
    }
}
