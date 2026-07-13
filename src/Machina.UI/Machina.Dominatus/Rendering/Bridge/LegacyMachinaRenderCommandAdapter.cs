using Dominatus.Core.Runtime;
using Machina.Dominatus.Rendering.Commands;
using Machina.Presentation;

namespace Machina.Dominatus.Rendering.Bridge;

/// <summary>
/// Temporary JTF-M2 adapter from Machina presentation intent to legacy Dominatus commands.
/// It performs no UI traversal, layout, text preparation, or style interpretation.
/// Scheduled for removal with the Dominatus render path in JTF-M5.
/// </summary>
public static class LegacyMachinaRenderCommandAdapter
{
    public static IReadOnlyList<IActuationCommand> ToLegacyCommands(MachinaPresentationFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var commands = new List<IActuationCommand>(frame.Operations.Count + 2)
        {
            new BeginFrameCommand(frame.Viewport.Width, frame.Viewport.Height),
        };

        foreach (MachinaPresentationOperation operation in frame.Operations)
        {
            commands.Add(ToLegacyCommand(operation));
        }

        commands.Add(new EndFrameCommand());
        return commands;
    }

    private static IActuationCommand ToLegacyCommand(MachinaPresentationOperation operation)
    {
        return operation switch
        {
            FillRectangleOperation fill => new FillRectCommand(fill.SourceId, fill.Rect, fill.Color),
            StrokeRectangleOperation stroke => new StrokeRectCommand(
                stroke.SourceId,
                stroke.Rect,
                stroke.Color,
                stroke.Thickness),
            PositionedTextOperation text => new DrawTextCommand(
                text.SourceId,
                text.Rect,
                text.Text,
                text.Style),
            PushRectangularClipOperation clip => new PushClipCommand(clip.SourceId, clip.Rect),
            PopClipOperation => new PopClipCommand(),
            _ => throw new InvalidOperationException(
                $"Unsupported Machina presentation operation '{operation.GetType().FullName}'."),
        };
    }
}
