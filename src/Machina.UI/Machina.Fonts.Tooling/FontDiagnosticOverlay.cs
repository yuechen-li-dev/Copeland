using Machina.Fonts.ReferenceRendering;

namespace Machina.Fonts.Tooling;

public static class FontDiagnosticOverlay
{
    public static RgbaImage DrawGrid(RgbaImage source, FontDiagnosticGridOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        FontDiagnosticGridOptions validated = options.Validate();
        DiagnosticLayerComposition composition = new(
            source.Width,
            source.Height,
            Rgba32.Transparent,
            [
                new DiagnosticImageLayer("source", "Source", true, 1d, 0, source),
                new DiagnosticGridLayer("grid", "Grid", validated.ShowGrid, 1d, 10, validated.GridStep, validated.AxisStep, validated.ShowUnitLabels, validated.GridColor, validated.MajorGridColor, validated.LabelColor),
                new DiagnosticAxisLayer("axes", "Axes", validated.ShowAxes, 1d, 20, true, true, validated.ShowOriginMarker, validated.AxisStep, validated.AxisColor),
                new DiagnosticBaselineLayer("baseline", "Baseline", validated.ShowBaseline, 1d, 30, validated.BaselineY, validated.BaselineColor),
            ]);

        return LayerCompositor.Compose(composition);
    }

    public static RgbaImage DrawBounds(
        RgbaImage source,
        FontDiagnosticBoundsSet bounds,
        FontDiagnosticBoundsOverlayOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(bounds);

        FontDiagnosticBoundsOverlayOptions resolved = options ?? new FontDiagnosticBoundsOverlayOptions();
        List<DiagnosticBoundsItem> items = [];
        if (resolved.ShowBounds)
        {
            items.Add(new DiagnosticBoundsItem("browser", "browser", bounds.BrowserBounds, resolved.BrowserBoundsColor));
            items.Add(new DiagnosticBoundsItem("machina", "machina", bounds.MachinaBounds, resolved.MachinaBoundsColor));
            items.Add(new DiagnosticBoundsItem("direct", "direct", bounds.DirectOutlineBounds, resolved.DirectOutlineBoundsColor));
            items.Add(new DiagnosticBoundsItem("msdf", "msdf", bounds.MsdfBounds, resolved.MsdfBoundsColor));
        }

        DiagnosticLayerComposition composition = new(
            source.Width,
            source.Height,
            Rgba32.Transparent,
            [
                new DiagnosticImageLayer("source", "Source", true, 1d, 0, source),
                new DiagnosticBoundsLayer("bounds", "Bounds", resolved.ShowBounds, 1d, 10, items),
                new DiagnosticGlyphWireframeLayer("wireframes", "Wireframes", resolved.ShowWireframes, 1d, 20, bounds.WireframeBounds, resolved.WireframeColor),
            ]);

        return LayerCompositor.Compose(composition);
    }

    internal static void CompositeNonBackground(
        RgbaImage destination,
        RgbaImage layer,
        Rgba32 background)
    {
        if (destination.Width != layer.Width || destination.Height != layer.Height)
        {
            throw new InvalidOperationException("Diagnostic layer sizes must match.");
        }

        for (int index = 0; index < destination.Pixels.Length; index++)
        {
            Rgba32 pixel = layer.Pixels[index];
            if (pixel != background)
            {
                destination.Pixels[index] = pixel;
            }
        }
    }
}
