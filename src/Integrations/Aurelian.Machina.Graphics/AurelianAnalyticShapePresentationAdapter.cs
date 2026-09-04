using Aurelian.Graphics.Vulkan.Native2D;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Presentation;

namespace Aurelian.Machina;

/// <summary>
/// Adapts renderer-neutral Machina shape intent to the native ordered-quad contract.
/// Shape meaning and validation remain owned by Machina.
/// </summary>
public static class AurelianAnalyticShapePresentationAdapter
{
    public static NativeAnalyticShapeSubmission? Adapt(
        MachinaAnalyticShapePrimitive primitive,
        Rect? clipRect = null)
    {
        ArgumentNullException.ThrowIfNull(primitive);

        Rect original = primitive.DestinationRect;
        Rect visible = clipRect is Rect clip ? Intersect(original, clip) : original;
        if (visible.Width <= 0 || visible.Height <= 0)
        {
            return null;
        }

        float u0 = (float)((visible.X - original.X) / original.Width);
        float v0 = (float)((visible.Y - original.Y) / original.Height);
        float u1 = (float)((visible.X + visible.Width - original.X) / original.Width);
        float v1 = (float)((visible.Y + visible.Height - original.Y) / original.Height);
        Native2DTint fill = ToTint(primitive.FillColor);
        return new NativeAnalyticShapeSubmission(
            ToRect(visible),
            new Native2DSize((float)original.Width, (float)original.Height),
            new Native2DUvRect(u0, v0, u1, v1),
            ToKind(primitive.Kind),
            fill,
            (float)primitive.Radius,
            primitive.BorderColor is ColorToken border ? ToTint(border) : fill,
            (float)primitive.BorderWidth);
    }

    private static Rect Intersect(Rect left, Rect right)
    {
        double x = Math.Max(left.X, right.X);
        double y = Math.Max(left.Y, right.Y);
        double rightEdge = Math.Min(left.X + left.Width, right.X + right.Width);
        double bottomEdge = Math.Min(left.Y + left.Height, right.Y + right.Height);
        return new Rect(x, y, Math.Max(0, rightEdge - x), Math.Max(0, bottomEdge - y));
    }

    private static NativeAnalyticShapeKind ToKind(MachinaAnalyticShapeKind kind)
    {
        return kind switch
        {
            MachinaAnalyticShapeKind.RoundedRect => NativeAnalyticShapeKind.RoundedRect,
            MachinaAnalyticShapeKind.Circle => NativeAnalyticShapeKind.Circle,
            MachinaAnalyticShapeKind.Pill => NativeAnalyticShapeKind.Pill,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    private static Native2DRect ToRect(Rect rect)
        => new((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);

    private static Native2DTint ToTint(ColorToken color)
    {
        const float scale = 1f / 255f;
        return new Native2DTint(
            (byte)(color.Rgba >> 24) * scale,
            (byte)(color.Rgba >> 16) * scale,
            (byte)(color.Rgba >> 8) * scale,
            (byte)color.Rgba * scale);
    }
}
