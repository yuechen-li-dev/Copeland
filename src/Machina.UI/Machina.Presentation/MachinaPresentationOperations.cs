using Machina.Core.Styling;
using Machina.Layout.Geometry;

namespace Machina.Presentation;

public abstract record MachinaPresentationOperation;

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
    public PositionedTextOperation(string sourceId, Rect rect, string text, TextStyle style, ColorToken color)
    {
        SourceId = MachinaPresentationValidation.ValidateSourceId(sourceId);
        Rect = MachinaPresentationValidation.ValidateRect(rect, nameof(rect));
        Text = string.IsNullOrWhiteSpace(text)
            ? throw new ArgumentException("Positioned text must not be empty or whitespace.", nameof(text))
            : text;
        Style = style ?? throw new ArgumentNullException(nameof(style));
        Color = color;
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
