using Machina.Fonts.Generation.Typography;
using Machina.Fonts.ReferenceRendering;
using Machina.Fonts.Tests.Generation.Typography;
using Xunit;

namespace Machina.Fonts.Tests.Rendering;

public sealed class DirectOutlineStaticTextRenderBridgeTests
{
    private static readonly Rgba32 Background = new(15, 23, 42, 255);
    private static readonly Rgba32 Foreground = new(248, 250, 252, 255);

    [Fact]
    public void StaticTextRenderRequest_RejectsEmptyTextOrHandlesItStably()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => CreateRequest(string.Empty).Validate());
        Assert.Contains("at least one character", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StaticTextRenderRequest_RejectsNegativeRectSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => (CreateRequest("Settings") with
            {
                Rect = new DirectOutlineRect(0d, 0d, -1d, 40d),
            }).Validate());
    }

    [Fact]
    public void StaticTextRenderRequest_RejectsInvalidFontSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => (CreateRequest("Settings") with
            {
                FontSize = 0d,
            }).Validate());
    }

    [Fact]
    public void StaticTextRenderRequest_RejectsInvalidSupersample()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => (CreateRequest("Settings") with
            {
                Supersample = 3,
            }).Validate());
    }

    [Fact]
    public void DirectOutlineStaticTextRenderBridge_MapsPaddingToContentRect()
    {
        DirectOutlineStaticTextRenderBridge bridge = CreateBridge();

        DirectOutlineTextBoxOptions options = bridge.CreateLayoutOptions(
            CreateRequest("Settings") with
            {
                Rect = new DirectOutlineRect(24d, 16d, 180d, 52d),
                Padding = new DirectOutlineTextPadding(10d, 6d, 8d, 4d),
            });

        Assert.Equal(0d, options.OuterRect.X);
        Assert.Equal(0d, options.OuterRect.Y);
        Assert.Equal(180d, options.OuterRect.Width);
        Assert.Equal(52d, options.OuterRect.Height);
        Assert.Equal(10d, options.Padding.Left);
        Assert.Equal(6d, options.Padding.Top);
        Assert.Equal(8d, options.Padding.Right);
        Assert.Equal(4d, options.Padding.Bottom);
    }

    [Fact]
    public void DirectOutlineStaticTextRenderBridge_MapsHorizontalAlignment()
    {
        DirectOutlineStaticTextRenderBridge bridge = CreateBridge();

        DirectOutlineTextBoxOptions options = bridge.CreateLayoutOptions(
            CreateRequest("Settings") with
            {
                HorizontalAlignment = StaticTextHorizontalAlignment.Center,
            });

        Assert.Equal(DirectOutlineHorizontalAlignment.Center, options.HorizontalAlignment);
    }

    [Fact]
    public void DirectOutlineStaticTextRenderBridge_MapsVerticalAlignment()
    {
        DirectOutlineStaticTextRenderBridge bridge = CreateBridge();

        DirectOutlineTextBoxOptions options = bridge.CreateLayoutOptions(
            CreateRequest("Settings") with
            {
                VerticalAlignment = StaticTextVerticalAlignment.Bottom,
            });

        Assert.Equal(DirectOutlineVerticalAlignment.Bottom, options.VerticalAlignment);
    }

    [Fact]
    public void DirectOutlineStaticTextRenderBridge_MapsExplicitLineHeight()
    {
        DirectOutlineStaticTextRenderBridge bridge = CreateBridge();

        DirectOutlineTextBoxOptions options = bridge.CreateLayoutOptions(
            CreateRequest("Settings\nSave changes") with
            {
                LineHeightMode = StaticTextLineHeightMode.Explicit,
                ExplicitLineHeight = 30d,
            });

        Assert.Equal(DirectOutlineLineHeightMode.Explicit, options.LineHeightMode);
        Assert.Equal(30d, options.ExplicitLineHeight);
    }

    [Fact]
    public void DirectOutlineStaticTextRenderBridge_MapsClipMode()
    {
        DirectOutlineStaticTextRenderBridge bridge = CreateBridge();

        DirectOutlineTextBoxOptions options = bridge.CreateLayoutOptions(
            CreateRequest("Settings") with
            {
                ClipMode = StaticTextClipMode.ClipToContentRect,
            });

        Assert.Equal(DirectOutlineTextClipMode.ClipToContentRect, options.ClipMode);
    }

    [Fact]
    public async Task DirectOutlineStaticTextRenderBridge_RendersNonEmptyImage()
    {
        DirectOutlineStaticTextRenderBridge bridge = CreateBridge();
        StaticTextRenderResult result = await bridge.RenderAsync(CreateRequest("Machina"), Foreground, Background);

        Assert.True(CountForegroundPixels(result.Image) > 0);
    }

    [Fact]
    public async Task DirectOutlineStaticTextRenderBridge_ReturnsLayout()
    {
        DirectOutlineStaticTextRenderBridge bridge = CreateBridge();
        StaticTextRenderResult result = await bridge.RenderAsync(CreateRequest("Settings"), Foreground, Background);

        Assert.Equal(result.Request.Rect.Width, result.Layout.OuterRect.Width);
        Assert.Equal(result.Request.Rect.Height, result.Layout.OuterRect.Height);
        Assert.NotEmpty(result.Layout.Lines);
    }

    [Fact]
    public async Task DirectOutlineStaticTextRenderBridge_ReturnsGlyphPlacements()
    {
        DirectOutlineStaticTextRenderBridge bridge = CreateBridge();
        StaticTextRenderResult result = await bridge.RenderAsync(CreateRequest("Aa0"), Foreground, Background);

        Assert.NotEmpty(result.Glyphs);
        Assert.Equal(result.Layout.Glyphs.Count, result.Glyphs.Count);
    }

    [Fact]
    public async Task DirectOutlineStaticTextRenderBridge_ReportsClipping()
    {
        DirectOutlineStaticTextRenderBridge bridge = CreateBridge();
        StaticTextRenderResult result = await bridge.RenderAsync(
            CreateRequest("Extremely long settings label that should clip") with
            {
                Rect = new DirectOutlineRect(0d, 0d, 180d, 52d),
                ClipMode = StaticTextClipMode.ClipToContentRect,
            },
            Foreground,
            Background);

        Assert.True(result.WasClipped);
    }

    [Fact]
    public async Task DirectOutlineStaticTextRenderBridge_IsDeterministic()
    {
        DirectOutlineStaticTextRenderBridge bridge = CreateBridge();
        StaticTextRenderRequest request = CreateRequest("Hello Machina");

        StaticTextRenderResult first = await bridge.RenderAsync(request, Foreground, Background);
        StaticTextRenderResult second = await bridge.RenderAsync(request, Foreground, Background);

        Assert.Equal(Summarize(first.Layout), Summarize(second.Layout));
        Assert.Equal(first.Image.Pixels, second.Image.Pixels);
    }

    private static DirectOutlineStaticTextRenderBridge CreateBridge()
    {
        TypographyGlyphOutlineSource source = TypographyKerningFixtureFont.CreateSource();
        return new DirectOutlineStaticTextRenderBridge(source);
    }

    private static StaticTextRenderRequest CreateRequest(string text)
    {
        return new StaticTextRenderRequest(
            text,
            TypographyKerningFixtureFont.Face,
            new DirectOutlineRect(12d, 8d, 240d, 72d),
            18d,
            new DirectOutlineTextPadding(12d, 10d, 12d, 10d),
            StaticTextHorizontalAlignment.Left,
            StaticTextVerticalAlignment.Middle,
            StaticTextLineHeightMode.FontMetrics,
            ExplicitLineHeight: null,
            StaticTextClipMode.None,
            UsePairAdjustments: true,
            Supersample: 4,
            DebugLabel: "bridge-test");
    }

    private static int CountForegroundPixels(RgbaImage image)
    {
        int count = 0;

        for (int index = 0; index < image.Pixels.Length; index++)
        {
            if (!image.Pixels[index].Equals(Background))
            {
                count++;
            }
        }

        return count;
    }

    private static string Summarize(DirectOutlineTextBoxLayoutResult result)
    {
        return string.Join(
            "|",
            result.Lines.Select(line => $"{line.Text}:{line.X:F4}:{line.BaselineY:F4}:{line.Width:F4}"));
    }
}
