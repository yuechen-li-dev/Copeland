using System.Security.Cryptography;
using Machina.Fonts.AvaloniaOracle;
using Machina.Fonts.Generation;
using Machina.Fonts.Generation.Typography;
using Machina.Fonts.ReferenceRendering;
using Xunit;

namespace Machina.Fonts.Tests.Rendering;

[Collection("Avalonia text oracle")]
public sealed class PositionedOutlineConformanceTests
{
    private static readonly FontFaceId Face = new("CrimsonText-Regular");

    [Theory]
    [InlineData(64)]
    [InlineData(96)]
    [InlineData(128)]
    public async Task ExactFontSingleGlyphOutlines_CoincideInComparisonSpace(int size)
    {
        AvaloniaTextReferenceRun reference = CreateGeometryReference("q", size);
        TypographyGlyphOutlineSource source = CreateTypographySource();
        GlyphOutlineLoadResult loaded = await source.LoadGlyphOutlineAsync(
            Face,
            'q',
            new GlyphOutlineLoadOptions(size, 0, GlyphHintingMode.None, normalizeToEm: true));

        Assert.True(loaded.Success);
        Assert.NotNull(loaded.Outline);
        ushort glyphId = source.GetGlyphId(Face, 'q');
        PositionedGlyphOutline actual = PositionedOutlineGeometry.FromTypography(
            glyphId,
            new MachinaTextSpan(0, 1),
            loaded.Outline!,
            reference.Glyphs[0].OriginX,
            reference.Glyphs[0].OriginY,
            size / (double)source.GetFaceFacts(Face).UnitsPerEm);
        OutlineComparisonResult comparison = PositionedOutlineGeometry.Compare(reference.Outlines[0], actual);

        Assert.Equal(reference.Glyphs[0].GlyphId, glyphId);
        Assert.True(comparison.HausdorffDistance < size * 0.00015d);
        Assert.True(Math.Abs(actual.Bounds.Width - reference.Outlines[0].Bounds.Width) < size * 0.0001d);
        Assert.True(Math.Abs(actual.Bounds.Height - reference.Outlines[0].Bounds.Height) < size * 0.0001d);
    }

    [Fact]
    public async Task TypographySpaceAdvance_UsesTrueTypeRepeatedHorizontalMetric()
    {
        TypographyGlyphOutlineSource source = CreateTypographySource();
        GlyphOutlineLoadResult loaded = await source.LoadGlyphOutlineAsync(
            Face,
            ' ',
            new GlyphOutlineLoadOptions(64, 0, GlyphHintingMode.None, normalizeToEm: true));

        Assert.True(loaded.Success);
        Assert.NotNull(loaded.Outline);
        Assert.Equal(14.3125d, loaded.Outline!.Metrics.Advance, 8);
    }

    [Fact]
    public void TranslationAndScaleFits_SeparatePlacementFromGeometry()
    {
        PositionedGlyphOutline reference = Rectangle(0d, 0d, 10d, 20d);
        PositionedGlyphOutline translated = Rectangle(3d, -4d, 10d, 20d);
        PositionedGlyphOutline scaled = Rectangle(3d, -4d, 12d, 24d);

        OutlineComparisonResult translationComparison = PositionedOutlineGeometry.Compare(reference, translated);
        OutlineComparisonResult scaleComparison = PositionedOutlineGeometry.Compare(reference, scaled);

        Assert.True(translationComparison.TranslationOnly.MaximumResidual < 0.000001d);
        Assert.True(scaleComparison.TranslationAndUniformScale.MaximumResidual < 0.000001d);
        Assert.True(scaleComparison.TranslationOnly.MaximumResidual > 1d);
        Assert.Equal(10d / 12d, scaleComparison.TranslationAndUniformScale.ScaleX, 8);
    }

    [Fact]
    public void GeometryOracle_PreservesSubpixelPlacementAndDoesNotRasterize()
    {
        AvaloniaTextReferenceRun reference = CreateGeometryReference("The quick", 64);

        Assert.Empty(reference.RasterPath);
        Assert.Equal(1, reference.RasterImage.Width);
        Assert.Contains(reference.Glyphs, static glyph => glyph.OriginX != Math.Truncate(glyph.OriginX));
        Assert.All(reference.Outlines, static outline => Assert.True(double.IsFinite(outline.Bounds.Left)));
    }

    [Fact]
    public void ExactFontIdentityAndProductionDependencyBoundary_AreStable()
    {
        AvaloniaTextReferenceRun reference = CreateGeometryReference("A", 64);
        string expectedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(FontPath))).ToLowerInvariant();

        Assert.Equal(expectedHash, reference.Font.Sha256);
        Assert.Equal(0, reference.Font.FaceIndex);
        Assert.Equal(1024, reference.Font.UnitsPerEm);

        string root = FindRepositoryRoot();
        string[] productionProjects =
        [
            "src/Machina.UI/Machina.Core/Machina.Core.csproj",
            "src/Machina.UI/Machina.Layout/Machina.Layout.csproj",
            "src/Machina.UI/Machina.Fonts/Machina.Fonts.csproj",
        ];
        foreach (string relativePath in productionProjects)
        {
            string project = File.ReadAllText(Path.Combine(root, relativePath));
            Assert.DoesNotContain("Avalonia", project, StringComparison.Ordinal);
            Assert.DoesNotContain("SkiaSharp", project, StringComparison.Ordinal);
        }
    }

    private static PositionedGlyphOutline Rectangle(double x, double y, double width, double height)
    {
        GlyphPoint p0 = new(x, y);
        GlyphPoint p1 = new(x + width, y);
        GlyphPoint p2 = new(x + width, y + height);
        GlyphPoint p3 = new(x, y + height);
        GlyphContour contour = new([
            new GlyphLineSegment(p0, p1),
            new GlyphLineSegment(p1, p2),
            new GlyphLineSegment(p2, p3),
            new GlyphLineSegment(p3, p0),
        ]);
        return PositionedOutlineGeometry.FromComparisonSpace(
            1,
            new MachinaTextSpan(0, 1),
            [contour],
            1d,
            0d,
            0d,
            x,
            y,
            y,
            "test");
    }

    private static AvaloniaTextReferenceRun CreateGeometryReference(string text, int size)
    {
        return new AvaloniaTextOracle().CreateGeometryReference(new AvaloniaTextReferenceRequest(
            FontPath,
            text,
            size,
            new DirectOutlineRect(12d, 0d, 1800d, 240d)));
    }

    private static TypographyGlyphOutlineSource CreateTypographySource()
    {
        return new TypographyGlyphOutlineSource(new Dictionary<FontFaceId, TypographyFontFaceSource>
        {
            [Face] = new(Face, FontPath),
        });
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Machina.UI.slnx"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string FontPath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", "CrimsonText-Regular.ttf");
}
