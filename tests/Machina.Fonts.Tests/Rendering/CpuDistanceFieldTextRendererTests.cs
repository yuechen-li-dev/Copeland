using Machina.Fonts.Generation;
using Machina.Fonts.ReferenceRendering;
using Xunit;

namespace Machina.Fonts.Tests.Rendering;

public sealed class CpuDistanceFieldTextRendererTests
{
    private static readonly Rgba32 Background = new(8, 8, 12, 255);
    private static readonly DistanceFieldTextRenderOptions Options = new(
        32,
        16,
        new FontFaceId("machina-reference-render"),
        32,
        MachinaFontWeight.Regular,
        MachinaFontSlant.Upright,
        DistanceFieldKind.Msdf,
        4,
        6,
        4d,
        Rgba32.White,
        Background,
        1d,
        10d);

    [Fact]
    public void RenderText_PlacesGlyphsUsingAdvance()
    {
        DistanceFieldPageReference page = CreateSolidPage();
        GlyphAtlasEntry entryA = CreateEntry('A', 0, 0);
        GlyphAtlasEntry entryB = CreateEntry('B', 4, 0);
        FontAtlasSnapshot snapshot = CreateSnapshot(entryA, entryB);
        DistanceFieldTextLayoutResult layout = CreateLayout("AB");

        RgbaImage image = CpuDistanceFieldTextRenderer.RenderText(
            snapshot,
            new Dictionary<int, DistanceFieldPageReference> { [0] = page },
            layout,
            Options);

        Assert.Contains(FindNonBackgroundPixels(image), point => point.X < 8);
        Assert.Contains(FindNonBackgroundPixels(image), point => point.X >= 8);
    }

    [Fact]
    public void RenderText_SkipsWhitespaceButAdvancesPen()
    {
        DistanceFieldPageReference page = CreateSolidPage();
        GlyphAtlasEntry entryA = CreateEntry('A', 0, 0);
        FontAtlasSnapshot snapshot = CreateSnapshot(entryA);
        DistanceFieldTextLayoutResult layout = CreateLayout("A A");

        RgbaImage image = CpuDistanceFieldTextRenderer.RenderText(
            snapshot,
            new Dictionary<int, DistanceFieldPageReference> { [0] = page },
            layout,
            Options);

        int minX = FindNonBackgroundPixels(image).Min(static point => point.X);
        int maxX = FindNonBackgroundPixels(image).Max(static point => point.X);

        Assert.Equal(1, minX);
        Assert.True(maxX >= 10);
    }

    [Fact]
    public void RenderText_ProducesNonBlankImage()
    {
        DistanceFieldPageReference page = CreateSolidPage();
        GlyphAtlasEntry entryA = CreateEntry('A', 0, 0);
        FontAtlasSnapshot snapshot = CreateSnapshot(entryA);
        DistanceFieldTextLayoutResult layout = CreateLayout("A");

        RgbaImage image = CpuDistanceFieldTextRenderer.RenderText(
            snapshot,
            new Dictionary<int, DistanceFieldPageReference> { [0] = page },
            layout,
            Options);

        Assert.Contains(image.Pixels, pixel => pixel != Background);
    }

    [Fact]
    public void RenderText_IsDeterministic()
    {
        DistanceFieldPageReference page = CreateSolidPage();
        GlyphAtlasEntry entryA = CreateEntry('A', 0, 0);
        GlyphAtlasEntry entryB = CreateEntry('B', 4, 0);
        FontAtlasSnapshot snapshot = CreateSnapshot(entryA, entryB);
        DistanceFieldTextLayoutResult layout = CreateLayout("AB");

        RgbaImage first = CpuDistanceFieldTextRenderer.RenderText(
            snapshot,
            new Dictionary<int, DistanceFieldPageReference> { [0] = page },
            layout,
            Options);
        RgbaImage second = CpuDistanceFieldTextRenderer.RenderText(
            snapshot,
            new Dictionary<int, DistanceFieldPageReference> { [0] = page },
            layout,
            Options);

        Assert.Equal(first.Pixels, second.Pixels);
    }

    [Fact]
    public void RenderText_RejectsMissingAtlasEntry()
    {
        DistanceFieldPageReference page = CreateSolidPage();
        GlyphAtlasEntry entryA = CreateEntry('A', 0, 0);
        FontAtlasSnapshot snapshot = CreateSnapshot(entryA);
        DistanceFieldTextLayoutResult layout = CreateLayout("AB");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => CpuDistanceFieldTextRenderer.RenderText(
            snapshot,
            new Dictionary<int, DistanceFieldPageReference> { [0] = page },
            layout,
            Options));

        Assert.Contains("U+0042", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderText_UsesBaselineAndBearing()
    {
        DistanceFieldPageReference page = CreateSolidPage();
        GlyphAtlasEntry entryA = CreateEntry('A', 0, 0, bearingY: 4);
        FontAtlasSnapshot snapshot = CreateSnapshot(entryA);
        DistanceFieldTextLayoutResult upperLayout = CreateLayout("A", baselineY: 6);
        DistanceFieldTextLayoutResult lowerLayout = CreateLayout("A", baselineY: 10);

        RgbaImage upper = CpuDistanceFieldTextRenderer.RenderText(
            snapshot,
            new Dictionary<int, DistanceFieldPageReference> { [0] = page },
            upperLayout,
            Options with { BaselineY = 6 });
        RgbaImage lower = CpuDistanceFieldTextRenderer.RenderText(
            snapshot,
            new Dictionary<int, DistanceFieldPageReference> { [0] = page },
            lowerLayout,
            Options with { BaselineY = 10 });

        int upperTop = FindNonBackgroundPixels(upper).Min(static point => point.Y);
        int lowerTop = FindNonBackgroundPixels(lower).Min(static point => point.Y);

        Assert.True(lowerTop > upperTop);
    }

    private static DistanceFieldTextLayoutResult CreateLayout(string text, double baselineY = 10d)
    {
        DistanceFieldTextRun run = DistanceFieldTextRun.Create(
            text,
            Options.Face,
            Options.EmSize,
            Options.Weight,
            Options.Slant);

        Dictionary<GlyphKey, GlyphMetrics> metrics = [];
        foreach (GlyphKey key in run.GlyphKeys)
        {
            metrics[key] = key.Codepoint == ' '
                ? new GlyphMetrics(5, 0, 0, 0, 0)
                : new GlyphMetrics(4, 0, 6, 4, 6);
        }

        return DistanceFieldTextLayout.Layout(
            run,
            metrics,
            Options with { BaselineY = baselineY });
    }

    private static FontAtlasSnapshot CreateSnapshot(params GlyphAtlasEntry[] entries)
    {
        return new FontAtlasSnapshot(
            1,
            [new FontAtlasPage(0, "synthetic.dfpage", 8, 6, null)],
            entries.ToDictionary(static entry => entry.Key, static entry => entry));
    }

    private static GlyphAtlasEntry CreateEntry(char value, int x, int y, double bearingY = 6)
    {
        GlyphKey key = GlyphKey.FromChar(Options.Face, value, Options.EmSize);
        GlyphMetrics metrics = new(4, 0, bearingY, 4, 6);
        return new GlyphAtlasEntry(
            key,
            0,
            x,
            y,
            4,
            6,
            x / 8d,
            y / 6d,
            (x + 4) / 8d,
            (y + 6) / 6d,
            metrics);
    }

    private static DistanceFieldPageReference CreateSolidPage()
    {
        return RenderingTestHelpers.CreatePage(
            DistanceFieldKind.Msdf,
            8,
            6,
            static (_, _) => [1f, 1f, 1f]);
    }

    private static IReadOnlyList<(int X, int Y)> FindNonBackgroundPixels(RgbaImage image)
    {
        List<(int X, int Y)> result = [];
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                if (image.GetPixel(x, y) != Background)
                {
                    result.Add((x, y));
                }
            }
        }

        return result;
    }
}
