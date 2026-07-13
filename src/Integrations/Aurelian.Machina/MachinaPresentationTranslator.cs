using Aurelian.Rendering.Contracts.Resolved2D;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Presentation;
using AurelianFill = Aurelian.Rendering.Contracts.Resolved2D.FillRectangleOperation;
using AurelianPop = Aurelian.Rendering.Contracts.Resolved2D.PopClipOperation;
using AurelianPush = Aurelian.Rendering.Contracts.Resolved2D.PushRectangularClipOperation;
using AurelianStroke = Aurelian.Rendering.Contracts.Resolved2D.StrokeRectangleOperation;
using AurelianText = Aurelian.Rendering.Contracts.Resolved2D.PositionedTextOperation;
using MachinaFill = Machina.Presentation.FillRectangleOperation;
using MachinaPop = Machina.Presentation.PopClipOperation;
using MachinaPush = Machina.Presentation.PushRectangularClipOperation;
using MachinaStroke = Machina.Presentation.StrokeRectangleOperation;
using MachinaText = Machina.Presentation.PositionedTextOperation;

namespace Aurelian.Machina;

/// <summary>
/// Translates immutable Machina presentation intent into Aurelian's resolved 2D contract.
/// </summary>
public static class MachinaPresentationTranslator
{
    /// <summary>
    /// Preserves the source viewport and operation sequence one-for-one.
    /// </summary>
    public static Resolved2DPlan Translate(MachinaPresentationFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var operations = new List<Resolved2DOperation>(frame.Operations.Count);
        for (var operationIndex = 0; operationIndex < frame.Operations.Count; operationIndex++)
        {
            MachinaPresentationOperation operation = frame.Operations[operationIndex]
                ?? throw new InvalidOperationException(
                    $"Machina presentation operation at index {operationIndex} is null.");
            operations.Add(TranslateOperation(operation, operationIndex));
        }

        return new Resolved2DPlan(
            new Resolved2DViewport(frame.Viewport.Width, frame.Viewport.Height),
            operations);
    }

    private static Resolved2DOperation TranslateOperation(
        MachinaPresentationOperation operation,
        int operationIndex)
    {
        return operation switch
        {
            MachinaFill fill => new AurelianFill(
                CreateOperationId(fill.SourceId, operationIndex),
                ToRectangle(fill.Rect),
                ToColor(fill.Color)),
            MachinaStroke stroke => new AurelianStroke(
                CreateOperationId(stroke.SourceId, operationIndex),
                ToRectangle(stroke.Rect),
                ToColor(stroke.Color),
                stroke.Thickness),
            MachinaText text => new AurelianText(
                CreateOperationId(text.SourceId, operationIndex),
                ToRectangle(text.Rect),
                text.Text,
                ToColor(ValidateAndResolveTextColor(text)),
                Resolved2DTextFace.ReadableBitmap5x7,
                ToTextSize(text.Style.Size),
                ToTextAlignX(text.Style.AlignX),
                ToTextAlignY(text.Style.AlignY)),
            MachinaPush clip => new AurelianPush(
                CreateOperationId(clip.SourceId, operationIndex),
                ToRectangle(clip.Rect)),
            MachinaPop => new AurelianPop(CreatePopOperationId(operationIndex)),
            _ => throw new InvalidOperationException(
                $"Unsupported Machina presentation operation '{operation.GetType().FullName}' at index {operationIndex}."),
        };
    }

    private static string CreateOperationId(string sourceId, int operationIndex)
    {
        return $"{sourceId}.{operationIndex}";
    }

    private static string CreatePopOperationId(int operationIndex)
    {
        return $"pop.{operationIndex}";
    }

    private static Resolved2DRectangle ToRectangle(Rect rectangle)
    {
        return new Resolved2DRectangle(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
    }

    private static ColorToken ValidateAndResolveTextColor(MachinaText text)
    {
        ColorToken styleColor = text.Style.Color ?? ColorToken.White;
        if (text.Color != styleColor)
        {
            throw new InvalidOperationException(
                $"Positioned text operation '{text.SourceId}' has a presentation color that does not match its resolved text style color.");
        }

        return text.Color;
    }

    private static Resolved2DRgbaColor ToColor(ColorToken color)
    {
        return new Resolved2DRgbaColor(
            (byte)(color.Rgba >> 24),
            (byte)(color.Rgba >> 16),
            (byte)(color.Rgba >> 8),
            (byte)color.Rgba);
    }

    private static Resolved2DTextSize ToTextSize(TextSize size)
    {
        return size switch
        {
            TextSize.Sm => Resolved2DTextSize.Small,
            TextSize.Md => Resolved2DTextSize.Medium,
            TextSize.H1 => Resolved2DTextSize.Heading,
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, "Unsupported Machina text size."),
        };
    }

    private static Resolved2DTextAlignX ToTextAlignX(TextAlignX alignX)
    {
        return alignX switch
        {
            TextAlignX.Left => Resolved2DTextAlignX.Left,
            TextAlignX.Center => Resolved2DTextAlignX.Center,
            TextAlignX.Right => Resolved2DTextAlignX.Right,
            _ => throw new ArgumentOutOfRangeException(nameof(alignX), alignX, "Unsupported Machina horizontal text alignment."),
        };
    }

    private static Resolved2DTextAlignY ToTextAlignY(TextAlignY alignY)
    {
        return alignY switch
        {
            TextAlignY.Top => Resolved2DTextAlignY.Top,
            TextAlignY.Center => Resolved2DTextAlignY.Center,
            TextAlignY.Bottom => Resolved2DTextAlignY.Bottom,
            _ => throw new ArgumentOutOfRangeException(nameof(alignY), alignY, "Unsupported Machina vertical text alignment."),
        };
    }
}
