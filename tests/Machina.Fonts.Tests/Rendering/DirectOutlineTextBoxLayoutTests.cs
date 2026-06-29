using Machina.Fonts.Generation.Typography;
using Machina.Fonts.ReferenceRendering;
using Machina.Fonts.Tests.Generation.Typography;
using Xunit;

namespace Machina.Fonts.Tests.Rendering;

public sealed class DirectOutlineTextBoxLayoutTests
{
    private static readonly Rgba32 Background = new(15, 23, 42, 255);
    private static readonly Rgba32 Foreground = new(248, 250, 252, 255);

    [Fact]
    public async Task DirectOutlineTextBoxLayout_ComputesContentRectFromPadding()
    {
        DirectOutlineTextBoxLayouter layouter = CreateLayouter();

        DirectOutlineTextBoxLayoutResult result = await layouter.LayoutAsync(
            CreateOptions("Settings") with
            {
                OuterRect = new DirectOutlineRect(10, 20, 200, 80),
                Padding = new DirectOutlineTextPadding(8, 6, 10, 12),
            });

        Assert.Equal(18d, result.ContentRect.X);
        Assert.Equal(26d, result.ContentRect.Y);
        Assert.Equal(182d, result.ContentRect.Width);
        Assert.Equal(62d, result.ContentRect.Height);
    }

    [Fact]
    public async Task DirectOutlineTextBoxLayout_UsesFontMetricsLineHeightByDefault()
    {
        TypographyGlyphOutlineSource source = TypographyKerningFixtureFont.CreateSource();
        DirectOutlineTextBoxLayouter layouter = new(source);

        DirectOutlineTextBoxLayoutResult result = await layouter.LayoutAsync(CreateOptions("Settings\nCancel"));
        DirectOutlineFontMetricsLoadResult metrics = await source.LoadFontMetricsAsync(TypographyKerningFixtureFont.Face, 18d);

        Assert.True(metrics.Success);
        Assert.Equal(metrics.Metrics!.LineHeight, result.Lines[0].LineHeight, 6);
        Assert.Equal(result.Lines[0].BaselineY + metrics.Metrics.LineHeight, result.Lines[1].BaselineY, 6);
    }

    [Fact]
    public async Task DirectOutlineTextBoxLayout_UsesExplicitLineHeight()
    {
        DirectOutlineTextBoxLayouter layouter = CreateLayouter();

        DirectOutlineTextBoxLayoutResult result = await layouter.LayoutAsync(
            CreateOptions("Settings\nCancel") with
            {
                LineHeightMode = DirectOutlineLineHeightMode.Explicit,
                ExplicitLineHeight = 32d,
            });

        Assert.Equal(32d, result.Lines[0].LineHeight, 6);
        Assert.Equal(result.Lines[0].BaselineY + 32d, result.Lines[1].BaselineY, 6);
    }

    [Fact]
    public async Task DirectOutlineTextBoxLayout_ComputesInkBounds()
    {
        DirectOutlineTextBoxLayouter layouter = CreateLayouter();

        DirectOutlineTextBoxLayoutResult result = await layouter.LayoutAsync(CreateOptions("The quick brown fox jumps over the lazy dog."));

        Assert.NotNull(result.InkBounds);
        Assert.True(result.InkBounds!.Width > 0d);
        Assert.True(result.InkBounds.Height > 0d);
    }

    [Fact]
    public async Task DirectOutlineTextBoxLayout_LeftAlignsLine()
    {
        DirectOutlineTextBoxLayouter layouter = CreateLayouter();
        DirectOutlineTextBoxLayoutResult result = await layouter.LayoutAsync(
            CreateOptions("Settings") with { HorizontalAlignment = DirectOutlineHorizontalAlignment.Left });

        Assert.Equal(result.ContentRect.X, result.Lines[0].X, 6);
    }

    [Fact]
    public async Task DirectOutlineTextBoxLayout_CenterAlignsLine()
    {
        DirectOutlineTextBoxLayouter layouter = CreateLayouter();
        DirectOutlineTextBoxLayoutResult result = await layouter.LayoutAsync(
            CreateOptions("Settings") with { HorizontalAlignment = DirectOutlineHorizontalAlignment.Center });

        double expected = result.ContentRect.X + ((result.ContentRect.Width - result.Lines[0].Width) / 2d);
        Assert.Equal(expected, result.Lines[0].X, 6);
    }

    [Fact]
    public async Task DirectOutlineTextBoxLayout_RightAlignsLine()
    {
        DirectOutlineTextBoxLayouter layouter = CreateLayouter();
        DirectOutlineTextBoxLayoutResult result = await layouter.LayoutAsync(
            CreateOptions("Settings") with { HorizontalAlignment = DirectOutlineHorizontalAlignment.Right });

        double expected = result.ContentRect.Right - result.Lines[0].Width;
        Assert.Equal(expected, result.Lines[0].X, 6);
    }

    [Fact]
    public async Task DirectOutlineTextBoxLayout_TopAlignsBlock()
    {
        TypographyGlyphOutlineSource source = TypographyKerningFixtureFont.CreateSource();
        DirectOutlineTextBoxLayouter layouter = new(source);
        DirectOutlineTextBoxLayoutResult result = await layouter.LayoutAsync(
            CreateOptions("Settings\nCancel") with { VerticalAlignment = DirectOutlineVerticalAlignment.Top });

        Assert.Equal(result.ContentRect.Top + result.FontMetrics.Ascent, result.Lines[0].BaselineY, 6);
    }

    [Fact]
    public async Task DirectOutlineTextBoxLayout_MiddleAlignsBlock()
    {
        DirectOutlineTextBoxLayouter layouter = CreateLayouter();
        DirectOutlineTextBoxLayoutResult result = await layouter.LayoutAsync(
            CreateOptions("Settings\nCancel") with { VerticalAlignment = DirectOutlineVerticalAlignment.Middle });

        double blockHeight = result.FontMetrics.Ascent + result.FontMetrics.Descent + result.Lines[0].LineHeight;
        double expected = result.ContentRect.Top + ((result.ContentRect.Height - blockHeight) / 2d) + result.FontMetrics.Ascent;
        Assert.Equal(expected, result.Lines[0].BaselineY, 6);
    }

    [Fact]
    public async Task DirectOutlineTextBoxLayout_BottomAlignsBlock()
    {
        DirectOutlineTextBoxLayouter layouter = CreateLayouter();
        DirectOutlineTextBoxLayoutResult result = await layouter.LayoutAsync(
            CreateOptions("Settings\nCancel") with { VerticalAlignment = DirectOutlineVerticalAlignment.Bottom });

        double blockHeight = result.FontMetrics.Ascent + result.FontMetrics.Descent + result.Lines[0].LineHeight;
        double expected = result.ContentRect.Bottom - blockHeight + result.FontMetrics.Ascent;
        Assert.Equal(expected, result.Lines[0].BaselineY, 6);
    }

    [Fact]
    public async Task DirectOutlineTextBoxLayout_BaselineModeIsDocumentedAndStable()
    {
        DirectOutlineTextBoxLayouter layouter = CreateLayouter();

        DirectOutlineTextBoxLayoutResult baseline = await layouter.LayoutAsync(
            CreateOptions("Settings") with { VerticalAlignment = DirectOutlineVerticalAlignment.Baseline });
        DirectOutlineTextBoxLayoutResult top = await layouter.LayoutAsync(
            CreateOptions("Settings") with { VerticalAlignment = DirectOutlineVerticalAlignment.Top });

        Assert.Equal(top.Lines[0].BaselineY, baseline.Lines[0].BaselineY, 6);
    }

    [Fact]
    public async Task DirectOutlineTextBoxLayout_SplitsExplicitNewlines()
    {
        DirectOutlineTextBoxLayouter layouter = CreateLayouter();
        DirectOutlineTextBoxLayoutResult result = await layouter.LayoutAsync(CreateOptions("Settings\nSave changes\nCancel"));

        Assert.Equal(3, result.Lines.Count);
        Assert.Equal("Settings", result.Lines[0].Text);
        Assert.Equal("Save changes", result.Lines[1].Text);
        Assert.Equal("Cancel", result.Lines[2].Text);
    }

    [Fact]
    public async Task DirectOutlineTextBoxLayout_ComputesMultipleBaselines()
    {
        DirectOutlineTextBoxLayouter layouter = CreateLayouter();
        DirectOutlineTextBoxLayoutResult result = await layouter.LayoutAsync(CreateOptions("Settings\nCancel"));

        Assert.Equal(result.Lines[0].BaselineY + result.Lines[0].LineHeight, result.Lines[1].BaselineY, 6);
    }

    [Fact]
    public async Task DirectOutlineTextBoxLayout_DoesNotWordWrapUnlessEnabled()
    {
        DirectOutlineTextBoxLayouter layouter = CreateLayouter();
        DirectOutlineTextBoxLayoutResult result = await layouter.LayoutAsync(
            CreateOptions("The quick brown fox jumps over the lazy dog.") with
            {
                OuterRect = new DirectOutlineRect(0, 0, 120, 60),
            });

        Assert.Single(result.Lines);
    }

    [Fact]
    public async Task DirectOutlineTextBoxRender_ClipToContentRectClipsPixels()
    {
        DirectOutlineTextBoxRenderer renderer = CreateRenderer();
        DirectOutlineTextBoxRenderResult result = await renderer.RenderAsync(
            new DirectOutlineTextBoxRenderOptions(
                CreateOptions("Extremely long settings label that should clip") with
                {
                    OuterRect = new DirectOutlineRect(20, 12, 180, 52),
                    ClipMode = DirectOutlineTextClipMode.ClipToContentRect,
                },
                240,
                80,
                Foreground,
                Background));

        Assert.Equal(0, CountInkOutsideContent(result));
        Assert.True(result.Layout.WasClipped);
    }

    [Fact]
    public async Task DirectOutlineTextBoxRender_NoneAllowsInkOutsideContentRect()
    {
        DirectOutlineTextBoxRenderer renderer = CreateRenderer();
        DirectOutlineTextBoxRenderResult result = await renderer.RenderAsync(
            new DirectOutlineTextBoxRenderOptions(
                CreateOptions("Extremely long settings label that should clip") with
                {
                    OuterRect = new DirectOutlineRect(20, 12, 180, 52),
                    ClipMode = DirectOutlineTextClipMode.None,
                },
                240,
                80,
                Foreground,
                Background));

        Assert.True(CountInkOutsideContent(result) > 0);
        Assert.False(result.Layout.WasClipped);
    }

    [Fact]
    public async Task DirectOutlineTextBoxLayout_ReportsWasClipped()
    {
        DirectOutlineTextBoxLayouter layouter = CreateLayouter();
        DirectOutlineTextBoxLayoutResult result = await layouter.LayoutAsync(
            CreateOptions("Extremely long settings label that should clip") with
            {
                OuterRect = new DirectOutlineRect(20, 12, 180, 52),
                ClipMode = DirectOutlineTextClipMode.ClipToContentRect,
            });

        Assert.True(result.WasClipped);
    }

    private static DirectOutlineTextBoxLayouter CreateLayouter()
    {
        TypographyGlyphOutlineSource source = TypographyKerningFixtureFont.CreateSource();
        return new DirectOutlineTextBoxLayouter(source);
    }

    private static DirectOutlineTextBoxRenderer CreateRenderer()
    {
        TypographyGlyphOutlineSource source = TypographyKerningFixtureFont.CreateSource();
        return new DirectOutlineTextBoxRenderer(source);
    }

    private static DirectOutlineTextBoxOptions CreateOptions(string text)
    {
        return new DirectOutlineTextBoxOptions(
            text,
            TypographyKerningFixtureFont.Face,
            18d,
            new DirectOutlineRect(0, 0, 260, 120),
            new DirectOutlineTextPadding(12, 10, 12, 10),
            DirectOutlineHorizontalAlignment.Left,
            DirectOutlineVerticalAlignment.Top,
            DirectOutlineLineHeightMode.FontMetrics,
            ExplicitLineHeight: null,
            DirectOutlineTextClipMode.None,
            UsePairAdjustments: true,
            Supersample: 4);
    }

    private static int CountInkOutsideContent(DirectOutlineTextBoxRenderResult result)
    {
        int left = (int)Math.Floor(result.Layout.ContentRect.Left);
        int top = (int)Math.Floor(result.Layout.ContentRect.Top);
        int right = (int)Math.Ceiling(result.Layout.ContentRect.Right) - 1;
        int bottom = (int)Math.Ceiling(result.Layout.ContentRect.Bottom) - 1;
        int count = 0;

        for (int y = 0; y < result.Image.Height; y++)
        {
            for (int x = 0; x < result.Image.Width; x++)
            {
                bool inside = x >= left && x <= right && y >= top && y <= bottom;
                if (inside)
                {
                    continue;
                }

                if (!result.Image.GetPixel(x, y).Equals(Background))
                {
                    count++;
                }
            }
        }

        return count;
    }
}
