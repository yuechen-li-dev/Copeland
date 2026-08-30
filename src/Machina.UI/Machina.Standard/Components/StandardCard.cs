using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Documents;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;
using Machina.Standard.Authoring;
using Machina.Standard.Theme;

namespace Machina.Standard.Components;

public sealed record CardFrame(
    string Id,
    Rect Bounds,
    Rect ContentBounds,
    bool ClipContent);

public sealed record StandardCardOptions(
    double Width,
    double Height,
    bool ClipContent = true,
    double TitleHeight = 24,
    double HeaderGap = 8,
    double BadgeRowHeight = 24,
    double BodyGap = 12,
    double BodyLineHeight = 16,
    double BodyLineGap = 6);

public static class StandardCard
{
    private const string BodyFrameSuffix = ".body-frame";
    private const string TitleSuffix = ".title";
    private const string BadgeRowSuffix = ".badges";
    private const string BodyStackSuffix = ".body-stack";
    private const string BodyLineSuffixPrefix = ".body-line-";
    private static readonly CardTextLayout BodyTextLayout = new(
        LineHeight: 16,
        LineGap: 6,
        Prefix: "\u2022 ");

    public static UiNode BuildTextCard(
        string id,
        string title,
        IReadOnlyList<string> badges,
        IReadOnlyList<string> lines,
        StandardTheme theme,
        StandardCardOptions options)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(badges);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(options);

        CardLayout layout = ComputeTextLayout(options, theme.Card.Default, badges.Count);
        double titleTop = layout.HeaderRectInContent.Y;
        double badgesTop = titleTop + options.TitleHeight + options.HeaderGap;

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
                BuildBodyFrame(id, lines, theme, options, layout),
                id: id + BodyFrameSuffix + ".slot",
                left: layout.BodyRectInContent.X,
                width: layout.BodyRectInContent.Width,
                top: layout.BodyRectInContent.Y,
                height: layout.BodyRectInContent.Height));

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
        StandardCardOptions options)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(badges);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(options);

        CardLayout layout = ComputeHostedLayout(options, theme.Card.Default, badges.Count);
        double titleTop = layout.HeaderRectInContent.Y;
        double badgesTop = titleTop + options.TitleHeight + options.HeaderGap;

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
                        Background: null,
                        BorderColor: ColorToken.Hex(0x334155FF),
                        BorderThickness: 1)),
                id: id + BodyFrameSuffix + ".slot",
                left: layout.BodyRectInContent.X,
                width: layout.BodyRectInContent.Width,
                top: layout.BodyRectInContent.Y,
                height: layout.BodyRectInContent.Height));

        return StandardUI.Card(
            id: id,
            theme: theme,
            width: options.Width,
            height: options.Height,
            child: UI.Layer(
                id: id + ".layout",
                children: layoutChildren));
    }

    public static CardLayout ComputeTextLayout(
        StandardCardOptions options,
        StandardCardStyle cardStyle,
        int badgeCount)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(cardStyle);

        double titleTop = 0;
        double badgesTop = titleTop + options.TitleHeight + options.HeaderGap;
        double bodyTop = badgeCount > 0
            ? badgesTop + options.BadgeRowHeight + options.BodyGap
            : titleTop + options.TitleHeight + options.BodyGap;
        return CardLayoutHelper.ComputeLayout(
            options.Width,
            options.Height,
            cardStyle.ContentInset,
            bodyTop);
    }

    public static CardLayout ComputeHostedLayout(
        StandardCardOptions options,
        StandardCardStyle cardStyle,
        int badgeCount)
    {
        return ComputeTextLayout(options, cardStyle, badgeCount);
    }

    public static CardFrame DescribeFrame(ResolvedLayoutDocument resolved, string id, bool clipContent = true)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(id);

        Rect bounds = FindRectBySuffix(resolved, id);
        Rect contentBounds = FindRectBySuffix(resolved, id + BodyFrameSuffix);
        return new CardFrame(id, bounds, contentBounds, clipContent);
    }

    public static IReadOnlyList<string> ClipBodyLinesToFit(
        IReadOnlyList<string> lines,
        double width,
        double height,
        StandardCardOptions options,
        ColorToken color)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(options);

        TextStyle style = new(
            Color: color,
            Size: TextSize.Sm,
            AlignX: TextAlignX.Left,
            AlignY: TextAlignY.Top);

        return CardLayoutHelper.ClipLinesToFit(
            lines,
            width,
            height,
            BodyTextLayout with
            {
                LineHeight = options.BodyLineHeight,
                LineGap = options.BodyLineGap,
            },
            style);
    }

    private static UiNode BuildBodyFrame(
        string id,
        IReadOnlyList<string> lines,
        StandardTheme theme,
        StandardCardOptions options,
        CardLayout layout)
    {
        IReadOnlyList<string> visibleLines = ClipBodyLinesToFit(
            lines,
            layout.BodyWidth,
            layout.BodyHeight,
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
