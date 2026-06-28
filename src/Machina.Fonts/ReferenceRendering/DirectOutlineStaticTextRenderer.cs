using System.Text;
using Machina.Fonts.Generation;

namespace Machina.Fonts.ReferenceRendering;

public sealed record DirectOutlineTextRenderOptions(
    string Text,
    FontFaceId Face,
    double EmSize,
    int OutputWidth,
    int OutputHeight,
    Rgba32 Foreground,
    Rgba32 Background,
    double X,
    double BaselineY,
    MachinaFontWeight Weight = MachinaFontWeight.Regular,
    MachinaFontSlant Slant = MachinaFontSlant.Upright,
    int Supersample = 4,
    OutlineFillRule FillRule = OutlineFillRule.EvenOdd,
    int CurveSubdivisionCount = 24,
    bool ShowBaselineGuide = false,
    Rgba32? BaselineGuideColor = null,
    bool UsePairAdjustments = true)
{
    public DirectOutlineTextRenderOptions Validate()
    {
        ArgumentNullException.ThrowIfNull(Text);

        if (!double.IsFinite(EmSize) || EmSize <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(EmSize));
        }

        if (OutputWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OutputWidth));
        }

        if (OutputHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OutputHeight));
        }

        if (!double.IsFinite(X))
        {
            throw new ArgumentOutOfRangeException(nameof(X));
        }

        if (!double.IsFinite(BaselineY))
        {
            throw new ArgumentOutOfRangeException(nameof(BaselineY));
        }

        if (Supersample is not 1 and not 2 and not 4)
        {
            throw new ArgumentOutOfRangeException(nameof(Supersample), "Supported supersample levels are 1, 2, and 4.");
        }

        if (CurveSubdivisionCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(CurveSubdivisionCount));
        }

        if (ShowBaselineGuide && BaselineGuideColor is null)
        {
            throw new ArgumentException("Baseline guide color must be provided when the baseline guide is enabled.", nameof(BaselineGuideColor));
        }

        return this;
    }
}

public sealed record DirectOutlineGlyphRenderPlacement(
    GlyphKey Key,
    GlyphMetrics Metrics,
    double X,
    double BaselineY,
    double Scale,
    bool IsWhitespace,
    InkMaskBounds? InkBounds);

public sealed record DirectOutlineTextRenderResult(
    bool Success,
    MachinaTextRenderStrategy RenderStrategy,
    RgbaImage? Image,
    InkMask? Mask,
    InkMaskBounds? InkBounds,
    IReadOnlyList<DirectOutlineGlyphRenderPlacement> Glyphs,
    IReadOnlyList<FontGenerationDiagnostic> Diagnostics);

public sealed class DirectOutlineStaticTextRenderer
{
    private readonly IGlyphOutlineSource outlineSource;
    private readonly IGlyphPairAdjustmentSource? pairAdjustmentSource;

    public DirectOutlineStaticTextRenderer(
        IGlyphOutlineSource outlineSource,
        IGlyphPairAdjustmentSource? pairAdjustmentSource = null)
    {
        this.outlineSource = outlineSource ?? throw new ArgumentNullException(nameof(outlineSource));
        this.pairAdjustmentSource = pairAdjustmentSource ?? outlineSource as IGlyphPairAdjustmentSource;
    }

    public async ValueTask<DirectOutlineTextRenderResult> RenderAsync(
        DirectOutlineTextRenderOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        DirectOutlineTextRenderOptions validated = options.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        DistanceFieldTextRun run = DistanceFieldTextRun.Create(
            validated.Text,
            validated.Face,
            validated.EmSize,
            validated.Weight,
            validated.Slant);

        GlyphOutlineLoadOptions loadOptions = new(
            (float)validated.EmSize,
            0,
            GlyphHintingMode.None,
            normalizeToEm: true);

        Dictionary<GlyphKey, GlyphOutline> outlinesByGlyph = [];
        Dictionary<GlyphKey, GlyphMetrics> metricsByGlyph = [];
        List<FontGenerationDiagnostic> diagnostics = [];

        foreach (GlyphKey glyphKey in run.GlyphKeys.Distinct())
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
                return CreateFailure(diagnostics);
            }

            outlinesByGlyph[glyphKey] = result.Outline;
            metricsByGlyph[glyphKey] = result.Outline.Metrics;
        }

        Dictionary<GlyphPairKey, GlyphPairAdjustment> pairAdjustments = validated.UsePairAdjustments
            ? await CollectPairAdjustmentsAsync(run, cancellationToken)
            : [];

        DistanceFieldTextLayoutResult layout = DistanceFieldTextLayout.Layout(
            run,
            metricsByGlyph,
            CreateLayoutOptions(validated),
            diagnostics,
            pairAdjustments);

        if (layout.Diagnostics.Any(static diagnostic => diagnostic.Severity == FontGenerationDiagnosticSeverity.Error))
        {
            return CreateFailure(layout.Diagnostics);
        }

        DirectOutlineMaskRenderOptions renderOptions = new(
            validated.OutputWidth,
            validated.OutputHeight,
            validated.Foreground,
            validated.Background,
            validated.X,
            validated.BaselineY,
            validated.Supersample,
            validated.FillRule,
            validated.CurveSubdivisionCount,
            validated.ShowBaselineGuide,
            validated.BaselineGuideColor);

        InkMask mask = DirectOutlineMaskRenderer.RenderMask(outlinesByGlyph, layout, renderOptions);
        RgbaImage image = mask.ToImage(
            validated.Foreground,
            validated.Background,
            validated.ShowBaselineGuide,
            validated.BaselineY,
            validated.BaselineGuideColor);

        List<DirectOutlineGlyphRenderPlacement> glyphs = layout.Placements
            .Select(placement => CreateGlyphPlacement(placement, outlinesByGlyph))
            .ToList();

        return new DirectOutlineTextRenderResult(
            true,
            MachinaTextRenderStrategyCatalog.DefaultStatic,
            image,
            mask,
            mask.ComputeBounds(),
            glyphs,
            layout.Diagnostics.ToArray());
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
            if (previousKey is GlyphKey previous && !previousWasWhitespace && !isWhitespace)
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

    private static DistanceFieldTextRenderOptions CreateLayoutOptions(DirectOutlineTextRenderOptions options)
    {
        return new DistanceFieldTextRenderOptions(
            options.OutputWidth,
            options.OutputHeight,
            options.Face,
            options.EmSize,
            options.Weight,
            options.Slant,
            DistanceFieldKind.Msdf,
            1,
            1,
            1d,
            options.Foreground,
            options.Background,
            options.X,
            options.BaselineY,
            options.ShowBaselineGuide,
            options.BaselineGuideColor).Validate();
    }

    private static DirectOutlineGlyphRenderPlacement CreateGlyphPlacement(
        DistanceFieldGlyphPlacement placement,
        IReadOnlyDictionary<GlyphKey, GlyphOutline> outlinesByGlyph)
    {
        InkMaskBounds? inkBounds = null;

        if (!placement.IsWhitespace && outlinesByGlyph.TryGetValue(placement.Key, out GlyphOutline? outline) && outline.Contours.Count > 0)
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

    private static DirectOutlineTextRenderResult CreateFailure(IReadOnlyList<FontGenerationDiagnostic> diagnostics)
    {
        return new DirectOutlineTextRenderResult(
            false,
            MachinaTextRenderStrategyCatalog.DefaultStatic,
            null,
            null,
            null,
            Array.Empty<DirectOutlineGlyphRenderPlacement>(),
            diagnostics.ToArray());
    }
}
