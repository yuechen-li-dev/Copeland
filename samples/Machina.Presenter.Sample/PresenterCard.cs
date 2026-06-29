using System.Text;
using Machina.Core.Authoring;
using Machina.Core.Measurement;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Documents;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;
using Machina.Standard.Authoring;
using Machina.Standard.Components;
using Machina.Standard.Theme;

namespace Machina.Presenter.Sample;

public sealed record PresenterCardFrame(
    string Id,
    Rect Bounds,
    Rect ContentBounds,
    bool ClipContent);

public sealed record PresenterCardOptions(
    double Width,
    double Height,
    bool ClipContent = true,
    double TitleHeight = 24,
    double HeaderGap = 8,
    double BadgeRowHeight = 24,
    double BodyGap = 12,
    double BodyLineHeight = 16,
    double BodyLineGap = 6);

public static class PresenterCard
{
    private const string BodyFrameSuffix = ".body-frame";
    private const string TitleSuffix = ".title";
    private const string BadgeRowSuffix = ".badges";
    private const string BodyStackSuffix = ".body-stack";
    private const string BodyLineSuffixPrefix = ".body-line-";
    private const string Ellipsis = "...";

    public static UiNode BuildTextCard(
        string id,
        string title,
        IReadOnlyList<string> badges,
        IReadOnlyList<string> lines,
        StandardTheme theme,
        PresenterCardOptions options)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(badges);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(options);

        StandardCardStyle cardStyle = theme.Card.Default;
        double innerWidth = Math.Max(0, options.Width - (cardStyle.ContentInset * 2));
        double titleTop = 0;
        double badgesTop = titleTop + options.TitleHeight + options.HeaderGap;
        double bodyTop = badges.Count > 0
            ? badgesTop + options.BadgeRowHeight + options.BodyGap
            : titleTop + options.TitleHeight + options.BodyGap;
        double bodyHeight = Math.Max(0, (options.Height - (cardStyle.ContentInset * 2)) - bodyTop);

        List<UiNode> layoutChildren =
        [
            UI.Anchor(
                UI.Text(
                    title,
                    id: id + TitleSuffix,
                    size: TextSize.Md,
                    color: theme.Colors.Foreground),
                id: id + TitleSuffix + ".slot",
                left: 0,
                right: 0,
                top: titleTop,
                height: options.TitleHeight),
        ];

        if (badges.Count > 0)
        {
            layoutChildren.Add(
                UI.Anchor(
                    UI.Row(
                        id: id + BadgeRowSuffix,
                        gap: 8,
                        children: badges
                            .Select((badge, index) => (UiNode)StandardUI.Badge(
                                badge,
                                id: $"{id}.badge-{index}",
                                theme: theme,
                                variant: BadgeVariant.Secondary))
                            .ToArray()),
                    id: id + BadgeRowSuffix + ".slot",
                    left: 0,
                    right: 0,
                    top: badgesTop,
                    height: options.BadgeRowHeight));
        }

        layoutChildren.Add(
            UI.Anchor(
                BuildBodyFrame(id, lines, theme, options, innerWidth, bodyHeight),
                id: id + BodyFrameSuffix + ".slot",
                left: 0,
                right: 0,
                top: bodyTop,
                bottom: 0));

        return StandardUI.Card(
            id: id,
            theme: theme,
            width: options.Width,
            height: options.Height,
            child: UI.Layer(
                id: id + ".layout",
                children: layoutChildren));
    }

    public static UiNode BuildHostedCard(
        string id,
        string title,
        IReadOnlyList<string> badges,
        UiNode body,
        StandardTheme theme,
        PresenterCardOptions options)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(badges);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(options);

        StandardCardStyle cardStyle = theme.Card.Default;
        double titleTop = 0;
        double badgesTop = titleTop + options.TitleHeight + options.HeaderGap;
        double bodyTop = badges.Count > 0
            ? badgesTop + options.BadgeRowHeight + options.BodyGap
            : titleTop + options.TitleHeight + options.BodyGap;

        List<UiNode> layoutChildren =
        [
            UI.Anchor(
                UI.Text(
                    title,
                    id: id + TitleSuffix,
                    size: TextSize.Md,
                    color: theme.Colors.Foreground),
                id: id + TitleSuffix + ".slot",
                left: 0,
                right: 0,
                top: titleTop,
                height: options.TitleHeight),
        ];

        if (badges.Count > 0)
        {
            layoutChildren.Add(
                UI.Anchor(
                    UI.Row(
                        id: id + BadgeRowSuffix,
                        gap: 8,
                        children: badges
                            .Select((badge, index) => (UiNode)StandardUI.Badge(
                                badge,
                                id: $"{id}.badge-{index}",
                                theme: theme,
                                variant: BadgeVariant.Secondary))
                            .ToArray()),
                    id: id + BadgeRowSuffix + ".slot",
                    left: 0,
                    right: 0,
                    top: badgesTop,
                    height: options.BadgeRowHeight));
        }

        layoutChildren.Add(
            UI.Anchor(
                UI.Rect(
                    child: body,
                    id: id + BodyFrameSuffix,
                    style: new UiStyle(
                        Background: options.ClipContent ? ColorToken.Hex(0x0B1220FF) : null,
                        BorderColor: ColorToken.Hex(0x334155FF),
                        BorderThickness: 1)),
                id: id + BodyFrameSuffix + ".slot",
                left: 0,
                right: 0,
                top: bodyTop,
                bottom: 0));

        return StandardUI.Card(
            id: id,
            theme: theme,
            width: options.Width,
            height: options.Height,
            child: UI.Layer(
                id: id + ".layout",
                children: layoutChildren));
    }

    public static PresenterCardFrame DescribeFrame(ResolvedLayoutDocument resolved, string id, bool clipContent = true)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(id);

        Rect bounds = FindRectBySuffix(resolved, id);
        Rect contentBounds = FindRectBySuffix(resolved, id + BodyFrameSuffix);
        return new PresenterCardFrame(id, bounds, contentBounds, clipContent);
    }

    public static IReadOnlyList<string> ClipBodyLinesToFit(
        IReadOnlyList<string> lines,
        double width,
        double height,
        PresenterCardOptions options,
        ColorToken color)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(options);

        int maxLineCount = ComputeLineCapacity(height, options);
        if (maxLineCount <= 0)
        {
            return [];
        }

        TextStyle style = new(
            Color: color,
            Size: TextSize.Sm,
            AlignX: TextAlignX.Left,
            AlignY: TextAlignY.Top);

        List<string> visibleLines = [];
        foreach (string line in lines)
        {
            if (visibleLines.Count == maxLineCount)
            {
                break;
            }

            visibleLines.Add(ClipLineToWidth("\u2022 " + line, width, style));
        }

        if (visibleLines.Count < lines.Count && visibleLines.Count > 0)
        {
            visibleLines[^1] = ClipLineToWidth(visibleLines[^1] + " " + Ellipsis, width, style);
        }

        return visibleLines;
    }

    private static UiNode BuildBodyFrame(
        string id,
        IReadOnlyList<string> lines,
        StandardTheme theme,
        PresenterCardOptions options,
        double innerWidth,
        double bodyHeight)
    {
        IReadOnlyList<string> visibleLines = ClipBodyLinesToFit(
            lines,
            innerWidth,
            bodyHeight,
            options,
            theme.Colors.MutedForeground);

        UiNode bodyStack = UI.Column(
            id: id + BodyStackSuffix,
            gap: options.BodyLineGap,
            children: visibleLines
                .Select((line, index) => (UiNode)UI.Text(
                    line,
                    id: id + BodyLineSuffixPrefix + index,
                    size: TextSize.Sm,
                    color: theme.Colors.MutedForeground))
                .ToArray());

        return UI.Rect(
            child: bodyStack,
            id: id + BodyFrameSuffix,
            style: new UiStyle(
                Background: options.ClipContent ? ColorToken.Hex(0x0B1220FF) : null,
                BorderColor: ColorToken.Hex(0x334155FF),
                BorderThickness: 1));
    }

    private static int ComputeLineCapacity(double height, PresenterCardOptions options)
    {
        double lineSpan = options.BodyLineHeight + options.BodyLineGap;
        if (height <= 0 || lineSpan <= 0)
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Floor((height + options.BodyLineGap) / lineSpan));
    }

    private static string ClipLineToWidth(string text, double width, TextStyle style)
    {
        if (string.IsNullOrEmpty(text) || width <= 0)
        {
            return string.Empty;
        }

        if (Measure(text, style) <= width)
        {
            return text;
        }

        if (Measure(Ellipsis, style) > width)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (char character in text)
        {
            string candidate = builder.ToString() + character + Ellipsis;
            if (Measure(candidate, style) > width)
            {
                break;
            }

            builder.Append(character);
        }

        return builder.Length == 0
            ? Ellipsis
            : builder.ToString().TrimEnd() + Ellipsis;
    }

    private static double Measure(string text, TextStyle style)
    {
        return DeterministicTextMeasurer.Instance.MeasureText(text, style).Width;
    }

    private static Rect FindRectBySuffix(ResolvedLayoutDocument resolved, string suffix)
    {
        foreach ((NodeId nodeId, ResolvedLayoutNode node) in resolved.Nodes)
        {
            if (nodeId.Value.EndsWith(suffix, StringComparison.Ordinal))
            {
                return node.Rect;
            }
        }

        throw new KeyNotFoundException($"No resolved layout node ended with '{suffix}'.");
    }
}
