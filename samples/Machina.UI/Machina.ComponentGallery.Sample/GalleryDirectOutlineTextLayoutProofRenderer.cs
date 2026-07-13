using Machina.Fonts;
using Machina.Fonts.Generation.Typography;
using Machina.Fonts.ReferenceRendering;
using Machina.Layout.Documents;
using RasterRgba32 = Machina.ComponentGallery.Sample.SampleRgba32;

namespace Machina.ComponentGallery.Sample;

public static class GalleryDirectOutlineTextLayoutProofRenderer
{
    private static readonly FontFaceId CrimsonFace = new("CrimsonText-Regular");
    private static readonly Rgba32 CanvasBackground = new(15, 23, 42, 255);
    private static readonly Rgba32 PanelFill = new(17, 24, 39, 255);
    private static readonly Rgba32 PanelStroke = new(71, 85, 105, 255);
    private static readonly Rgba32 Foreground = new(248, 250, 252, 255);
    private static readonly Rgba32 MutedForeground = new(203, 213, 225, 255);
    private static readonly Rgba32 Accent = new(56, 189, 248, 255);
    private static readonly Rgba32 ContentRectStroke = new(34, 197, 94, 255);
    private static readonly Rgba32 InkStroke = new(244, 114, 182, 255);
    private static readonly Rgba32 BaselineStroke = new(59, 130, 246, 255);
    private static readonly Rgba32 Transparent = new(0, 0, 0, 0);

    public static GalleryDirectOutlineTextLayoutProofPlacement BlitProof(
        RasterFrame frame,
        ResolvedLayoutDocument resolved)
    {
        if (!GalleryDirectOutlineTextLayoutProofLayout.TryGetProofImageSlotRect(resolved, out var proofRect))
        {
            throw new InvalidOperationException("Direct-outline text layout proof slot was not found in the resolved gallery layout.");
        }

        if (!GalleryDirectOutlineTextLayoutProofLayout.TryGetAlignmentGridImageSlotRect(resolved, out var alignmentGridRect))
        {
            throw new InvalidOperationException("Direct-outline text alignment grid slot was not found in the resolved gallery layout.");
        }

        var proofPlacement = ToPlacement(proofRect);
        var gridPlacement = ToPlacement(alignmentGridRect);
        RgbaImage proofImage = RenderProofImageAsync(proofPlacement.Width, proofPlacement.Height).GetAwaiter().GetResult();
        RgbaImage alignmentGridImage = RenderAlignmentGridImageAsync(gridPlacement.Width, gridPlacement.Height).GetAwaiter().GetResult();

        Blit(frame.Surface, proofImage, proofPlacement.X, proofPlacement.Y);
        Blit(frame.Surface, alignmentGridImage, gridPlacement.X, gridPlacement.Y);

        return new GalleryDirectOutlineTextLayoutProofPlacement(
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
        DirectOutlineTextBoxRenderer renderer = CreateRenderer();
        RgbaImage canvas = CreateFilledImage(width, height, CanvasBackground);

        await DrawCaptionAsync(renderer, canvas, "Labels", 16, 18);
        await DrawTextBoxAsync(renderer, canvas, "Settings", new DirectOutlineRect(24, 46, 248, 54), 18, DirectOutlineHorizontalAlignment.Left, DirectOutlineVerticalAlignment.Middle);
        await DrawTextBoxAsync(renderer, canvas, "Settings", new DirectOutlineRect(306, 46, 248, 54), 18, DirectOutlineHorizontalAlignment.Center, DirectOutlineVerticalAlignment.Middle);
        await DrawTextBoxAsync(renderer, canvas, "Settings", new DirectOutlineRect(588, 46, 248, 54), 18, DirectOutlineHorizontalAlignment.Right, DirectOutlineVerticalAlignment.Middle);

        await DrawCaptionAsync(renderer, canvas, "Buttons", 16, 126);
        await DrawTextBoxAsync(renderer, canvas, "Save changes", new DirectOutlineRect(24, 154, 178, 48), 16, DirectOutlineHorizontalAlignment.Center, DirectOutlineVerticalAlignment.Middle);
        await DrawTextBoxAsync(renderer, canvas, "Save changes", new DirectOutlineRect(230, 146, 240, 56), 18, DirectOutlineHorizontalAlignment.Center, DirectOutlineVerticalAlignment.Middle);
        await DrawTextBoxAsync(renderer, canvas, "Cancel", new DirectOutlineRect(506, 138, 330, 64), 22, DirectOutlineHorizontalAlignment.Center, DirectOutlineVerticalAlignment.Middle);

        await DrawCaptionAsync(renderer, canvas, "Settings Rows", 16, 230);
        await DrawSettingsRowAsync(renderer, canvas, new DirectOutlineRect(24, 258, 812, 58), "Settings", "Save changes");
        await DrawSettingsRowAsync(renderer, canvas, new DirectOutlineRect(24, 326, 812, 58), "Direct outline static text", "Cancel");

        await DrawCaptionAsync(renderer, canvas, "Cards", 16, 412);
        await DrawCardAsync(
            renderer,
            canvas,
            new DirectOutlineRect(24, 440, 392, 262),
            "Settings",
            "Direct outline static text\nThe quick brown fox jumps over the lazy dog.");
        await DrawCardAsync(
            renderer,
            canvas,
            new DirectOutlineRect(444, 440, 392, 262),
            "Body Text",
            "Save changes\nCancel\nThe quick brown fox jumps over the lazy dog.");

        await DrawCaptionAsync(renderer, canvas, "Clipping", 16, 730);
        await DrawTextBoxAsync(
            renderer,
            canvas,
            "Extremely long settings label that should clip",
            new DirectOutlineRect(24, 758, 392, 56),
            18,
            DirectOutlineHorizontalAlignment.Left,
            DirectOutlineVerticalAlignment.Middle,
            clipMode: DirectOutlineTextClipMode.ClipToContentRect,
            showBounds: true);
        await DrawTextBoxAsync(
            renderer,
            canvas,
            "Extremely long settings label that should clip",
            new DirectOutlineRect(444, 758, 392, 56),
            18,
            DirectOutlineHorizontalAlignment.Left,
            DirectOutlineVerticalAlignment.Middle,
            clipMode: DirectOutlineTextClipMode.None,
            showBounds: true);

        return canvas;
    }

    private static async Task<RgbaImage> RenderAlignmentGridImageAsync(int width, int height)
    {
        DirectOutlineTextBoxRenderer renderer = CreateRenderer();
        RgbaImage canvas = CreateFilledImage(width, height, CanvasBackground);
        DrawGrid(canvas, 20, 20, width - 40, height - 40, 20, new Rgba32(30, 41, 59, 255));

        DirectOutlineHorizontalAlignment[] horizontalAlignments =
        [
            DirectOutlineHorizontalAlignment.Left,
            DirectOutlineHorizontalAlignment.Center,
            DirectOutlineHorizontalAlignment.Right,
        ];
        DirectOutlineVerticalAlignment[] verticalAlignments =
        [
            DirectOutlineVerticalAlignment.Top,
            DirectOutlineVerticalAlignment.Middle,
            DirectOutlineVerticalAlignment.Bottom,
            DirectOutlineVerticalAlignment.Baseline,
        ];

        int cellWidth = 268;
        int cellHeight = 40;
        int originX = 26;
        int originY = 28;

        for (int row = 0; row < verticalAlignments.Length; row++)
        {
            for (int column = 0; column < horizontalAlignments.Length; column++)
            {
                DirectOutlineRect rect = new(
                    originX + (column * 278),
                    originY + (row * 46),
                    cellWidth,
                    cellHeight);

                await DrawTextBoxAsync(
                    renderer,
                    canvas,
                    "Settings",
                    rect,
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
        DirectOutlineTextBoxRenderer renderer,
        RgbaImage canvas,
        DirectOutlineRect rowRect,
        string label,
        string value)
    {
        DrawPanel(canvas, rowRect);
        DrawHorizontalLine(canvas, rowRect.Left + 1, rowRect.Right - 1, rowRect.Top + (rowRect.Height / 2d), new Rgba32(30, 41, 59, 255));

        await DrawTextBoxAsync(
            renderer,
            canvas,
            label,
            new DirectOutlineRect(rowRect.X + 12, rowRect.Y + 8, 370, rowRect.Height - 16),
            18,
            DirectOutlineHorizontalAlignment.Left,
            DirectOutlineVerticalAlignment.Baseline,
            panelFill: Transparent,
            panelStroke: Transparent,
            showBaselines: true);
        await DrawTextBoxAsync(
            renderer,
            canvas,
            value,
            new DirectOutlineRect(rowRect.Right - 250, rowRect.Y + 8, 238, rowRect.Height - 16),
            18,
            DirectOutlineHorizontalAlignment.Right,
            DirectOutlineVerticalAlignment.Baseline,
            panelFill: Transparent,
            panelStroke: Transparent,
            showBaselines: true);
    }

    private static async Task DrawCardAsync(
        DirectOutlineTextBoxRenderer renderer,
        RgbaImage canvas,
        DirectOutlineRect cardRect,
        string title,
        string body)
    {
        DrawPanel(canvas, cardRect);

        await DrawTextBoxAsync(
            renderer,
            canvas,
            title,
            new DirectOutlineRect(cardRect.X + 18, cardRect.Y + 18, cardRect.Width - 36, 40),
            22,
            DirectOutlineHorizontalAlignment.Left,
            DirectOutlineVerticalAlignment.Top,
            panelFill: Transparent,
            panelStroke: Transparent);
        await DrawTextBoxAsync(
            renderer,
            canvas,
            body,
            new DirectOutlineRect(cardRect.X + 18, cardRect.Y + 72, cardRect.Width - 36, cardRect.Height - 90),
            16,
            DirectOutlineHorizontalAlignment.Left,
            DirectOutlineVerticalAlignment.Top,
            panelFill: Transparent,
            panelStroke: Transparent);
    }

    private static async Task DrawCaptionAsync(
        DirectOutlineTextBoxRenderer renderer,
        RgbaImage canvas,
        string text,
        double x,
        double y)
    {
        DirectOutlineTextBoxOptions layout = new(
            text,
            CrimsonFace,
            14d,
            new DirectOutlineRect(x, y, 240, 20),
            DirectOutlineTextPadding.Zero,
            DirectOutlineHorizontalAlignment.Left,
            DirectOutlineVerticalAlignment.Top,
            DirectOutlineLineHeightMode.FontMetrics,
            ExplicitLineHeight: null,
            DirectOutlineTextClipMode.None);
        await RenderTextAsync(renderer, canvas, layout, MutedForeground, Transparent, Transparent);
    }

    private static async Task DrawTextBoxAsync(
        DirectOutlineTextBoxRenderer renderer,
        RgbaImage canvas,
        string text,
        DirectOutlineRect outerRect,
        double fontSize,
        DirectOutlineHorizontalAlignment horizontalAlignment,
        DirectOutlineVerticalAlignment verticalAlignment,
        DirectOutlineTextClipMode clipMode = DirectOutlineTextClipMode.None,
        Rgba32? panelFill = null,
        Rgba32? panelStroke = null,
        bool showBounds = false,
        bool showBaselines = false)
    {
        DrawPanel(canvas, outerRect, panelFill ?? PanelFill, panelStroke ?? PanelStroke);

        DirectOutlineTextBoxOptions layout = new(
            text,
            CrimsonFace,
            fontSize,
            outerRect,
            new DirectOutlineTextPadding(12, 8, 12, 8),
            horizontalAlignment,
            verticalAlignment,
            DirectOutlineLineHeightMode.FontMetrics,
            ExplicitLineHeight: null,
            clipMode,
            UsePairAdjustments: true,
            Supersample: 4);

        DirectOutlineTextBoxRenderResult result = await RenderTextAsync(
            renderer,
            canvas,
            layout,
            Foreground,
            Transparent,
            Transparent,
            showBaselines);

        if (showBounds)
        {
            StrokeRect(canvas, result.Layout.ContentRect, ContentRectStroke);
            if (result.Layout.InkBounds is not null)
            {
                StrokeRect(canvas, result.Layout.InkBounds, InkStroke);
            }
        }
    }

    private static async Task<DirectOutlineTextBoxRenderResult> RenderTextAsync(
        DirectOutlineTextBoxRenderer renderer,
        RgbaImage canvas,
        DirectOutlineTextBoxOptions layout,
        Rgba32 foreground,
        Rgba32 background,
        Rgba32 panelStroke,
        bool showBaselines = false)
    {
        DirectOutlineTextBoxRenderResult result = await renderer.RenderAsync(
            new DirectOutlineTextBoxRenderOptions(
                layout,
                canvas.Width,
                canvas.Height,
                foreground,
                background,
                showBaselines,
                showBaselines ? BaselineStroke : null));
        CompositeImage(canvas, result.Image, background);

        if (!panelStroke.Equals(Transparent))
        {
            StrokeRect(canvas, layout.OuterRect, panelStroke);
        }

        if (showBaselines)
        {
            foreach (DirectOutlineLineLayout line in result.Layout.Lines)
            {
                DrawHorizontalLine(canvas, result.Layout.ContentRect.Left, result.Layout.ContentRect.Right, line.BaselineY, BaselineStroke);
            }
        }

        return result;
    }

    private static DirectOutlineTextBoxRenderer CreateRenderer()
    {
        TypographyGlyphOutlineSource source = new(new Dictionary<FontFaceId, TypographyFontFaceSource>
        {
            [CrimsonFace] = new(CrimsonFace, GalleryFontFixturePaths.ResolveCrimsonTextPath()),
        });

        return new DirectOutlineTextBoxRenderer(source);
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

    private static void CompositeImage(RgbaImage target, RgbaImage source, Rgba32 transparentColor)
    {
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                Rgba32 pixel = source.GetPixel(x, y);
                if (pixel.A == 0 || pixel.Equals(transparentColor))
                {
                    continue;
                }

                target.SetPixel(x, y, pixel);
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
