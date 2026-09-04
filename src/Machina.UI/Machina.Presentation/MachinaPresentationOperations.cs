using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.VectorAssets;
using Machina.Core.Assets;

namespace Machina.Presentation;

public abstract record MachinaPresentationOperation;

public sealed record MachinaVectorIconPresentationPrimitive : MachinaPresentationOperation
{
    public MachinaVectorIconPresentationPrimitive(
        string sourceId,
        MachinaVectorIconId icon,
        Rect destinationRect,
        ColorToken tint,
        Rect? clipRect = null)
    {
        SourceId = MachinaPresentationValidation.ValidateSourceId(sourceId);
        DestinationRect = MachinaPresentationValidation.ValidateRect(destinationRect, nameof(destinationRect));
        if (destinationRect.Width <= 0 || destinationRect.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(destinationRect), "Vector icon destination dimensions must be positive.");
        }
        if (clipRect is Rect clip)
        {
            MachinaPresentationValidation.ValidateRect(clip, nameof(clipRect));
        }
        Icon = icon;
        Tint = tint;
        ClipRect = clipRect;
    }

    public string SourceId { get; }

    public MachinaVectorIconId Icon { get; }

    public Rect DestinationRect { get; }

    public ColorToken Tint { get; }

    public Rect? ClipRect { get; }
}

public enum MachinaAnalyticShapeKind
{
    RoundedRect,
    Circle,
    Pill,
}

public sealed record MachinaAnalyticShapePrimitive : MachinaPresentationOperation
{
    public MachinaAnalyticShapePrimitive(
        string sourceId,
        MachinaAnalyticShapeKind kind,
        Rect destinationRect,
        ColorToken fillColor,
        double radius = 0,
        ColorToken? borderColor = null,
        double borderWidth = 0)
    {
        SourceId = MachinaPresentationValidation.ValidateSourceId(sourceId);
        DestinationRect = MachinaPresentationValidation.ValidateRect(destinationRect, nameof(destinationRect));
        if (destinationRect.Width <= 0 || destinationRect.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(destinationRect), "Analytic shape dimensions must be positive.");
        }
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        if (kind == MachinaAnalyticShapeKind.Circle && destinationRect.Width != destinationRect.Height)
        {
            throw new ArgumentException("Circle destination bounds must be square.", nameof(destinationRect));
        }
        if (!double.IsFinite(radius) || radius < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius must be finite and non-negative.");
        }
        if (!double.IsFinite(borderWidth) || borderWidth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(borderWidth), borderWidth, "Border width must be finite and non-negative.");
        }
        Kind = kind;
        FillColor = fillColor;
        BorderColor = borderColor;
        BorderWidth = Math.Min(borderWidth, Math.Min(destinationRect.Width, destinationRect.Height) / 2);
        Radius = kind switch
        {
            MachinaAnalyticShapeKind.Circle => destinationRect.Width / 2,
            MachinaAnalyticShapeKind.Pill => Math.Min(destinationRect.Width, destinationRect.Height) / 2,
            _ => Math.Min(radius, Math.Min(destinationRect.Width, destinationRect.Height) / 2),
        };
    }

    public string SourceId { get; }
    public MachinaAnalyticShapeKind Kind { get; }
    public Rect DestinationRect { get; }
    public ColorToken FillColor { get; }
    public ColorToken? BorderColor { get; }
    public double BorderWidth { get; }
    public double Radius { get; }
}

public sealed record FillRectangleOperation : MachinaPresentationOperation
{
    public FillRectangleOperation(string sourceId, Rect rect, ColorToken color)
    {
        SourceId = MachinaPresentationValidation.ValidateSourceId(sourceId);
        Rect = MachinaPresentationValidation.ValidateRect(rect, nameof(rect));
        Color = color;
    }

    public string SourceId { get; }

    public Rect Rect { get; }

    public ColorToken Color { get; }
}

public sealed record StrokeRectangleOperation : MachinaPresentationOperation
{
    public StrokeRectangleOperation(string sourceId, Rect rect, ColorToken color, double thickness)
    {
        SourceId = MachinaPresentationValidation.ValidateSourceId(sourceId);
        Rect = MachinaPresentationValidation.ValidateRect(rect, nameof(rect));
        Color = color;

        if (!double.IsFinite(thickness) || thickness <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(thickness), thickness, "Stroke thickness must be finite and greater than zero.");
        }

        Thickness = thickness;
    }

    public string SourceId { get; }

    public Rect Rect { get; }

    public ColorToken Color { get; }

    public double Thickness { get; }
}

public sealed record PositionedTextOperation : MachinaPresentationOperation
{
    public PositionedTextOperation(
        string sourceId,
        Rect rect,
        string text,
        TextStyle style,
        ColorToken color,
        MachinaTextPresentationPrimitive? primitive = null)
    {
        SourceId = MachinaPresentationValidation.ValidateSourceId(sourceId);
        Rect = MachinaPresentationValidation.ValidateRect(rect, nameof(rect));
        Text = string.IsNullOrWhiteSpace(text)
            ? throw new ArgumentException("Positioned text must not be empty or whitespace.", nameof(text))
            : text;
        Style = style ?? throw new ArgumentNullException(nameof(style));
        Color = color;
        Primitive = primitive;

        if (primitive is not null && !string.Equals(primitive.GlyphRun.Text, Text, StringComparison.Ordinal))
        {
            throw new ArgumentException("The presentation glyph run text must match the semantic text.", nameof(primitive));
        }
    }

    public string SourceId { get; }

    public Rect Rect { get; }

    public string Text { get; }

    /// <summary>
    /// Machina typography and placement policy already resolved by UI preparation.
    /// </summary>
    public TextStyle Style { get; }

    /// <summary>
    /// The presentation color resolved from the Machina text style.
    /// </summary>
    public ColorToken Color { get; }

    /// <summary>
    /// Optional qualified glyph realization. Existing UI remains raster/pixel by default when absent.
    /// </summary>
    public MachinaTextPresentationPrimitive? Primitive { get; }

    public MachinaTextRenderingMode RenderingMode =>
        Primitive?.RenderingMode ?? MachinaTextRenderingMode.RasterPixel;
}

public sealed record PushRectangularClipOperation : MachinaPresentationOperation
{
    public PushRectangularClipOperation(string sourceId, Rect rect)
    {
        SourceId = MachinaPresentationValidation.ValidateSourceId(sourceId);
        Rect = MachinaPresentationValidation.ValidateRect(rect, nameof(rect));
    }

    public string SourceId { get; }

    public Rect Rect { get; }
}

public sealed record PopClipOperation : MachinaPresentationOperation;

internal static class MachinaPresentationValidation
{
    public static string ValidateSourceId(string sourceId)
    {
        return string.IsNullOrWhiteSpace(sourceId)
            ? throw new ArgumentException("Presentation operation source identity must not be empty or whitespace.", nameof(sourceId))
            : sourceId;
    }

    public static Rect ValidateRect(Rect rect, string parameterName)
    {
        if (!double.IsFinite(rect.X) ||
            !double.IsFinite(rect.Y) ||
            !double.IsFinite(rect.Width) ||
            !double.IsFinite(rect.Height))
        {
            throw new ArgumentException("Presentation geometry must contain only finite values.", parameterName);
        }

        return rect;
    }
}
