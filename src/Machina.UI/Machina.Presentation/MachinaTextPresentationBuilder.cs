using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Standard.Text;

namespace Machina.Presentation;

public sealed record MachinaTextPresentationStyle(
    TextStyle BaseStyle,
    ColorToken? LinkColor = null,
    TextSize TitleSize = TextSize.H1,
    TextSize BodySize = TextSize.Md,
    TextSize LabelSize = TextSize.Sm,
    TextSize CaptionSize = TextSize.Sm,
    TextSize MonoSize = TextSize.Sm)
{
    public static MachinaTextPresentationStyle Default { get; } = new(new TextStyle());
}

public static class MachinaTextPresentationBuilder
{
    public static IReadOnlyList<PositionedTextOperation> Build(
        string sourceIdPrefix,
        MachinaTextLayoutResult layout,
        MachinaTextPresentationStyle? style = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceIdPrefix);
        ArgumentNullException.ThrowIfNull(layout);

        var presentationStyle = style ?? MachinaTextPresentationStyle.Default;
        ArgumentNullException.ThrowIfNull(presentationStyle.BaseStyle);

        var operations = new List<PositionedTextOperation>();
        foreach (var line in layout.Lines)
        {
            for (var runIndex = 0; runIndex < line.Runs.Count; runIndex++)
            {
                var run = line.Runs[runIndex];
                if (string.IsNullOrWhiteSpace(run.Text))
                {
                    continue;
                }

                TextStyle textStyle = ResolveTextStyle(run.Style, presentationStyle);
                var rect = new Rect(run.Bounds.X, run.Bounds.Y, run.Bounds.Width, run.Bounds.Height);
                operations.Add(new PositionedTextOperation(
                    $"{sourceIdPrefix}.b{line.BlockIndex}.l{line.LineIndex}.r{runIndex}",
                    rect,
                    run.Text,
                    textStyle,
                    ResolveColor(textStyle)));
            }
        }

        return operations;
    }

    private static TextStyle ResolveTextStyle(MachinaTextRunStyle runStyle, MachinaTextPresentationStyle presentationStyle)
    {
        var color = runStyle.LinkHref is not null && presentationStyle.LinkColor is not null
            ? presentationStyle.LinkColor
            : presentationStyle.BaseStyle.Color;

        return presentationStyle.BaseStyle with
        {
            Color = color,
            Size = MapTextSize(runStyle.Variant, presentationStyle),
            AlignX = TextAlignX.Left,
            AlignY = TextAlignY.Top,
        };
    }

    private static TextSize MapTextSize(MachinaTextVariant variant, MachinaTextPresentationStyle presentationStyle)
    {
        return variant switch
        {
            MachinaTextVariant.Title => presentationStyle.TitleSize,
            MachinaTextVariant.Body => presentationStyle.BodySize,
            MachinaTextVariant.Label => presentationStyle.LabelSize,
            MachinaTextVariant.Caption => presentationStyle.CaptionSize,
            MachinaTextVariant.Mono => presentationStyle.MonoSize,
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "Unsupported Machina text variant."),
        };
    }

    internal static ColorToken ResolveColor(TextStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return style.Color ?? ColorToken.White;
    }
}
