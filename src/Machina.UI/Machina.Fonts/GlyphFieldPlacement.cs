namespace Machina.Fonts;

public sealed record GlyphFieldPlacement
{
    public GlyphFieldPlacement(
        double planeLeft,
        double planeTop,
        double planeRight,
        double planeBottom,
        double pixelRange,
        double projectionScale)
    {
        if (!double.IsFinite(planeLeft))
        {
            throw new ArgumentOutOfRangeException(nameof(planeLeft));
        }

        if (!double.IsFinite(planeTop))
        {
            throw new ArgumentOutOfRangeException(nameof(planeTop));
        }

        if (!double.IsFinite(planeRight))
        {
            throw new ArgumentOutOfRangeException(nameof(planeRight));
        }

        if (!double.IsFinite(planeBottom))
        {
            throw new ArgumentOutOfRangeException(nameof(planeBottom));
        }

        if (planeRight <= planeLeft)
        {
            throw new ArgumentException("Plane right must be greater than plane left.", nameof(planeRight));
        }

        if (planeBottom <= planeTop)
        {
            throw new ArgumentException("Plane bottom must be greater than plane top.", nameof(planeBottom));
        }

        if (!double.IsFinite(pixelRange) || pixelRange < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelRange));
        }

        if (!double.IsFinite(projectionScale) || projectionScale <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(projectionScale));
        }

        PlaneLeft = planeLeft;
        PlaneTop = planeTop;
        PlaneRight = planeRight;
        PlaneBottom = planeBottom;
        PixelRange = pixelRange;
        ProjectionScale = projectionScale;
    }

    public double PlaneLeft { get; }

    public double PlaneTop { get; }

    public double PlaneRight { get; }

    public double PlaneBottom { get; }

    public double PixelRange { get; }

    public double ProjectionScale { get; }

    public double Width => PlaneRight - PlaneLeft;

    public double Height => PlaneBottom - PlaneTop;

    public static GlyphFieldPlacement CreateFromMetricsBox(
        GlyphMetrics metrics,
        double pixelRange = 0d,
        double projectionScale = 1d)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        double width = Math.Max(metrics.Width, 0.0001d);
        double height = Math.Max(metrics.Height, 0.0001d);

        return new GlyphFieldPlacement(
            metrics.BearingX,
            -metrics.BearingY,
            metrics.BearingX + width,
            height - metrics.BearingY,
            pixelRange,
            projectionScale);
    }
}
