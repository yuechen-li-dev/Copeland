using System.Security.Cryptography;
using Machina.Fonts.AvaloniaOracle;
using Machina.Fonts.ReferenceRendering;
using Xunit;

namespace Machina.Fonts.Tests.Rendering;

[Collection("Avalonia text oracle")]
public sealed class AvaloniaTextOracleTests
{
    [Fact]
    public void Oracle_UsesExactFontBytesAndExposesShapedGeometry()
    {
        string outputPath = CreateOutputPath("identity");
        AvaloniaTextReferenceRun reference = CreateReference("Hello Machina", 32d, outputPath);

        using FileStream stream = File.OpenRead(FontPath);
        string expectedHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

        Assert.Equal(expectedHash, reference.Font.Sha256);
        Assert.Equal("Crimson Text", reference.Font.FamilyName);
        Assert.Equal(1024, reference.Font.UnitsPerEm);
        Assert.True(reference.Availability.GlyphIds);
        Assert.True(reference.Availability.GlyphClusters);
        Assert.NotEmpty(reference.Glyphs);
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public void Oracle_ReportsFirstVisibleGlyphAsTokenAnchor()
    {
        AvaloniaTextReferenceRun reference = CreateReference("Hello, world!", 32d, CreateOutputPath("tokens"));
        AvaloniaReferenceToken hello = reference.Tokens.Single(static token => token.Text == "Hello");
        AvaloniaReferenceToken whitespace = reference.Tokens.Single(static token => token.Kind == MachinaTextTokenKind.Whitespace);
        AvaloniaReferenceToken world = reference.Tokens.Single(static token => token.Text == "world");

        Assert.NotNull(hello.AnchorGlyphIndex);
        Assert.Equal(reference.Glyphs[hello.AnchorGlyphIndex!.Value].OriginX, hello.AnchorOriginX);
        Assert.Null(whitespace.AnchorGlyphIndex);
        Assert.NotNull(world.AnchorOriginX);
        Assert.True(world.AnchorOriginX > hello.AnchorOriginX);
    }

    [Fact]
    public void Oracle_CoordinateTransformPreservesRequestedOriginAndBaselineProgression()
    {
        AvaloniaTextReferenceRun reference = CreateReference("Ag\nAg", 24d, CreateOutputPath("multiline"));

        Assert.Equal(2, reference.Lines.Count);
        Assert.True(reference.Lines[0].Baseline > 11d);
        Assert.Equal(reference.Lines[0].Height, reference.Lines[1].Baseline - reference.Lines[0].Baseline, 6);
        Assert.True(reference.Tokens[0].AnchorOriginX >= 13d);
    }

    [Fact]
    public void ProductionFontProject_HasNoAvaloniaDependency()
    {
        string projectPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "Machina.UI",
            "Machina.Fonts",
            "Machina.Fonts.csproj"));
        string project = File.ReadAllText(projectPath);

        Assert.DoesNotContain("Avalonia", project, StringComparison.Ordinal);
    }

    private static AvaloniaTextReferenceRun CreateReference(string text, double size, string outputPath)
    {
        AvaloniaTextOracle oracle = new();
        return oracle.CreateReference(
            new AvaloniaTextReferenceRequest(
                FontPath,
                text,
                size,
                new DirectOutlineRect(13d, 11d, 760d, 150d)),
            outputPath);
    }

    private static string CreateOutputPath(string name)
    {
        return Path.Combine(Path.GetTempPath(), "machina-text-conformance-m0-tests", name + ".png");
    }

    private static string FontPath => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "Fonts",
        "CrimsonText-Regular.ttf");
}
