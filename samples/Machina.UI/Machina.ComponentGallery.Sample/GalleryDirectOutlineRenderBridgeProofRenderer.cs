using Machina.Fonts;
using Machina.Fonts.Generation.Typography;
using Machina.Fonts.ReferenceRendering;
using Machina.Layout.Documents;
using Machina.Renderer.Raster.Dominatus.Models;
using Machina.Renderer.Raster.Surface;
using RasterRgba32 = Machina.Renderer.Raster.Colors.Rgba32;

namespace Machina.ComponentGallery.Sample;

public static class GalleryDirectOutlineRenderBridgeProofRenderer
{
    private static readonly FontFaceId CrimsonFace = new("CrimsonText-Regular");
    private static readonly Rgba32 CanvasBackground = new(15, 23, 42, 255);
    private static readonly Rgba32 PanelFill = new(17, 24, 39, 255);
    private static readonly Rgba32 PanelStroke = new(71, 85, 105, 255);
    private static readonly Rgba32 Foreground = new(248, 250, 252, 255);
    private static readonly Rgba32 MutedForeground = new(203, 213, 225, 255);
    private static readonly Rgba32 ContentRectStroke = new(34, 197, 94, 255);
    private static readonly Rgba32 InkStroke = new(244, 114, 182, 255);
    private static readonly Rgba32 BaselineStroke = new(59, 130, 246, 255);
    private static readonly Rgba32 Transparent = new(0, 0, 0, 0);

    public static GalleryDirectOutlineRenderBridgeProofPlacement BlitProof(
        RasterFrame frame,
        ResolvedLayoutDocument resolved)
    {
        if (!GalleryDirectOutlineRenderBridgeProofLayout.TryGetProofImageSlotRect(resolved, out var proofRect))
        {
            throw new InvalidOperationException("Direct-outline render bridge proof slot was not found in the resolved gallery layout.");
        }

        if (!GalleryDirectOutlineRenderBridgeProofLayout.TryGetAlignmentGridImageSlotRect(resolved, out var alignmentGridRect))
        {
            throw new InvalidOperationException("Direct-outline render bridge layout grid slot was not found in the resolved gallery layout.");
        }

        var proofPlacement = ToPlacement(proofRect);
        var gridPlacement = ToPlacement(alignmentGridRect);
        RgbaImage proofImage = RenderProofImageAsync(proofPlacement.Width, proofPlacement.Height).GetAwaiter().GetResult();
        RgbaImage alignmentGridImage = RenderAlignmentGridImageAsync(gridPlacement.Width, gridPlacement.Height).GetAwaiter().GetResult();

        Blit(frame.Surface, proofImage, proofPlacement.X, proofPlacement.Y);
        Blit(frame.Surface, alignmentGridImage, gridPlacement.X, gridPlacement.Y);

        return new GalleryDirectOutlineRenderBridgeProofPlacement(
            proofPlacement.X,
            proofPlacement.Y,
            proofPlacement.Width,
            proofPlacement.Height,
            gridPlacement.X,
            gridPlacement.Y,
            gridPlacement.Width,
            gridPlacement.Height);
    }

    private static async Task<RgbaImage> RenderProofImageAsync(int width, int height)
    {
        DirectOutlineStaticTextRenderBridge bridge = CreateBridge();
        RgbaImage canvas = CreateFilledImage(width, height, CanvasBackground);

        await DrawCaptionAsync(bridge, canvas, "Label", 16, 18);
        await DrawTextBoxAsync(bridge, canvas, "Settings", new DirectOutlineRect(24, 46, 248, 54), 18, StaticTextHorizontalAlignment.Left, StaticTextVerticalAlignment.Middle);

        await DrawCaptionAsync(bridge, canvas, "Centered Button", 16, 126);
        await DrawTextBoxAsync(bridge, canvas, "Save changes", new DirectOutlineRect(24, 154, 240, 56), 18, StaticTextHorizontalAlignment.Center, StaticTextVerticalAlignment.Middle);

        await DrawCaptionAsync(bridge, canvas, "Settings Row", 16, 230);
        await DrawSettingsRowAsync(bridge, canvas, new DirectOutlineRect(24, 258, 812, 58), "Settings", "Save changes");

        await DrawCaptionAsync(bridge, canvas, "Card Title / Body", 16, 344);
        await DrawCardAsync(
            bridge,
            canvas,
            new DirectOutlineRect(24, 372, 392, 262),
            "Settings",
            "Direct outline static text\nThe quick brown fox jumps over the lazy dog.");
        await DrawCardAsync(
            bridge,
            canvas,
            new DirectOutlineRect(444, 372, 392, 262),
            "Body Text",
            "Save changes\nCancel\nThe quick brown fox jumps over the lazy dog.");

        await DrawCaptionAsync(bridge, canvas, "Clipped Long Label", 16, 662);
        await DrawTextBoxAsync(
            bridge,
            canvas,
            "Extremely long settings label that should clip",
            new DirectOutlineRect(24, 690, 392, 56),
            18,
            StaticTextHorizontalAlignment.Left,
            StaticTextVerticalAlignment.Middle,
            clipMode: StaticTextClipMode.ClipToContentRect,
            showBounds: true);
        await DrawTextBoxAsync(
            bridge,
            canvas,
            "Extremely long settings label that should clip",
            new DirectOutlineRect(444, 690, 392, 56),
            18,
            StaticTextHorizontalAlignment.Left,
            StaticTextVerticalAlignment.Middle,
            clipMode: StaticTextClipMode.None,
            showBounds: true);

        return canvas;
    }

    private static async Task<RgbaImage> RenderAlignmentGridImageAsync(int width, int height)
    {
        DirectOutlineStaticTextRenderBridge bridge = CreateBridge();
        RgbaImage canvas = CreateFilledImage(width, height, CanvasBackground);
        DrawGrid(canvas, 20, 20, width - 40, height - 40, 20, new Rgba32(30, 41, 59, 255));

        StaticTextHorizontalAlignment[] horizontalAlignments =
        [
            StaticTextHorizontalAlignment.Left,
            StaticTextHorizontalAlignment.Center,
            StaticTextHorizontalAlignment.Right,
        ];
        StaticTextVerticalAlignment[] verticalAlignments =
        [
            StaticTextVerticalAlignment.Top,
            StaticTextVerticalAlignment.Middle,
            StaticTextVerticalAlignment.Bottom,
            StaticTextVerticalAlignment.Baseline,
        ];

        int cellWidth = 268;
        int cellHeight = 40;
        int originX = 26;
        int originY = 28;

        for (int row = 0; row < verticalAlignments.Length; row++)
        {
            for (int column = 0; column < horizontalAlignments.Length; column++)
            {
                await DrawTextBoxAsync(
                    bridge,
                    canvas,
                    "Settings",
                    new DirectOutlineRect(
                        originX + (column * 278),
                        originY + (row * 46),
                        cellWidth,
                        cellHeight),
                    16,
                    horizontalAlignments[column],
                    verticalAlignments[row],
                    showBounds: true,
                    showBaselines: true);
            }
        }

        return canvas;
    }

    private static async Task DrawSettingsRowAsync(
        DirectOutlineStaticTextRenderBridge bridge,
        RgbaImage canvas,
        DirectOutlineRect rowRect,
        string label,
        string value)
    {
        DrawPanel(canvas, rowRect);
        DrawHorizontalLine(canvas, rowRect.Left + 1d, rowRect.Right - 1d, rowRect.Top + (rowRect.Height / 2d), new Rgba32(30, 41, 59, 255));

        await DrawTextBoxAsync(
            bridge,
            canvas,
            label,
            new DirectOutlineRect(rowRect.X + 12d, rowRect.Y + 8d, 370d, rowRect.Height - 16d),
            18d,
            StaticTextHorizontalAlignment.Left,
            StaticTextVerticalAlignment.Baseline,
            panelFill: Transparent,
            panelStroke: Transparent,
            showBaselines: true);
        await DrawTextBoxAsync(
            bridge,
            canvas,
            value,
            new DirectOutlineRect(rowRect.Right - 250d, rowRect.Y + 8d, 238d, rowRect.Height - 16d),
            18d,
            StaticTextHorizontalAlignment.Right,
            StaticTextVerticalAlignment.Baseline,
            panelFill: Transparent,
            panelStroke: Transparent,
            showBaselines: true);
    }

    private static async Task DrawCardAsync(
        DirectOutlineStaticTextRenderBridge bridge,
        RgbaImage canvas,
        DirectOutlineRect cardRect,
        string title,
        string body)
    {
        DrawPanel(canvas, cardRect);

        await DrawTextBoxAsync(
            bridge,
            canvas,
            title,
            new DirectOutlineRect(cardRect.X + 18d, cardRect.Y + 18d, cardRect.Width - 36d, 40d),
            22d,
            StaticTextHorizontalAlignment.Left,
            StaticTextVerticalAlignment.Top,
            panelFill: Transparent,
            panelStroke: Transparent);
        await DrawTextBoxAsync(
            bridge,
            canvas,
            body,
            new DirectOutlineRect(cardRect.X + 18d, cardRect.Y + 72d, cardRect.Width - 36d, cardRect.Height - 90d),
            16d,
            StaticTextHorizontalAlignment.Left,
            StaticTextVerticalAlignment.Top,
            panelFill: Transparent,
            panelStroke: Transparent);
    }

    private static async Task DrawCaptionAsync(
        DirectOutlineStaticTextRenderBridge bridge,
        RgbaImage canvas,
        string text,
        double x,
        double y)
    {
        await RenderTextAsync(
            bridge,
            canvas,
            new StaticTextRenderRequest(
                text,
                CrimsonFace,
                new DirectOutlineRect(x, y, 240d, 20d),
                14d,
                DirectOutlineTextPadding.Zero,
                StaticTextHorizontalAlignment.Left,
                StaticTextVerticalAlignment.Top,
                StaticTextLineHeightMode.FontMetrics,
                ExplicitLineHeight: null,
                StaticTextClipMode.None),
            MutedForeground,
            Transparent,
            Transparent);
    }

    private static async Task DrawTextBoxAsync(
        DirectOutlineStaticTextRenderBridge bridge,
        RgbaImage canvas,
        string text,
        DirectOutlineRect outerRect,
        double fontSize,
        StaticTextHorizontalAlignment horizontalAlignment,
        StaticTextVerticalAlignment verticalAlignment,
        StaticTextClipMode clipMode = StaticTextClipMode.None,
        Rgba32? panelFill = null,
        Rgba32? panelStroke = null,
        bool showBounds = false,
        bool showBaselines = false)
    {
        DrawPanel(canvas, outerRect, panelFill ?? PanelFill, panelStroke ?? PanelStroke);

        StaticTextRenderResult result = await RenderTextAsync(
            bridge,
            canvas,
            new StaticTextRenderRequest(
                text,
                CrimsonFace,
                outerRect,
                fontSize,
                new DirectOutlineTextPadding(12d, 8d, 12d, 8d),
                horizontalAlignment,
                verticalAlignment,
                StaticTextLineHeightMode.FontMetrics,
                ExplicitLineHeight: null,
                clipMode,
                UsePairAdjustments: true,
                Supersample: 4,
                DebugLabel: text),
            Foreground,
            Transparent,
            Transparent,
            showBaselines);

        if (showBounds)
        {
            StrokeRect(canvas, OffsetRect(result.Layout.ContentRect, outerRect), ContentRectStroke);
            if (result.InkBounds is not null)
            {
                StrokeRect(canvas, OffsetRect(result.InkBounds, outerRect), InkStroke);
            }
        }
    }

    private static async Task<StaticTextRenderResult> RenderTextAsync(
        DirectOutlineStaticTextRenderBridge bridge,
        RgbaImage canvas,
        StaticTextRenderRequest request,
        Rgba32 foreground,
        Rgba32 background,
        Rgba32 panelStroke,
        bool showBaselines = false)
    {
        StaticTextRenderResult result = await bridge.RenderAsync(request, foreground, background);
        CompositeImage(canvas, result.Image, request.Rect, background);

        if (!panelStroke.Equals(Transparent))
        {
            StrokeRect(canvas, request.Rect, panelStroke);
        }

        if (showBaselines)
        {
            foreach (DirectOutlineLineLayout line in result.Layout.Lines)
            {
                DrawHorizontalLine(
                    canvas,
                    request.Rect.X + result.Layout.ContentRect.Left,
                    request.Rect.X + result.Layout.ContentRect.Right,
                    request.Rect.Y + line.BaselineY,
                    BaselineStroke);
            }
        }

        return result;
    }

    private static DirectOutlineStaticTextRenderBridge CreateBridge()
    {
        TypographyGlyphOutlineSource source = new(new Dictionary<FontFaceId, TypographyFontFaceSource>
        {
            [CrimsonFace] = new(CrimsonFace, GalleryFontFixturePaths.ResolveCrimsonTextPath()),
        });

        return new DirectOutlineStaticTextRenderBridge(source);
    }

    private static DirectOutlineRect OffsetRect(DirectOutlineRect rect, DirectOutlineRect offset)
    {
        return new DirectOutlineRect(
            offset.X + rect.X,
            offset.Y + rect.Y,
            rect.Width,
            rect.Height);
    }

    private static void DrawPanel(
        RgbaImage canvas,
        DirectOutlineRect rect,
        Rgba32? fill = null,
        Rgba32? stroke = null)
    {
        FillRect(canvas, rect, fill ?? PanelFill);
        StrokeRect(canvas, rect, stroke ?? PanelStroke);
    }

    private static void FillRect(RgbaImage canvas, DirectOutlineRect rect, Rgba32 color)
    {
        if (color.Equals(Transparent))
        {
            return;
        }

        int left = Math.Max(0, (int)Math.Floor(rect.Left));
        int top = Math.Max(0, (int)Math.Floor(rect.Top));
        int right = Math.Min(canvas.Width - 1, (int)Math.Ceiling(rect.Right) - 1);
        int bottom = Math.Min(canvas.Height - 1, (int)Math.Ceiling(rect.Bottom) - 1);

        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                canvas.SetPixel(x, y, color);
            }
        }
    }

    private static void StrokeRect(RgbaImage canvas, DirectOutlineRect rect, Rgba32 color)
    {
        if (color.Equals(Transparent))
        {
            return;
        }

        DrawHorizontalLine(canvas, rect.Left, rect.Right, rect.Top, color);
        DrawHorizontalLine(canvas, rect.Left, rect.Right, rect.Bottom - 1d, color);
        DrawVerticalLine(canvas, rect.Top, rect.Bottom, rect.Left, color);
        DrawVerticalLine(canvas, rect.Top, rect.Bottom, rect.Right - 1d, color);
    }

    private static void DrawHorizontalLine(RgbaImage canvas, double x0, double x1, double y, Rgba32 color)
    {
        int row = Clamp((int)Math.Round(y), 0, canvas.Height - 1);
        int left = Clamp((int)Math.Floor(Math.Min(x0, x1)), 0, canvas.Width - 1);
        int right = Clamp((int)Math.Ceiling(Math.Max(x0, x1)) - 1, 0, canvas.Width - 1);

        for (int x = left; x <= right; x++)
        {
            canvas.SetPixel(x, row, color);
        }
    }

    private static void DrawVerticalLine(RgbaImage canvas, double y0, double y1, double x, Rgba32 color)
    {
        int column = Clamp((int)Math.Round(x), 0, canvas.Width - 1);
        int top = Clamp((int)Math.Floor(Math.Min(y0, y1)), 0, canvas.Height - 1);
        int bottom = Clamp((int)Math.Ceiling(Math.Max(y0, y1)) - 1, 0, canvas.Height - 1);

        for (int y = top; y <= bottom; y++)
        {
            canvas.SetPixel(column, y, color);
        }
    }

    private static void DrawGrid(RgbaImage canvas, int x, int y, int width, int height, int step, Rgba32 color)
    {
        for (int column = x; column <= x + width; column += step)
        {
            DrawVerticalLine(canvas, y, y + height, column, color);
        }

        for (int row = y; row <= y + height; row += step)
        {
            DrawHorizontalLine(canvas, x, x + width, row, color);
        }
    }

    private static void CompositeImage(RgbaImage target, RgbaImage source, DirectOutlineRect destinationRect, Rgba32 transparentColor)
    {
        int destinationX = (int)Math.Round(destinationRect.X);
        int destinationY = (int)Math.Round(destinationRect.Y);

        for (int y = 0; y < source.Height; y++)
        {
            int targetY = destinationY + y;
            if ((uint)targetY >= (uint)target.Height)
            {
                continue;
            }

            for (int x = 0; x < source.Width; x++)
            {
                int targetX = destinationX + x;
                if ((uint)targetX >= (uint)target.Width)
                {
                    continue;
                }

                Rgba32 pixel = source.GetPixel(x, y);
                if (pixel.A == 0 || pixel.Equals(transparentColor))
                {
                    continue;
                }

                target.SetPixel(targetX, targetY, pixel);
            }
        }
    }

    private static RgbaImage CreateFilledImage(int width, int height, Rgba32 color)
    {
        RgbaImage image = new(width, height);

        for (int index = 0; index < image.Pixels.Length; index++)
        {
            image.Pixels[index] = color;
        }

        return image;
    }

    private static void Blit(RasterSurface target, RgbaImage source, int targetX, int targetY)
    {
        for (int y = 0; y < source.Height; y++)
        {
            int destinationY = targetY + y;
            if ((uint)destinationY >= (uint)target.Height)
            {
                continue;
            }

            for (int x = 0; x < source.Width; x++)
            {
                int destinationX = targetX + x;
                if ((uint)destinationX >= (uint)target.Width)
                {
                    continue;
                }

                Rgba32 pixel = source.GetPixel(x, y);
                target.SetPixel(destinationX, destinationY, new RasterRgba32(pixel.R, pixel.G, pixel.B, pixel.A));
            }
        }
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Min(max, Math.Max(min, value));
    }

    private static (int X, int Y, int Width, int Height) ToPlacement(Machina.Layout.Geometry.Rect rect)
    {
        return (
            (int)Math.Round(rect.X),
            (int)Math.Round(rect.Y),
            Math.Max(1, (int)Math.Round(rect.Width)),
            Math.Max(1, (int)Math.Round(rect.Height)));
    }
}
