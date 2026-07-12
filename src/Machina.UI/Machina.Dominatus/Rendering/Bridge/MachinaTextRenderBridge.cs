using Machina.Core.Styling;
using Machina.Dominatus.Rendering.Commands;
using Machina.Layout.Geometry;
using Machina.Standard.Text;

namespace Machina.Dominatus.Rendering.Bridge;

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
}

public static class MachinaTextRenderBridge
{
    public static IReadOnlyList<DrawTextCommand> ToDrawTextCommands(
        string idPrefix,
        MachinaTextLayoutResult layout,
        MachinaTextRenderStyle? style = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idPrefix);
        ArgumentNullException.ThrowIfNull(layout);

        var renderStyle = style ?? MachinaTextRenderStyle.Default;
        var commands = new List<DrawTextCommand>();

        foreach (var line in layout.Lines)
        {
            for (var runIndex = 0; runIndex < line.Runs.Count; runIndex++)
            {
                var run = line.Runs[runIndex];
                if (string.IsNullOrWhiteSpace(run.Text))
                {
                    continue;
                }

                var commandStyle = ResolveTextStyle(run.Style, renderStyle);
                var rect = new Rect(run.Bounds.X, run.Bounds.Y, run.Bounds.Width, run.Bounds.Height);
                commands.Add(new DrawTextCommand(
                    $"{idPrefix}.b{line.BlockIndex}.l{line.LineIndex}.r{runIndex}",
                    rect,
                    run.Text,
                    commandStyle));
            }
        }

        return commands;
    }

    private static TextStyle ResolveTextStyle(MachinaTextRunStyle runStyle, MachinaTextRenderStyle renderStyle)
    {
        var color = runStyle.LinkHref is not null && renderStyle.LinkColor is not null
            ? renderStyle.LinkColor
            : renderStyle.BaseStyle.Color;

        return renderStyle.BaseStyle with
        {
            Color = color,
            Size = MapTextSize(runStyle.Variant, renderStyle),
            AlignX = TextAlignX.Left,
            AlignY = TextAlignY.Top,
        };
    }

    private static TextSize MapTextSize(MachinaTextVariant variant, MachinaTextRenderStyle renderStyle)
    {
        return variant switch
        {
            MachinaTextVariant.Title => renderStyle.TitleSize,
            MachinaTextVariant.Body => renderStyle.BodySize,
            MachinaTextVariant.Label => renderStyle.LabelSize,
            MachinaTextVariant.Caption => renderStyle.CaptionSize,
            MachinaTextVariant.Mono => renderStyle.MonoSize,
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "Unsupported Machina text variant."),
        };
    }
}
