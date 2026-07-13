namespace Aurelian.Rendering.Contracts.Resolved2D;

/// <summary>
/// A finite resolved rectangle. Empty and negative-sized rectangles are valid
/// renderer inputs and rasterize as no-ops.
/// </summary>
public readonly record struct Resolved2DRectangle
{
    public Resolved2DRectangle(double x, double y, double width, double height)
    {
        ValidateFinite(x, nameof(x));
        ValidateFinite(y, nameof(y));
        ValidateFinite(width, nameof(width));
        ValidateFinite(height, nameof(height));

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    public bool IsEmptyOrNegative => Width <= 0 || Height <= 0;

    private static void ValidateFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Resolved rectangle values must be finite.");
        }
    }
}
