using Machina.Core.Styling;
using Machina.Dominatus.Rendering.Commands;
using Machina.Presentation;
using Machina.Standard.Text;

namespace Machina.Dominatus.Rendering.Bridge;

/// <summary>
/// Transitional compatibility style for the legacy text-command API. Remove in JTF-M5.
/// </summary>
public sealed record MachinaTextRenderStyle(
    TextStyle BaseStyle,
    ColorToken? LinkColor = null,
    TextSize TitleSize = TextSize.H1,
    TextSize BodySize = TextSize.Md,
    TextSize LabelSize = TextSize.Sm,
    TextSize CaptionSize = TextSize.Sm,
    TextSize MonoSize = TextSize.Sm)
{
    public static MachinaTextRenderStyle Default { get; } = new(new TextStyle());

    internal MachinaTextPresentationStyle ToPresentationStyle()
    {
        return new MachinaTextPresentationStyle(
            BaseStyle,
            LinkColor,
            TitleSize,
            BodySize,
            LabelSize,
            CaptionSize,
            MonoSize);
    }
}

/// <summary>
/// Transitional compatibility surface. Presentation preparation is owned by Machina.Presentation.
/// </summary>
public static class MachinaTextRenderBridge
{
    public static IReadOnlyList<DrawTextCommand> ToDrawTextCommands(
        string idPrefix,
        MachinaTextLayoutResult layout,
        MachinaTextRenderStyle? style = null)
    {
        IReadOnlyList<PositionedTextOperation> operations = MachinaTextPresentationBuilder.Build(
            idPrefix,
            layout,
            (style ?? MachinaTextRenderStyle.Default).ToPresentationStyle());

        return operations
            .Select(operation => new DrawTextCommand(
                operation.SourceId,
                operation.Rect,
                operation.Text,
                operation.Style))
            .ToArray();
    }
}
