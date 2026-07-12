using Machina.Layout.Diagnostics;
using Machina.Layout.Frames;
using Machina.Layout.Geometry;

namespace Machina.Layout.Resolving;

public static class FrameResolver
{
    public static Rect ResolveFrame(Rect parent, FrameSpec frame)
    {
        ValidateParent(parent);

        return frame switch
        {
            AbsoluteFrame absoluteFrame => ResolveAbsolute(parent, absoluteFrame),
            AnchorFrame anchorFrame => ResolveAnchor(parent, anchorFrame),
            FixedFrame => throw new LayoutError("FixedFrameWithoutArranger", "FixedFrame cannot be resolved directly without a stack arranger."),
            FillFrame => throw new LayoutError("FillFrameWithoutArranger", "FillFrame cannot be resolved directly without a stack arranger."),
            RootFrame => throw new LayoutError("RootFrameWithoutRoot", "RootFrame cannot be resolved without an explicit root context."),
            CellFrame => throw new LayoutError("CellFrameWithoutGrid", "CellFrame cannot be resolved directly without a grid arranger."),
            _ => throw new LayoutError("UnsupportedFrame", $"Unsupported frame type: {frame.GetType().FullName}"),
        };
    }

    private static Rect ResolveAbsolute(Rect parent, AbsoluteFrame frame)
    {
        ValidateFinite(frame.X, "InvalidFrameNumber", "AbsoluteFrame.X must be finite.");
        ValidateFinite(frame.Y, "InvalidFrameNumber", "AbsoluteFrame.Y must be finite.");
        ValidateFinite(frame.Width, "InvalidFrameNumber", "AbsoluteFrame.Width must be finite.");
        ValidateFinite(frame.Height, "InvalidFrameNumber", "AbsoluteFrame.Height must be finite.");
        ValidateNonNegative(frame.Width, "NegativeFrameSize", "AbsoluteFrame.Width must be non-negative.");
        ValidateNonNegative(frame.Height, "NegativeFrameSize", "AbsoluteFrame.Height must be non-negative.");

        return new Rect(parent.X + frame.X, parent.Y + frame.Y, frame.Width, frame.Height);
    }

    private static Rect ResolveAnchor(Rect parent, AnchorFrame frame)
    {
        var (x, width) = ResolveHorizontal(parent, frame);
        var (y, height) = ResolveVertical(parent, frame);
        ValidateFinite(x, "InvalidFrameNumber", "Resolved X must be finite.");
        ValidateFinite(y, "InvalidFrameNumber", "Resolved Y must be finite.");
        ValidateFinite(width, "InvalidFrameNumber", "Resolved Width must be finite.");
        ValidateFinite(height, "InvalidFrameNumber", "Resolved Height must be finite.");

        return new Rect(x, y, width, height);
    }

    private static (double X, double Width) ResolveHorizontal(Rect parent, AnchorFrame frame)
    {
        var left = ResolveOptional(frame.Left, parent.Width);
        var right = ResolveOptional(frame.Right, parent.Width);
        var width = ResolveOptional(frame.Width, parent.Width);
        var count = CountNonNull(frame.Left, frame.Right, frame.Width);

        if (count != 2)
            throw new LayoutError("InvalidAnchorHorizontal", "AnchorFrame requires exactly two horizontal constraints from Left, Right, Width.");

        if (left is not null && width is not null)
        {
            ValidateNonNegative(width.Value, "NegativeFrameSize", "AnchorFrame.Width must be non-negative.");
            return (parent.X + left.Value, width.Value);
        }

        if (right is not null && width is not null)
        {
            ValidateNonNegative(width.Value, "NegativeFrameSize", "AnchorFrame.Width must be non-negative.");
            return (parent.X + parent.Width - right.Value - width.Value, width.Value);
        }

        if (left is not null && right is not null)
        {
            var resolvedWidth = parent.Width - left.Value - right.Value;
            ValidateNonNegative(resolvedWidth, "NegativeResolvedSize", "Resolved AnchorFrame width must be non-negative.");
            return (parent.X + left.Value, resolvedWidth);
        }

        throw new LayoutError("InvalidAnchorHorizontal", "Unsupported horizontal anchor combination.");
    }

    private static (double Y, double Height) ResolveVertical(Rect parent, AnchorFrame frame)
    {
        var top = ResolveOptional(frame.Top, parent.Height);
        var bottom = ResolveOptional(frame.Bottom, parent.Height);
        var height = ResolveOptional(frame.Height, parent.Height);
        var count = CountNonNull(frame.Top, frame.Bottom, frame.Height);

        if (count != 2)
            throw new LayoutError("InvalidAnchorVertical", "AnchorFrame requires exactly two vertical constraints from Top, Bottom, Height.");

        if (top is not null && height is not null)
        {
            ValidateNonNegative(height.Value, "NegativeFrameSize", "AnchorFrame.Height must be non-negative.");
            return (parent.Y + top.Value, height.Value);
        }

        if (bottom is not null && height is not null)
        {
            ValidateNonNegative(height.Value, "NegativeFrameSize", "AnchorFrame.Height must be non-negative.");
            return (parent.Y + parent.Height - bottom.Value - height.Value, height.Value);
        }

        if (top is not null && bottom is not null)
        {
            var resolvedHeight = parent.Height - top.Value - bottom.Value;
            ValidateNonNegative(resolvedHeight, "NegativeResolvedSize", "Resolved AnchorFrame height must be non-negative.");
            return (parent.Y + top.Value, resolvedHeight);
        }

        throw new LayoutError("InvalidAnchorVertical", "Unsupported vertical anchor combination.");
    }

    private static double? ResolveOptional(UiLength? length, double axisSize)
        => length is null ? null : ResolveLength(length.Value, axisSize);

    private static double ResolveLength(UiLength length, double axisSize)
    {
        ValidateFinite(length.Value, "InvalidFrameNumber", "UiLength value must be finite.");

        return length.Unit switch
        {
            UiLengthUnit.Px => length.Value,
            UiLengthUnit.Ui => length.Value * axisSize,
            _ => throw new LayoutError("InvalidFrameNumber", $"Unsupported UiLengthUnit value: {length.Unit}"),
        };
    }

    private static int CountNonNull(params object?[] values)
        => values.Count(v => v is not null);

    private static void ValidateParent(Rect parent)
    {
        ValidateFinite(parent.X, "InvalidParentRect", "Parent X must be finite.");
        ValidateFinite(parent.Y, "InvalidParentRect", "Parent Y must be finite.");
        ValidateFinite(parent.Width, "InvalidParentRect", "Parent Width must be finite.");
        ValidateFinite(parent.Height, "InvalidParentRect", "Parent Height must be finite.");
        ValidateNonNegative(parent.Width, "InvalidParentRect", "Parent Width must be non-negative.");
        ValidateNonNegative(parent.Height, "InvalidParentRect", "Parent Height must be non-negative.");
    }

    private static void ValidateFinite(double value, string code, string message)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new LayoutError(code, message);
    }

    private static void ValidateNonNegative(double value, string code, string message)
    {
        if (value < 0)
            throw new LayoutError(code, message);
    }
}
