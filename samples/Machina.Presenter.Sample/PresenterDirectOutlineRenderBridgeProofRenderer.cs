using Machina.Fonts;
using Machina.Fonts.Generation.Typography;
using Machina.Fonts.ReferenceRendering;
using Machina.Layout.Documents;
using Machina.Renderer.Raster.Dominatus.Models;
using Machina.Renderer.Raster.Surface;
using RasterRgba32 = Machina.Renderer.Raster.Colors.Rgba32;

namespace Machina.Presenter.Sample;

public static class PresenterDirectOutlineRenderBridgeProofRenderer
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

    public static PresenterDirectOutlineRenderBridgeProofPlacement BlitProof(
        RasterFrame frame,
        ResolvedLayoutDocument resolved)
    {
        if (!PresenterDirectOutlineRenderBridgeProofLayout.TryGetProofImageSlotRect(resolved, out var proofRect))
        {
            throw new InvalidOperationException("Presenter direct-outline proof slot was not found in the resolved layout.");
        }

        if (!PresenterDirectOutlineRenderBridgeProofLayout.TryGetAlignmentGridImageSlotRect(resolved, out var alignmentRect))
        {
            throw new InvalidOperationException("Presenter direct-outline alignment grid slot was not found in the resolved layout.");
        }

        var proofPlacement = ToPlacement(proofRect);
        var gridPlacement = ToPlacement(alignmentRect);
        PresenterDirectOutlineRenderBridgeProofRenderResult rendered = RenderStandaloneAsync(
            proofPlacement.Width,
            proofPlacement.Height,
            gridPlacement.Width,
            gridPlacement.Height).GetAwaiter().GetResult();

        Blit(frame.Surface, rendered.ProofImage, proofPlacement.X, proofPlacement.Y);
        Blit(frame.Surface, rendered.AlignmentGridImage, gridPlacement.X, gridPlacement.Y);

        return new PresenterDirectOutlineRenderBridgeProofPlacement(
            proofPlacement.X,
            proofPlacement.Y,
            proofPlacement.Width,
            proofPlacement.Height,
            gridPlacement.X,
            gridPlacement.Y,
            gridPlacement.Width,
            gridPlacement.Height);
    }

    public static async Task<PresenterDirectOutlineRenderBridgeProofRenderResult> RenderStandaloneAsync(
        int proofWidth,
        int proofHeight,
        int alignmentGridWidth,
        int alignmentGridHeight)
    {
        DirectOutlineStaticTextRenderBridge bridge = CreateBridge();
        RgbaImage proofImage = CreateFilledImage(proofWidth, proofHeight, CanvasBackground);
        RgbaImage alignmentGridImage = CreateFilledImage(alignmentGridWidth, alignmentGridHeight, CanvasBackground);
        List<PresenterDirectOutlineRenderBridgeProofCaseRender> proofCases = [];
        List<PresenterDirectOutlineRenderBridgeProofCaseRender> alignmentCases = [];

        proofCases.Add(await DrawTextAsync(
            bridge,
            proofImage,
            "header-title",
            "Machina Presenter",
            new DirectOutlineRect(16d, 14d, proofWidth - 32d, 26d),
            22d,
            StaticTextHorizontalAlignment.Left,
            StaticTextVerticalAlignment.Top));
        proofCases.Add(await DrawTextAsync(
            bridge,
            proofImage,
            "header-backend",
            "DirectOutlineStatic",
            new DirectOutlineRect(16d, 40d, 190d, 18d),
            14d,
            StaticTextHorizontalAlignment.Left,
            StaticTextVerticalAlignment.Top,
            foreground: MutedForeground));
        proofCases.Add(await DrawTextAsync(
            bridge,
            proofImage,
            "header-proof",
            "Render bridge proof",
            new DirectOutlineRect(16d, 58d, 190d, 18d),
            14d,
            StaticTextHorizontalAlignment.Left,
            StaticTextVerticalAlignment.Top,
            foreground: MutedForeground));
        proofCases.Add(await DrawTextAsync(
            bridge,
            proofImage,
            "status-reference",
            "Static/reference backend",
            new DirectOutlineRect(proofWidth - 198d, 18d, 182d, 20d),
            13d,
            StaticTextHorizontalAlignment.Right,
            StaticTextVerticalAlignment.Top));
        proofCases.Add(await DrawTextAsync(
            bridge,
            proofImage,
            "status-msdf",
            "MSDF experimental remains opt-in",
            new DirectOutlineRect(proofWidth - 220d, 42d, 204d, 18d),
            12d,
            StaticTextHorizontalAlignment.Right,
            StaticTextVerticalAlignment.Top,
            foreground: MutedForeground));

        proofCases.Add(await DrawTextAsync(
            bridge,
            proofImage,
            "presenter-title",
            "Presenter settings",
            new DirectOutlineRect(16d, 86d, proofWidth - 32d, 28d),
            20d,
            StaticTextHorizontalAlignment.Left,
            StaticTextVerticalAlignment.Middle,
            drawBounds: true));

        DrawPanel(proofImage, new DirectOutlineRect(16d, 122d, proofWidth - 32d, 40d));
        proofCases.Add(await DrawTextAsync(
            bridge,
            proofImage,
            "row-label",
            "Email updates",
            new DirectOutlineRect(28d, 128d, 180d, 28d),
            16d,
            StaticTextHorizontalAlignment.Left,
            StaticTextVerticalAlignment.Baseline,
            panelFill: Transparent,
            panelStroke: Transparent,
            showBaselines: true));
        proofCases.Add(await DrawTextAsync(
            bridge,
            proofImage,
            "row-value",
            "Enabled",
            new DirectOutlineRect(proofWidth - 156d, 128d, 128d, 28d),
            16d,
            StaticTextHorizontalAlignment.Right,
            StaticTextVerticalAlignment.Baseline,
            panelFill: Transparent,
            panelStroke: Transparent,
            showBaselines: true));

        DrawPanel(proofImage, new DirectOutlineRect(16d, 174d, 176d, 44d));
        proofCases.Add(await DrawTextAsync(
            bridge,
            proofImage,
            "button-label",
            "Save changes",
            new DirectOutlineRect(16d, 174d, 176d, 44d),
            16d,
            StaticTextHorizontalAlignment.Center,
            StaticTextVerticalAlignment.Middle,
            panelFill: Transparent,
            panelStroke: Transparent));

        DrawPanel(proofImage, new DirectOutlineRect(206d, 174d, proofWidth - 222d, 116d));
        proofCases.Add(await DrawTextAsync(
            bridge,
            proofImage,
            "card-title",
            "Status card",
            new DirectOutlineRect(220d, 186d, proofWidth - 250d, 24d),
            18d,
            StaticTextHorizontalAlignment.Left,
            StaticTextVerticalAlignment.Top,
            panelFill: Transparent,
            panelStroke: Transparent));
        proofCases.Add(await DrawTextAsync(
            bridge,
            proofImage,
            "card-body",
            "Render bridge proof\nStatic/reference backend",
            new DirectOutlineRect(220d, 216d, proofWidth - 250d, 60d),
            14d,
            StaticTextHorizontalAlignment.Left,
            StaticTextVerticalAlignment.Top,
            panelFill: Transparent,
            panelStroke: Transparent,
            drawBounds: true));

        DrawPanel(proofImage, new DirectOutlineRect(16d, 232d, proofWidth - 32d, 44d));
        proofCases.Add(await DrawTextAsync(
            bridge,
            proofImage,
            "clipped-label",
            "Very long presenter label that should clip before it escapes the row",
            new DirectOutlineRect(16d, 232d, proofWidth - 32d, 44d),
            15d,
            StaticTextHorizontalAlignment.Left,
            StaticTextVerticalAlignment.Middle,
            clipMode: StaticTextClipMode.ClipToContentRect,
            drawBounds: true));

        DrawPanel(proofImage, new DirectOutlineRect(16d, 288d, proofWidth - 32d, proofHeight - 304d));
        proofCases.Add(await DrawTextAsync(
            bridge,
            proofImage,
            "multiline-body",
            "Presenter-style sample\nExplicit newline body\nStatic/reference path",
            new DirectOutlineRect(16d, 288d, proofWidth - 32d, proofHeight - 304d),
            15d,
            StaticTextHorizontalAlignment.Left,
            StaticTextVerticalAlignment.Top,
            panelFill: Transparent,
            panelStroke: Transparent,
            drawBounds: true));

        DrawGrid(alignmentGridImage, 12, 16, alignmentGridWidth - 24, alignmentGridHeight - 28, 16, new Rgba32(30, 41, 59, 255));
        double cellWidth = Math.Floor((alignmentGridWidth - 48d) / 3d);

        alignmentCases.Add(await DrawTextAsync(
            bridge,
            alignmentGridImage,
            "alignment-left",
            "Aligned label",
            new DirectOutlineRect(16d, 44d, cellWidth, 44d),
            15d,
            StaticTextHorizontalAlignment.Left,
            StaticTextVerticalAlignment.Middle,
            drawBounds: true,
            showBaselines: true));
        alignmentCases.Add(await DrawTextAsync(
            bridge,
            alignmentGridImage,
            "alignment-center",
            "Aligned label",
            new DirectOutlineRect(24d + cellWidth, 44d, cellWidth, 44d),
            15d,
            StaticTextHorizontalAlignment.Center,
            StaticTextVerticalAlignment.Middle,
            drawBounds: true,
            showBaselines: true));
        alignmentCases.Add(await DrawTextAsync(
            bridge,
            alignmentGridImage,
            "alignment-right",
            "Aligned label",
            new DirectOutlineRect(32d + (cellWidth * 2d), 44d, cellWidth, 44d),
            15d,
            StaticTextHorizontalAlignment.Right,
            StaticTextVerticalAlignment.Middle,
            drawBounds: true,
            showBaselines: true));

        alignmentCases.Add(await DrawTextAsync(
            bridge,
            alignmentGridImage,
            "alignment-caption-left",
            "Left",
            new DirectOutlineRect(16d, 18d, cellWidth, 18d),
            12d,
            StaticTextHorizontalAlignment.Left,
            StaticTextVerticalAlignment.Top,
            foreground: MutedForeground));
        alignmentCases.Add(await DrawTextAsync(
            bridge,
            alignmentGridImage,
            "alignment-caption-center",
            "Center",
            new DirectOutlineRect(24d + cellWidth, 18d, cellWidth, 18d),
            12d,
            StaticTextHorizontalAlignment.Center,
            StaticTextVerticalAlignment.Top,
            foreground: MutedForeground));
        alignmentCases.Add(await DrawTextAsync(
            bridge,
            alignmentGridImage,
            "alignment-caption-right",
            "Right",
            new DirectOutlineRect(32d + (cellWidth * 2d), 18d, cellWidth, 18d),
            12d,
            StaticTextHorizontalAlignment.Right,
            StaticTextVerticalAlignment.Top,
            foreground: MutedForeground));

        return new PresenterDirectOutlineRenderBridgeProofRenderResult(
            proofImage,
            alignmentGridImage,
            proofCases,
            alignmentCases);
    }

    private static async Task<PresenterDirectOutlineRenderBridgeProofCaseRender> DrawTextAsync(
        DirectOutlineStaticTextRenderBridge bridge,
        RgbaImage canvas,
        string caseId,
        string text,
        DirectOutlineRect rect,
        double fontSize,
        StaticTextHorizontalAlignment horizontalAlignment,
        StaticTextVerticalAlignment verticalAlignment,
        StaticTextClipMode clipMode = StaticTextClipMode.None,
        Rgba32? foreground = null,
        Rgba32? panelFill = null,
        Rgba32? panelStroke = null,
        bool drawBounds = false,
        bool showBaselines = false)
    {
        if (panelFill is not null || panelStroke is not null)
        {
            DrawPanel(canvas, rect, panelFill ?? PanelFill, panelStroke ?? PanelStroke);
        }

        StaticTextRenderRequest request = new(
            text,
            CrimsonFace,
            rect,
            fontSize,
            new DirectOutlineTextPadding(10d, 6d, 10d, 6d),
            horizontalAlignment,
            verticalAlignment,
            StaticTextLineHeightMode.FontMetrics,
            ExplicitLineHeight: null,
            clipMode,
            UsePairAdjustments: true,
            Supersample: 4,
            DebugLabel: caseId);

        StaticTextRenderResult result = await bridge.RenderAsync(
            request,
            foreground ?? Foreground,
            Transparent);

        CompositeImage(canvas, result.Image, rect, Transparent);

        if (showBaselines)
        {
            foreach (DirectOutlineLineLayout line in result.Layout.Lines)
            {
                DrawHorizontalLine(
                    canvas,
                    rect.X + result.Layout.ContentRect.Left,
                    rect.X + result.Layout.ContentRect.Right,
                    rect.Y + line.BaselineY,
                    BaselineStroke);
            }
        }

        if (drawBounds)
        {
            StrokeRect(canvas, OffsetRect(result.Layout.ContentRect, rect), ContentRectStroke);
            if (result.InkBounds is not null)
            {
                StrokeRect(canvas, OffsetRect(result.InkBounds, rect), InkStroke);
            }
        }

        return new PresenterDirectOutlineRenderBridgeProofCaseRender(caseId, result);
    }

    private static DirectOutlineStaticTextRenderBridge CreateBridge()
    {
        TypographyGlyphOutlineSource source = new(new Dictionary<FontFaceId, TypographyFontFaceSource>
        {
            [CrimsonFace] = new(CrimsonFace, PresenterFontFixturePaths.ResolveCrimsonTextPath()),
        });

        return new DirectOutlineStaticTextRenderBridge(source);
    }

    private static DirectOutlineRect OffsetRect(DirectOutlineRect rect, DirectOutlineRect offset)
    {
        return new DirectOutlineRect(offset.X + rect.X, offset.Y + rect.Y, rect.Width, rect.Height);
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

public sealed record PresenterDirectOutlineRenderBridgeProofCaseRender(
    string CaseId,
    StaticTextRenderResult RenderResult);

public sealed record PresenterDirectOutlineRenderBridgeProofRenderResult(
    RgbaImage ProofImage,
    RgbaImage AlignmentGridImage,
    IReadOnlyList<PresenterDirectOutlineRenderBridgeProofCaseRender> ProofCases,
    IReadOnlyList<PresenterDirectOutlineRenderBridgeProofCaseRender> AlignmentCases);
