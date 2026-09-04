namespace Aurelian.Rendering.Contracts.Resolved2D;

/// <summary>
/// A resolved, ordered renderer operation. The supported concrete operations
/// are deliberately limited to the current 2D CPU raster capability.
/// </summary>
public abstract record Resolved2DOperation
{
    private protected Resolved2DOperation(string operationId)
    {
        OperationId = string.IsNullOrWhiteSpace(operationId)
            ? throw new ArgumentException("Renderer operation identity must not be empty or whitespace.", nameof(operationId))
            : operationId;
    }

    public string OperationId { get; }
}

public sealed record FillRectangleOperation : Resolved2DOperation
{
    public FillRectangleOperation(string operationId, Resolved2DRectangle rectangle, Resolved2DRgbaColor color)
        : base(operationId)
    {
        Rectangle = rectangle;
        Color = color;
    }

    public Resolved2DRectangle Rectangle { get; }

    public Resolved2DRgbaColor Color { get; }
}

public enum Resolved2DAnalyticShapeKind
{
    RoundedRect,
    Circle,
    Pill,
}

public sealed record AnalyticShapeOperation : Resolved2DOperation
{
    public AnalyticShapeOperation(
        string operationId,
        Resolved2DAnalyticShapeKind kind,
        Resolved2DRectangle rectangle,
        Resolved2DRgbaColor fillColor,
        double radius,
        Resolved2DRgbaColor borderColor,
        double borderWidth)
        : base(operationId)
    {
        if (!Enum.IsDefined(kind) || !double.IsFinite(radius) || radius < 0
            || !double.IsFinite(borderWidth) || borderWidth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Analytic shape parameters are invalid.");
        }
        Kind = kind;
        Rectangle = rectangle;
        FillColor = fillColor;
        Radius = radius;
        BorderColor = borderColor;
        BorderWidth = borderWidth;
    }

    public Resolved2DAnalyticShapeKind Kind { get; }
    public Resolved2DRectangle Rectangle { get; }
    public Resolved2DRgbaColor FillColor { get; }
    public double Radius { get; }
    public Resolved2DRgbaColor BorderColor { get; }
    public double BorderWidth { get; }
}

public sealed record StrokeRectangleOperation : Resolved2DOperation
{
    public StrokeRectangleOperation(
        string operationId,
        Resolved2DRectangle rectangle,
        Resolved2DRgbaColor color,
        double thickness)
        : base(operationId)
    {
        if (!double.IsFinite(thickness) || thickness <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(thickness), thickness, "Stroke thickness must be finite and greater than zero.");
        }

        Rectangle = rectangle;
        Color = color;
        Thickness = thickness;
    }

    public Resolved2DRectangle Rectangle { get; }

    public Resolved2DRgbaColor Color { get; }

    public double Thickness { get; }
}

public sealed record PositionedTextOperation : Resolved2DOperation
{
    public PositionedTextOperation(
        string operationId,
        Resolved2DRectangle bounds,
        string text,
        Resolved2DRgbaColor color,
        Resolved2DTextFace face = Resolved2DTextFace.ReadableBitmap5x7,
        Resolved2DTextSize size = Resolved2DTextSize.Medium,
        Resolved2DTextAlignX alignX = Resolved2DTextAlignX.Left,
        Resolved2DTextAlignY alignY = Resolved2DTextAlignY.Top)
        : base(operationId)
    {
        if (!Enum.IsDefined(face))
        {
            throw new ArgumentOutOfRangeException(nameof(face), face, "Text face is not supported.");
        }

        if (!Enum.IsDefined(size))
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "Text size is not supported.");
        }

        if (!Enum.IsDefined(alignX))
        {
            throw new ArgumentOutOfRangeException(nameof(alignX), alignX, "Horizontal text alignment is not supported.");
        }

        if (!Enum.IsDefined(alignY))
        {
            throw new ArgumentOutOfRangeException(nameof(alignY), alignY, "Vertical text alignment is not supported.");
        }

        Bounds = bounds;
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Color = color;
        Face = face;
        Size = size;
        AlignX = alignX;
        AlignY = alignY;
    }

    public Resolved2DRectangle Bounds { get; }

    public string Text { get; }

    public Resolved2DRgbaColor Color { get; }

    public Resolved2DTextFace Face { get; }

    public Resolved2DTextSize Size { get; }

    public Resolved2DTextAlignX AlignX { get; }

    public Resolved2DTextAlignY AlignY { get; }
}

public sealed record PushRectangularClipOperation : Resolved2DOperation
{
    public PushRectangularClipOperation(string operationId, Resolved2DRectangle rectangle)
        : base(operationId)
    {
        Rectangle = rectangle;
    }

    public Resolved2DRectangle Rectangle { get; }
}

public sealed record PopClipOperation : Resolved2DOperation
{
    public PopClipOperation(string operationId)
        : base(operationId)
    {
    }
}
