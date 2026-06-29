using System.Text;
using Machina.Fonts.Generation;

namespace Machina.Fonts.ReferenceRendering;

public enum DirectOutlineHorizontalAlignment
{
    Left,
    Center,
    Right,
}

public enum DirectOutlineVerticalAlignment
{
    Top,
    Middle,
    Bottom,
    Baseline,
}

public enum DirectOutlineTextClipMode
{
    None,
    ClipToContentRect,
}

public enum DirectOutlineLineHeightMode
{
    FontMetrics,
    Explicit,
}

public sealed record DirectOutlineRect(
    double X,
    double Y,
    double Width,
    double Height)
{
    public double Left => X;

    public double Top => Y;

    public double Right => X + Width;

    public double Bottom => Y + Height;
}

public sealed record DirectOutlineTextPadding(
    double Left,
    double Top,
    double Right,
    double Bottom)
{
    public static DirectOutlineTextPadding Zero { get; } = new(0d, 0d, 0d, 0d);
}

public sealed record DirectOutlineFontMetrics(
    double UnitsPerEm,
    double Ascent,
    double Descent,
    double LineGap)
{
    public double LineHeight => Ascent + Descent + LineGap;
}

public sealed record DirectOutlineFontMetricsLoadResult(
    bool Success,
    DirectOutlineFontMetrics? Metrics,
    bool UsedFallbackMetrics,
    IReadOnlyList<FontGenerationDiagnostic> Diagnostics);

public interface IDirectOutlineFontMetricsSource
{
    ValueTask<DirectOutlineFontMetricsLoadResult> LoadFontMetricsAsync(
        FontFaceId face,
        double fontSize,
        CancellationToken cancellationToken = default);
}

public sealed record DirectOutlineTextBoxOptions(
    string Text,
    FontFaceId FontFaceId,
    double FontSize,
    DirectOutlineRect OuterRect,
    DirectOutlineTextPadding Padding,
    DirectOutlineHorizontalAlignment HorizontalAlignment,
    DirectOutlineVerticalAlignment VerticalAlignment,
    DirectOutlineLineHeightMode LineHeightMode,
    double? ExplicitLineHeight,
    DirectOutlineTextClipMode ClipMode,
    bool UsePairAdjustments = true,
    int Supersample = 4,
    MachinaFontWeight Weight = MachinaFontWeight.Regular,
    MachinaFontSlant Slant = MachinaFontSlant.Upright)
{
    public DirectOutlineTextBoxOptions Validate()
    {
        ArgumentNullException.ThrowIfNull(Text);

        if (!double.IsFinite(FontSize) || FontSize <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(FontSize));
        }

        ValidateRect(OuterRect, nameof(OuterRect));
        ValidatePadding(Padding, nameof(Padding));

        if (LineHeightMode == DirectOutlineLineHeightMode.Explicit)
        {
            if (ExplicitLineHeight is null || !double.IsFinite(ExplicitLineHeight.Value) || ExplicitLineHeight.Value <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(ExplicitLineHeight));
            }
        }
        else if (ExplicitLineHeight is not null && (!double.IsFinite(ExplicitLineHeight.Value) || ExplicitLineHeight.Value <= 0d))
        {
            throw new ArgumentOutOfRangeException(nameof(ExplicitLineHeight));
        }

        if (Supersample is not 1 and not 2 and not 4)
        {
            throw new ArgumentOutOfRangeException(nameof(Supersample), "Supported supersample levels are 1, 2, and 4.");
        }

        return this;
    }

    private static void ValidateRect(DirectOutlineRect rect, string name)
    {
        ArgumentNullException.ThrowIfNull(rect, name);

        if (!double.IsFinite(rect.X)
            || !double.IsFinite(rect.Y)
            || !double.IsFinite(rect.Width)
            || !double.IsFinite(rect.Height)
            || rect.Width < 0d
            || rect.Height < 0d)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static void ValidatePadding(DirectOutlineTextPadding padding, string name)
    {
        ArgumentNullException.ThrowIfNull(padding, name);

        if (!double.IsFinite(padding.Left)
            || !double.IsFinite(padding.Top)
            || !double.IsFinite(padding.Right)
            || !double.IsFinite(padding.Bottom)
            || padding.Left < 0d
            || padding.Top < 0d
            || padding.Right < 0d
            || padding.Bottom < 0d)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}

public sealed record DirectOutlineLineLayout(
    string Text,
    double X,
    double BaselineY,
    double Width,
    double Ascent,
    double Descent,
    double LineHeight,
    DirectOutlineRect? InkBounds);

public sealed record DirectOutlineTextBoxLayoutResult(
    DirectOutlineRect OuterRect,
    DirectOutlineRect ContentRect,
    DirectOutlineFontMetrics FontMetrics,
    IReadOnlyList<DirectOutlineLineLayout> Lines,
    IReadOnlyList<DirectOutlineGlyphRenderPlacement> Glyphs,
    DirectOutlineRect? InkBounds,
    bool WasClipped,
    bool UsedFallbackMetrics,
    IReadOnlyList<FontGenerationDiagnostic> Diagnostics);

public sealed record DirectOutlineTextBoxRenderOptions(
    DirectOutlineTextBoxOptions Layout,
    int OutputWidth,
    int OutputHeight,
    Rgba32 Foreground,
    Rgba32 Background,
    bool ShowBaselineGuides = false,
    Rgba32? BaselineGuideColor = null,
    OutlineFillRule FillRule = OutlineFillRule.EvenOdd,
    int CurveSubdivisionCount = 24)
{
    public DirectOutlineTextBoxRenderOptions Validate()
    {
        ArgumentNullException.ThrowIfNull(Layout);
        Layout.Validate();

        if (OutputWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OutputWidth));
        }

        if (OutputHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OutputHeight));
        }

        if (CurveSubdivisionCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(CurveSubdivisionCount));
        }

        if (ShowBaselineGuides && BaselineGuideColor is null)
        {
            throw new ArgumentException("Baseline guide color must be provided when baseline guides are enabled.", nameof(BaselineGuideColor));
        }

        return this;
    }
}

public sealed record DirectOutlineTextBoxRenderResult(
    DirectOutlineTextBoxLayoutResult Layout,
    RgbaImage Image,
    DirectOutlineRect? RenderedInkBounds,
    IReadOnlyList<FontGenerationDiagnostic> Diagnostics);

public sealed class DirectOutlineTextBoxLayouter
{
    private readonly IGlyphOutlineSource outlineSource;
    private readonly IGlyphPairAdjustmentSource? pairAdjustmentSource;
    private readonly IDirectOutlineFontMetricsSource? fontMetricsSource;

    public DirectOutlineTextBoxLayouter(
        IGlyphOutlineSource outlineSource,
        IGlyphPairAdjustmentSource? pairAdjustmentSource = null,
        IDirectOutlineFontMetricsSource? fontMetricsSource = null)
    {
        this.outlineSource = outlineSource ?? throw new ArgumentNullException(nameof(outlineSource));
        this.pairAdjustmentSource = pairAdjustmentSource ?? outlineSource as IGlyphPairAdjustmentSource;
        this.fontMetricsSource = fontMetricsSource ?? outlineSource as IDirectOutlineFontMetricsSource;
    }

    public async ValueTask<DirectOutlineTextBoxLayoutResult> LayoutAsync(
        DirectOutlineTextBoxOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        DirectOutlineTextBoxOptions validated = options.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        List<FontGenerationDiagnostic> diagnostics = [];
        DirectOutlineFontMetricsLoadResult metricsResult = await LoadFontMetricsAsync(validated, cancellationToken);
        diagnostics.AddRange(metricsResult.Diagnostics);

        if (!metricsResult.Success || metricsResult.Metrics is null)
        {
            return new DirectOutlineTextBoxLayoutResult(
                validated.OuterRect,
                ComputeContentRect(validated.OuterRect, validated.Padding),
                CreateFallbackMetrics(validated.FontSize),
                Array.Empty<DirectOutlineLineLayout>(),
                Array.Empty<DirectOutlineGlyphRenderPlacement>(),
                null,
                false,
                UsedFallbackMetrics: true,
                diagnostics);
        }

        DirectOutlineFontMetrics fontMetrics = metricsResult.Metrics;
        DirectOutlineRect contentRect = ComputeContentRect(validated.OuterRect, validated.Padding);
        string[] lines = SplitExplicitLines(validated.Text);

        Dictionary<GlyphKey, GlyphOutline> outlinesByGlyph = await LoadOutlinesAsync(validated, lines, diagnostics, cancellationToken);
        if (diagnostics.Any(static diagnostic => diagnostic.Severity == FontGenerationDiagnosticSeverity.Error))
        {
            return new DirectOutlineTextBoxLayoutResult(
                validated.OuterRect,
                contentRect,
                fontMetrics,
                Array.Empty<DirectOutlineLineLayout>(),
                Array.Empty<DirectOutlineGlyphRenderPlacement>(),
                null,
                false,
                metricsResult.UsedFallbackMetrics,
                diagnostics);
        }

        Dictionary<GlyphKey, GlyphMetrics> metricsByGlyph = outlinesByGlyph.ToDictionary(
            static item => item.Key,
            static item => item.Value.Metrics);

        double lineHeight = validated.LineHeightMode == DirectOutlineLineHeightMode.Explicit
            ? validated.ExplicitLineHeight!.Value
            : fontMetrics.LineHeight;

        double blockHeight = ComputeBlockHeight(lines.Length, fontMetrics.Ascent, fontMetrics.Descent, lineHeight);
        double firstBaselineY = ComputeFirstBaselineY(contentRect, fontMetrics, lineHeight, lines.Length, blockHeight, validated.VerticalAlignment);

        List<DirectOutlineLineLayout> lineLayouts = [];
        List<DirectOutlineGlyphRenderPlacement> glyphs = [];
        DirectOutlineRect? blockInkBounds = null;

        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string lineText = lines[lineIndex];
            double baselineY = firstBaselineY + (lineIndex * lineHeight);
            DistanceFieldTextRun run = DistanceFieldTextRun.Create(
                lineText,
                validated.FontFaceId,
                validated.FontSize,
                validated.Weight,
                validated.Slant);
            Dictionary<GlyphPairKey, GlyphPairAdjustment> pairAdjustments = validated.UsePairAdjustments
                ? await CollectPairAdjustmentsAsync(run, cancellationToken)
                : [];

            DistanceFieldTextLayoutResult baseLayout = DistanceFieldTextLayout.Layout(
                run,
                metricsByGlyph,
                CreateLayoutOptions(validated, x: 0d, baselineY),
                diagnostics,
                pairAdjustments);

            double lineX = ComputeLineX(contentRect, baseLayout.Width, validated.HorizontalAlignment);
            DistanceFieldTextLayoutResult positionedLayout = DistanceFieldTextLayout.Layout(
                run,
                metricsByGlyph,
                CreateLayoutOptions(validated, lineX, baselineY),
                diagnostics,
                pairAdjustments);

            DirectOutlineRect? lineInkBounds = ComputeInkBounds(positionedLayout.Placements, outlinesByGlyph);
            blockInkBounds = Union(blockInkBounds, lineInkBounds);

            lineLayouts.Add(new DirectOutlineLineLayout(
                lineText,
                lineX,
                baselineY,
                positionedLayout.Width,
                fontMetrics.Ascent,
                fontMetrics.Descent,
                lineHeight,
                lineInkBounds));

            glyphs.AddRange(positionedLayout.Placements.Select(placement => CreateGlyphPlacement(placement, outlinesByGlyph)));
        }

        bool wasClipped = validated.ClipMode == DirectOutlineTextClipMode.ClipToContentRect
            && IntersectsOutsideContent(blockInkBounds, contentRect);

        return new DirectOutlineTextBoxLayoutResult(
            validated.OuterRect,
            contentRect,
            fontMetrics,
            lineLayouts,
            glyphs,
            blockInkBounds,
            wasClipped,
            metricsResult.UsedFallbackMetrics,
            diagnostics);
    }

    private async ValueTask<DirectOutlineFontMetricsLoadResult> LoadFontMetricsAsync(
        DirectOutlineTextBoxOptions options,
        CancellationToken cancellationToken)
    {
        if (fontMetricsSource is not null)
        {
            DirectOutlineFontMetricsLoadResult result = await fontMetricsSource.LoadFontMetricsAsync(
                options.FontFaceId,
                options.FontSize,
                cancellationToken);
            if (result.Success && result.Metrics is not null)
            {
                return result;
            }
        }

        DirectOutlineFontMetrics fallbackMetrics = CreateFallbackMetrics(options.FontSize);
        FontGenerationDiagnostic diagnostic = new(
            FontGenerationDiagnosticSeverity.Info,
            FontGenerationDiagnosticCode.InvalidGenerationSettings,
            $"Font-level ascent/descent/line-gap metrics were unavailable for '{options.FontFaceId}'. Falling back to a stable 0.8/0.2/0 line-height policy.");

        return new DirectOutlineFontMetricsLoadResult(
            Success: true,
            Metrics: fallbackMetrics,
            UsedFallbackMetrics: true,
            Diagnostics: [diagnostic]);
    }

    private async ValueTask<Dictionary<GlyphKey, GlyphOutline>> LoadOutlinesAsync(
        DirectOutlineTextBoxOptions options,
        IReadOnlyList<string> lines,
        List<FontGenerationDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        GlyphOutlineLoadOptions loadOptions = new(
            (float)options.FontSize,
            0,
            GlyphHintingMode.None,
            normalizeToEm: true);
        Dictionary<GlyphKey, GlyphOutline> outlinesByGlyph = [];
        HashSet<GlyphKey> glyphKeys = [];

        foreach (string line in lines)
        {
            DistanceFieldTextRun run = DistanceFieldTextRun.Create(
                line,
                options.FontFaceId,
                options.FontSize,
                options.Weight,
                options.Slant);

            foreach (GlyphKey glyphKey in run.GlyphKeys)
            {
                glyphKeys.Add(glyphKey);
            }
        }

        foreach (GlyphKey glyphKey in glyphKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            GlyphOutlineLoadResult result = await outlineSource.LoadGlyphOutlineAsync(
                glyphKey.Face,
                glyphKey.Codepoint,
                loadOptions,
                cancellationToken);
            diagnostics.AddRange(result.Diagnostics);

            if (!result.Success || result.Outline is null)
            {
                continue;
            }

            outlinesByGlyph[glyphKey] = result.Outline;
        }

        return outlinesByGlyph;
    }

    private async ValueTask<Dictionary<GlyphPairKey, GlyphPairAdjustment>> CollectPairAdjustmentsAsync(
        DistanceFieldTextRun run,
        CancellationToken cancellationToken)
    {
        Dictionary<GlyphPairKey, GlyphPairAdjustment> adjustments = [];
        if (pairAdjustmentSource is null)
        {
            return adjustments;
        }

        GlyphKey? previousKey = null;
        bool previousWasWhitespace = true;

        foreach (GlyphKey glyphKey in run.GlyphKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool isWhitespace = Rune.IsWhiteSpace(new Rune(glyphKey.Codepoint));
            if (previousKey is GlyphKey previous
                && !previousWasWhitespace
                && !isWhitespace)
            {
                GlyphPairAdjustment? adjustment = await pairAdjustmentSource.GetPairAdjustmentAsync(previous, glyphKey, cancellationToken);
                if (adjustment is not null)
                {
                    adjustments[new GlyphPairKey(previous, glyphKey)] = adjustment;
                }
            }

            previousKey = glyphKey;
            previousWasWhitespace = isWhitespace;
        }

        return adjustments;
    }

    private static string[] SplitExplicitLines(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }

    private static DistanceFieldTextRenderOptions CreateLayoutOptions(
        DirectOutlineTextBoxOptions options,
        double x,
        double baselineY)
    {
        return new DistanceFieldTextRenderOptions(
            1,
            1,
            options.FontFaceId,
            options.FontSize,
            options.Weight,
            options.Slant,
            DistanceFieldKind.Msdf,
            1,
            1,
            1d,
            new Rgba32(255, 255, 255, 255),
            new Rgba32(0, 0, 0, 0),
            x,
            baselineY,
            ShowBaselineGuide: false,
            BaselineGuideColor: null).Validate();
    }

    private static DirectOutlineTextPadding ValidatePadding(DirectOutlineTextPadding padding)
    {
        return padding ?? DirectOutlineTextPadding.Zero;
    }

    private static DirectOutlineRect ComputeContentRect(DirectOutlineRect outerRect, DirectOutlineTextPadding padding)
    {
        DirectOutlineTextPadding safePadding = ValidatePadding(padding);
        double width = Math.Max(0d, outerRect.Width - safePadding.Left - safePadding.Right);
        double height = Math.Max(0d, outerRect.Height - safePadding.Top - safePadding.Bottom);

        return new DirectOutlineRect(
            outerRect.X + safePadding.Left,
            outerRect.Y + safePadding.Top,
            width,
            height);
    }

    private static double ComputeBlockHeight(int lineCount, double ascent, double descent, double lineHeight)
    {
        return lineCount <= 0
            ? 0d
            : ascent + descent + ((lineCount - 1) * lineHeight);
    }

    private static double ComputeFirstBaselineY(
        DirectOutlineRect contentRect,
        DirectOutlineFontMetrics fontMetrics,
        double lineHeight,
        int lineCount,
        double blockHeight,
        DirectOutlineVerticalAlignment alignment)
    {
        double topBaseline = contentRect.Top + fontMetrics.Ascent;

        return alignment switch
        {
            DirectOutlineVerticalAlignment.Top => topBaseline,
            DirectOutlineVerticalAlignment.Middle => contentRect.Top + ((contentRect.Height - blockHeight) / 2d) + fontMetrics.Ascent,
            DirectOutlineVerticalAlignment.Bottom => contentRect.Bottom - blockHeight + fontMetrics.Ascent,
            DirectOutlineVerticalAlignment.Baseline => topBaseline,
            _ => throw new ArgumentOutOfRangeException(nameof(alignment)),
        };
    }

    private static double ComputeLineX(
        DirectOutlineRect contentRect,
        double lineWidth,
        DirectOutlineHorizontalAlignment alignment)
    {
        return alignment switch
        {
            DirectOutlineHorizontalAlignment.Left => contentRect.Left,
            DirectOutlineHorizontalAlignment.Center => contentRect.Left + ((contentRect.Width - lineWidth) / 2d),
            DirectOutlineHorizontalAlignment.Right => contentRect.Right - lineWidth,
            _ => throw new ArgumentOutOfRangeException(nameof(alignment)),
        };
    }

    private static DirectOutlineRect? ComputeInkBounds(
        IReadOnlyList<DistanceFieldGlyphPlacement> placements,
        IReadOnlyDictionary<GlyphKey, GlyphOutline> outlinesByGlyph)
    {
        DirectOutlineRect? bounds = null;

        foreach (DistanceFieldGlyphPlacement placement in placements)
        {
            if (placement.IsWhitespace
                || !outlinesByGlyph.TryGetValue(placement.Key, out GlyphOutline? outline)
                || outline.Contours.Count == 0)
            {
                continue;
            }

            DirectOutlineRect glyphBounds = new(
                placement.X + outline.Bounds.MinX,
                placement.BaselineY - outline.Bounds.MaxY,
                outline.Bounds.MaxX - outline.Bounds.MinX,
                outline.Bounds.MaxY - outline.Bounds.MinY);

            bounds = Union(bounds, glyphBounds);
        }

        return bounds;
    }

    private static DirectOutlineRect? Union(DirectOutlineRect? left, DirectOutlineRect? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        double minX = Math.Min(left.Left, right.Left);
        double minY = Math.Min(left.Top, right.Top);
        double maxX = Math.Max(left.Right, right.Right);
        double maxY = Math.Max(left.Bottom, right.Bottom);

        return new DirectOutlineRect(minX, minY, maxX - minX, maxY - minY);
    }

    private static bool IntersectsOutsideContent(DirectOutlineRect? inkBounds, DirectOutlineRect contentRect)
    {
        if (inkBounds is null)
        {
            return false;
        }

        return inkBounds.Left < contentRect.Left
            || inkBounds.Top < contentRect.Top
            || inkBounds.Right > contentRect.Right
            || inkBounds.Bottom > contentRect.Bottom;
    }

    private static DirectOutlineFontMetrics CreateFallbackMetrics(double fontSize)
    {
        return new DirectOutlineFontMetrics(
            UnitsPerEm: 1000d,
            Ascent: fontSize * 0.8d,
            Descent: fontSize * 0.2d,
            LineGap: 0d);
    }

    private static DirectOutlineGlyphRenderPlacement CreateGlyphPlacement(
        DistanceFieldGlyphPlacement placement,
        IReadOnlyDictionary<GlyphKey, GlyphOutline> outlinesByGlyph)
    {
        InkMaskBounds? inkBounds = null;

        if (!placement.IsWhitespace
            && outlinesByGlyph.TryGetValue(placement.Key, out GlyphOutline? outline)
            && outline.Contours.Count > 0)
        {
            int left = (int)Math.Floor(placement.X + outline.Bounds.MinX);
            int right = (int)Math.Ceiling(placement.X + outline.Bounds.MaxX) - 1;
            int top = (int)Math.Floor(placement.BaselineY - outline.Bounds.MaxY);
            int bottom = (int)Math.Ceiling(placement.BaselineY - outline.Bounds.MinY) - 1;

            if (right >= left && bottom >= top)
            {
                inkBounds = new InkMaskBounds(left, top, right, bottom);
            }
        }

        return new DirectOutlineGlyphRenderPlacement(
            placement.Key,
            placement.Metrics,
            placement.X,
            placement.BaselineY,
            placement.Scale,
            placement.IsWhitespace,
            inkBounds);
    }
}

public sealed class DirectOutlineTextBoxRenderer
{
    private readonly DirectOutlineStaticTextRenderer textRenderer;
    private readonly DirectOutlineTextBoxLayouter layouter;

    public DirectOutlineTextBoxRenderer(
        IGlyphOutlineSource outlineSource,
        IGlyphPairAdjustmentSource? pairAdjustmentSource = null,
        IDirectOutlineFontMetricsSource? fontMetricsSource = null)
    {
        textRenderer = new DirectOutlineStaticTextRenderer(outlineSource, pairAdjustmentSource);
        layouter = new DirectOutlineTextBoxLayouter(outlineSource, pairAdjustmentSource, fontMetricsSource);
    }

    public async ValueTask<DirectOutlineTextBoxRenderResult> RenderAsync(
        DirectOutlineTextBoxRenderOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        DirectOutlineTextBoxRenderOptions validated = options.Validate();

        DirectOutlineTextBoxLayoutResult layout = await layouter.LayoutAsync(validated.Layout, cancellationToken);
        RgbaImage image = CreateFilledImage(validated.OutputWidth, validated.OutputHeight, validated.Background);
        List<FontGenerationDiagnostic> diagnostics = [.. layout.Diagnostics];

        foreach (DirectOutlineLineLayout line in layout.Lines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DirectOutlineTextRenderResult lineRender = await textRenderer.RenderAsync(
                new DirectOutlineTextRenderOptions(
                    line.Text,
                    validated.Layout.FontFaceId,
                    validated.Layout.FontSize,
                    validated.OutputWidth,
                    validated.OutputHeight,
                    validated.Foreground,
                    new Rgba32(0, 0, 0, 0),
                    line.X,
                    line.BaselineY,
                    validated.Layout.Weight,
                    validated.Layout.Slant,
                    validated.Layout.Supersample,
                    validated.FillRule,
                    validated.CurveSubdivisionCount,
                    validated.ShowBaselineGuides,
                    validated.BaselineGuideColor,
                    validated.Layout.UsePairAdjustments),
                cancellationToken);

            diagnostics.AddRange(lineRender.Diagnostics);

            if (!lineRender.Success || lineRender.Image is null)
            {
                continue;
            }

            Composite(image, lineRender.Image);
        }

        if (validated.Layout.ClipMode == DirectOutlineTextClipMode.ClipToContentRect)
        {
            ClipOutside(image, layout.ContentRect, validated.Background);
        }

        return new DirectOutlineTextBoxRenderResult(
            layout,
            image,
            ComputeRenderedInkBounds(image, validated.Background),
            diagnostics);
    }

    private static void Composite(RgbaImage target, RgbaImage source)
    {
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                Rgba32 pixel = source.GetPixel(x, y);
                if (pixel.A == 0)
                {
                    continue;
                }

                target.SetPixel(x, y, pixel);
            }
        }
    }

    private static void ClipOutside(RgbaImage image, DirectOutlineRect contentRect, Rgba32 background)
    {
        PixelClipBounds? clipBounds = ToPixelClipBounds(contentRect, image.Width, image.Height);
        if (clipBounds is null)
        {
            FillImage(image, background);
            return;
        }

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                bool inside = x >= clipBounds.Left
                    && x <= clipBounds.Right
                    && y >= clipBounds.Top
                    && y <= clipBounds.Bottom;

                if (!inside)
                {
                    image.SetPixel(x, y, background);
                }
            }
        }
    }

    private static DirectOutlineRect? ComputeRenderedInkBounds(RgbaImage image, Rgba32 background)
    {
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                if (image.GetPixel(x, y).Equals(background))
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return maxX < minX || maxY < minY
            ? null
            : new DirectOutlineRect(minX, minY, (maxX - minX) + 1d, (maxY - minY) + 1d);
    }

    private static PixelClipBounds? ToPixelClipBounds(DirectOutlineRect rect, int width, int height)
    {
        int left = Math.Max(0, (int)Math.Floor(rect.Left));
        int top = Math.Max(0, (int)Math.Floor(rect.Top));
        int right = Math.Min(width - 1, (int)Math.Ceiling(rect.Right) - 1);
        int bottom = Math.Min(height - 1, (int)Math.Ceiling(rect.Bottom) - 1);

        return right < left || bottom < top
            ? null
            : new PixelClipBounds(left, top, right, bottom);
    }

    private static RgbaImage CreateFilledImage(int width, int height, Rgba32 background)
    {
        RgbaImage image = new(width, height);
        FillImage(image, background);
        return image;
    }

    private static void FillImage(RgbaImage image, Rgba32 background)
    {
        for (int index = 0; index < image.Pixels.Length; index++)
        {
            image.Pixels[index] = background;
        }
    }

    private sealed record PixelClipBounds(int Left, int Top, int Right, int Bottom);
}
