namespace Machina.Runtime.Input;

public enum ImageStretchMode
{
    None,
    Fill,
    Uniform
}

public readonly record struct PresentedImageRect(
    double X,
    double Y,
    double Width,
    double Height);

public static class PresentedImageMapper
{
    public static PointerPoint? ToRootPoint(
        PointerPoint presentedPoint,
        double sourceWidth,
        double sourceHeight,
        PresentedImageRect destination,
        ImageStretchMode stretchMode)
    {
        ValidateFinite(presentedPoint.X, nameof(presentedPoint));
        ValidateFinite(presentedPoint.Y, nameof(presentedPoint));
        ValidatePositiveFinite(sourceWidth, nameof(sourceWidth));
        ValidatePositiveFinite(sourceHeight, nameof(sourceHeight));
        ValidatePositiveFinite(destination.Width, nameof(destination));
        ValidatePositiveFinite(destination.Height, nameof(destination));
        ValidateFinite(destination.X, nameof(destination));
        ValidateFinite(destination.Y, nameof(destination));

        return stretchMode switch
        {
            ImageStretchMode.None => MapNone(presentedPoint, sourceWidth, sourceHeight, destination),
            ImageStretchMode.Fill => MapFill(presentedPoint, sourceWidth, sourceHeight, destination),
            ImageStretchMode.Uniform => MapUniform(presentedPoint, sourceWidth, sourceHeight, destination),
            _ => throw new ArgumentOutOfRangeException(nameof(stretchMode), stretchMode, "Unsupported stretch mode.")
        };
    }

    private static PointerPoint? MapNone(
        PointerPoint presentedPoint,
        double sourceWidth,
        double sourceHeight,
        PresentedImageRect destination)
    {
        double rootX = presentedPoint.X - destination.X;
        double rootY = presentedPoint.Y - destination.Y;

        if (!IsInsideHalfOpen(rootX, rootY, 0, 0, sourceWidth, sourceHeight))
        {
            return null;
        }

        return new PointerPoint(rootX, rootY);
    }

    private static PointerPoint? MapFill(
        PointerPoint presentedPoint,
        double sourceWidth,
        double sourceHeight,
        PresentedImageRect destination)
    {
        if (!IsInsideHalfOpen(
                presentedPoint.X,
                presentedPoint.Y,
                destination.X,
                destination.Y,
                destination.Width,
                destination.Height))
        {
            return null;
        }

        double relativeX = presentedPoint.X - destination.X;
        double relativeY = presentedPoint.Y - destination.Y;
        double rootX = relativeX * sourceWidth / destination.Width;
        double rootY = relativeY * sourceHeight / destination.Height;

        return new PointerPoint(rootX, rootY);
    }

    private static PointerPoint? MapUniform(
        PointerPoint presentedPoint,
        double sourceWidth,
        double sourceHeight,
        PresentedImageRect destination)
    {
        double scale = Math.Min(destination.Width / sourceWidth, destination.Height / sourceHeight);
        double contentWidth = sourceWidth * scale;
        double contentHeight = sourceHeight * scale;
        double contentX = destination.X + ((destination.Width - contentWidth) / 2.0);
        double contentY = destination.Y + ((destination.Height - contentHeight) / 2.0);

        if (!IsInsideHalfOpen(presentedPoint.X, presentedPoint.Y, contentX, contentY, contentWidth, contentHeight))
        {
            return null;
        }

        double rootX = (presentedPoint.X - contentX) / scale;
        double rootY = (presentedPoint.Y - contentY) / scale;

        return new PointerPoint(rootX, rootY);
    }

    private static bool IsInsideHalfOpen(double x, double y, double left, double top, double width, double height)
    {
        double right = left + width;
        double bottom = top + height;

        return x >= left && x < right && y >= top && y < bottom;
    }

    private static void ValidatePositiveFinite(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "Value must be finite and greater than zero.");
        }
    }

    private static void ValidateFinite(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentException("Value must be finite.", name);
        }
    }
}
