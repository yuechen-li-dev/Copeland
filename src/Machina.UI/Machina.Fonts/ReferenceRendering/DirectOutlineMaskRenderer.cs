using Machina.Fonts.Generation;

namespace Machina.Fonts.ReferenceRendering;

public sealed record DirectOutlineMaskRenderOptions(
    int OutputWidth,
    int OutputHeight,
    Rgba32 Foreground,
    Rgba32 Background,
    double X,
    double BaselineY,
    int Supersample = 4,
    OutlineFillRule FillRule = OutlineFillRule.EvenOdd,
    int CurveSubdivisionCount = 24,
    bool ShowBaselineGuide = false,
    Rgba32? BaselineGuideColor = null)
{
    public DirectOutlineMaskRenderOptions Validate()
    {
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

    public OutlineFlatteningOptions ToFlatteningOptions()
    {
        return new OutlineFlatteningOptions(CurveSubdivisionCount);
    }
}

public static class DirectOutlineMaskRenderer
{
    public static InkMask RenderMask(
        IReadOnlyDictionary<GlyphKey, GlyphOutline> outlines,
        DistanceFieldTextLayoutResult layout,
        DirectOutlineMaskRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(outlines);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(options);
        DirectOutlineMaskRenderOptions validated = options.Validate();

        if (layout.Diagnostics.Any(static diagnostic => diagnostic.Severity == FontGenerationDiagnosticSeverity.Error))
        {
            throw new InvalidOperationException("Cannot render text layout with error diagnostics.");
        }

        InkMask mask = new(validated.OutputWidth, validated.OutputHeight);
        Dictionary<GlyphKey, FlattenedGlyphOutline> flattenedCache = [];

        foreach (DistanceFieldGlyphPlacement placement in layout.Placements)
        {
            if (placement.IsWhitespace)
            {
                continue;
            }

            if (!outlines.TryGetValue(placement.Key, out GlyphOutline? outline))
            {
                throw new InvalidOperationException($"Missing outline for glyph U+{placement.Key.Codepoint:X4}.");
            }

            if (outline.Contours.Count == 0)
            {
                continue;
            }

            if (!flattenedCache.TryGetValue(placement.Key, out FlattenedGlyphOutline? flattened))
            {
                flattened = OutlineFlattening.FlattenOutline(outline, validated.ToFlatteningOptions());
                flattenedCache.Add(placement.Key, flattened);
            }

            RenderGlyphInto(mask, flattened, placement, validated);
        }

        return mask;
    }

    public static RgbaImage RenderText(
        IReadOnlyDictionary<GlyphKey, GlyphOutline> outlines,
        DistanceFieldTextLayoutResult layout,
        DirectOutlineMaskRenderOptions options)
    {
        InkMask mask = RenderMask(outlines, layout, options);
        return mask.ToImage(
            options.Foreground,
            options.Background,
            options.ShowBaselineGuide,
            options.BaselineY,
            options.BaselineGuideColor);
    }

    public static RgbaImage RenderWireframe(
        IReadOnlyDictionary<GlyphKey, GlyphOutline> outlines,
        DistanceFieldTextLayoutResult layout,
        DirectOutlineMaskRenderOptions options,
        Rgba32 contourColor,
        Rgba32 backgroundColor)
    {
        ArgumentNullException.ThrowIfNull(outlines);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        RgbaImage image = new(options.OutputWidth, options.OutputHeight);
        CpuDistanceFieldGlyphRenderer.Fill(image, backgroundColor);
        Dictionary<GlyphKey, FlattenedGlyphOutline> flattenedCache = [];

        foreach (DistanceFieldGlyphPlacement placement in layout.Placements)
        {
            if (placement.IsWhitespace)
            {
                continue;
            }

            if (!outlines.TryGetValue(placement.Key, out GlyphOutline? outline) || outline.Contours.Count == 0)
            {
                continue;
            }

            if (!flattenedCache.TryGetValue(placement.Key, out FlattenedGlyphOutline? flattened))
            {
                flattened = OutlineFlattening.FlattenOutline(outline, options.ToFlatteningOptions());
                flattenedCache.Add(placement.Key, flattened);
            }

            foreach (FlattenedGlyphContour contour in flattened.Contours)
            {
                if (contour.Points.Count < 2)
                {
                    continue;
                }

                for (int pointIndex = 0; pointIndex < contour.Points.Count; pointIndex++)
                {
                    GlyphPoint start = contour.Points[pointIndex];
                    GlyphPoint end = contour.Points[(pointIndex + 1) % contour.Points.Count];
                    DrawLine(image, placement, start, end, contourColor);
                }
            }
        }

        if (options.ShowBaselineGuide)
        {
            int baselineRow = (int)Math.Round(options.BaselineY, MidpointRounding.AwayFromZero);
            if ((uint)baselineRow < (uint)image.Height)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    image.SetPixel(x, baselineRow, options.BaselineGuideColor!.Value);
                }
            }
        }

        return image;
    }

    private static void RenderGlyphInto(
        InkMask mask,
        FlattenedGlyphOutline outline,
        DistanceFieldGlyphPlacement placement,
        DirectOutlineMaskRenderOptions options)
    {
        int left = Math.Max(0, (int)Math.Floor(placement.X + outline.Bounds.MinX));
        int right = Math.Min(mask.Width - 1, (int)Math.Ceiling(placement.X + outline.Bounds.MaxX) - 1);
        int top = Math.Max(0, (int)Math.Floor(placement.BaselineY - outline.Bounds.MaxY));
        int bottom = Math.Min(mask.Height - 1, (int)Math.Ceiling(placement.BaselineY - outline.Bounds.MinY) - 1);

        if (right < left || bottom < top)
        {
            return;
        }

        int sampleCountPerAxis = options.Supersample;
        int totalSamples = sampleCountPerAxis * sampleCountPerAxis;

        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                int insideCount = 0;

                for (int sampleY = 0; sampleY < sampleCountPerAxis; sampleY++)
                {
                    double offsetY = (sampleY + 0.5d) / sampleCountPerAxis;
                    double localY = placement.BaselineY - (y + offsetY);

                    for (int sampleX = 0; sampleX < sampleCountPerAxis; sampleX++)
                    {
                        double offsetX = (sampleX + 0.5d) / sampleCountPerAxis;
                        double localX = (x + offsetX) - placement.X;

                        if (ContainsPoint(outline, localX, localY, options.FillRule))
                        {
                            insideCount++;
                        }
                    }
                }

                if (insideCount == 0)
                {
                    continue;
                }

                float existing = mask.GetCoverage(x, y);
                float coverage = insideCount / (float)totalSamples;
                mask.SetCoverage(x, y, Math.Max(existing, coverage));
            }
        }
    }

    private static bool ContainsPoint(
        FlattenedGlyphOutline outline,
        double x,
        double y,
        OutlineFillRule fillRule)
    {
        return fillRule switch
        {
            OutlineFillRule.EvenOdd => ContainsPointEvenOdd(outline.Contours, x, y),
            OutlineFillRule.NonZero => ContainsPointNonZero(outline.Contours, x, y),
            _ => throw new InvalidOperationException($"Unsupported fill rule '{fillRule}'."),
        };
    }

    private static bool ContainsPointEvenOdd(
        IReadOnlyList<FlattenedGlyphContour> contours,
        double x,
        double y)
    {
        bool inside = false;

        foreach (FlattenedGlyphContour contour in contours)
        {
            if (contour.Points.Count < 2)
            {
                continue;
            }

            bool contourInside = false;
            for (int index = 0; index < contour.Points.Count; index++)
            {
                GlyphPoint left = contour.Points[index];
                GlyphPoint right = contour.Points[(index + 1) % contour.Points.Count];

                bool intersects = ((left.Y > y) != (right.Y > y))
                    && (x < (((right.X - left.X) * (y - left.Y)) / (right.Y - left.Y)) + left.X);

                if (intersects)
                {
                    contourInside = !contourInside;
                }
            }

            inside ^= contourInside;
        }

        return inside;
    }

    private static bool ContainsPointNonZero(
        IReadOnlyList<FlattenedGlyphContour> contours,
        double x,
        double y)
    {
        int windingNumber = 0;

        foreach (FlattenedGlyphContour contour in contours)
        {
            if (contour.Points.Count < 2)
            {
                continue;
            }

            for (int index = 0; index < contour.Points.Count; index++)
            {
                GlyphPoint start = contour.Points[index];
                GlyphPoint end = contour.Points[(index + 1) % contour.Points.Count];

                if (start.Y <= y)
                {
                    if (end.Y > y && IsLeft(start, end, x, y) > 0d)
                    {
                        windingNumber++;
                    }
                }
                else if (end.Y <= y && IsLeft(start, end, x, y) < 0d)
                {
                    windingNumber--;
                }
            }
        }

        return windingNumber != 0;
    }

    private static double IsLeft(GlyphPoint start, GlyphPoint end, double x, double y)
    {
        return ((end.X - start.X) * (y - start.Y)) - ((x - start.X) * (end.Y - start.Y));
    }

    private static void DrawLine(
        RgbaImage image,
        DistanceFieldGlyphPlacement placement,
        GlyphPoint start,
        GlyphPoint end,
        Rgba32 color)
    {
        int x0 = (int)Math.Round(placement.X + start.X, MidpointRounding.AwayFromZero);
        int y0 = (int)Math.Round(placement.BaselineY - start.Y, MidpointRounding.AwayFromZero);
        int x1 = (int)Math.Round(placement.X + end.X, MidpointRounding.AwayFromZero);
        int y1 = (int)Math.Round(placement.BaselineY - end.Y, MidpointRounding.AwayFromZero);

        int deltaX = Math.Abs(x1 - x0);
        int stepX = x0 < x1 ? 1 : -1;
        int deltaY = -Math.Abs(y1 - y0);
        int stepY = y0 < y1 ? 1 : -1;
        int error = deltaX + deltaY;

        while (true)
        {
            if ((uint)x0 < (uint)image.Width && (uint)y0 < (uint)image.Height)
            {
                image.SetPixel(x0, y0, color);
            }

            if (x0 == x1 && y0 == y1)
            {
                break;
            }

            int twiceError = error * 2;
            if (twiceError >= deltaY)
            {
                error += deltaY;
                x0 += stepX;
            }

            if (twiceError <= deltaX)
            {
                error += deltaX;
                y0 += stepY;
            }
        }
    }
}
