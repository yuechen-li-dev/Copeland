using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Documents;
using Machina.Layout.Geometry;
using Machina.Standard.Authoring;
using Machina.Standard.Components;
using Machina.Standard.Theme;

namespace Machina.Presenter.Sample;

public sealed record OblivionCardRenderOptions(
    double Width,
    double Height,
    double TitleHeight = 24,
    double SubtitleHeight = 18,
    double RowHeight = 24,
    double SmallGap = 6,
    double SectionGap = 10,
    double BodyLineHeight = 18,
    double BodyLineGap = 4,
    int MaxTagsToShow = 4,
    int MaxActionsToShow = 3,
    int MaxArtifactsToShow = 3);

public static class OblivionCardRenderer
{
    private const string BodyFrameSuffix = ".body-frame";
    private const string TitleSuffix = ".title";
    private const string SubtitleSuffix = ".subtitle";
    private const string MetaRowSuffix = ".meta-row";
    private const string TagsRowSuffix = ".tags-row";
    private const string BodyLineSuffixPrefix = ".body-line-";
    private const string ActionsRowSuffix = ".actions-row";
    private const string ArtifactsRowSuffix = ".artifacts-row";
    private static readonly ColorToken PreviewFrameBackground = ColorToken.Hex(0x0B1220FF);
    private static readonly ColorToken PreviewFrameBorder = ColorToken.Hex(0x334155FF);
    private static readonly ColorToken PreviewBodyForeground = ColorToken.Hex(0xCBD5E1FF);
    private static readonly PresenterCardTextLayout BodyTextLayout = new(
        LineHeight: 16,
        LineGap: 6);

    public static UiNode BuildCard(
        OblivionCompactCardView view,
        StandardTheme theme,
        OblivionCardRenderOptions options,
        bool isSelected = false)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(options);

        double cursorTop = 0;

        List<UiNode> layoutChildren =
        [
            UI.Anchor(
                UI.Text(
                    view.Title,
                    id: view.CardId + TitleSuffix,
                    size: TextSize.Md,
                    color: theme.Colors.Foreground),
                id: view.CardId + TitleSuffix + ".slot",
                left: 0,
                right: 0,
                top: cursorTop,
                height: options.TitleHeight),
        ];

        cursorTop += options.TitleHeight;

        if (!string.IsNullOrWhiteSpace(view.Subtitle))
        {
            cursorTop += options.SmallGap;
            layoutChildren.Add(
                UI.Anchor(
                    UI.Text(
                        view.Subtitle,
                        id: view.CardId + SubtitleSuffix,
                        size: TextSize.Sm,
                        color: theme.Colors.MutedForeground),
                    id: view.CardId + SubtitleSuffix + ".slot",
                    left: 0,
                    right: 0,
                    top: cursorTop,
                    height: options.SubtitleHeight));
            cursorTop += options.SubtitleHeight;
        }

        cursorTop += options.SectionGap;
        layoutChildren.Add(
            UI.Anchor(
                UI.Row(
                    id: view.CardId + MetaRowSuffix,
                    gap: 8,
                    children: BuildBadges(view.MetaBadges, view.CardId + ".meta", theme)),
                id: view.CardId + MetaRowSuffix + ".slot",
                left: 0,
                right: 0,
                top: cursorTop,
                height: options.RowHeight));
        cursorTop += options.RowHeight;

        IReadOnlyList<string> tags = LimitLabels(view.Tags, options.MaxTagsToShow);
        if (tags.Count > 0)
        {
            cursorTop += options.SmallGap;
            layoutChildren.Add(
                UI.Anchor(
                    UI.Row(
                        id: view.CardId + TagsRowSuffix,
                        gap: 8,
                        children: tags
                            .Select((tag, index) => BuildBadge(tag, $"{view.CardId}.tag-{index}", theme))
                            .ToArray()),
                    id: view.CardId + TagsRowSuffix + ".slot",
                    left: 0,
                    right: 0,
                    top: cursorTop,
                    height: options.RowHeight));
            cursorTop += options.RowHeight;
        }

        cursorTop += options.SectionGap;
        PresenterCardLayout layout = ComputeLayout(view, options, theme.Card.Default, cursorTop);
        double bodyContainerHeight = layout.BodyRectInContent.Height + (layout.FooterRectInContent?.Height ?? 0);
        layoutChildren.Add(
            UI.Anchor(
                BuildBody(view, theme, options, layout),
                id: view.CardId + BodyFrameSuffix + ".slot",
                left: layout.BodyRectInContent.X,
                width: layout.BodyRectInContent.Width,
                top: layout.BodyRectInContent.Y,
                height: bodyContainerHeight));

        return StandardUI.Card(
            id: view.CardId,
            theme: isSelected ? CreateSelectedTheme(theme) : theme,
            width: options.Width,
            height: options.Height,
            child: UI.Layer(
                id: view.CardId + ".layout",
                children: layoutChildren));
    }

    public static PresenterCardFrame DescribeFrame(ResolvedLayoutDocument resolved, string cardId)
    {
        return PresenterCard.DescribeFrame(resolved, cardId);
    }

    public static PresenterCardLayout ComputeLayout(
        OblivionCompactCardView view,
        OblivionCardRenderOptions options,
        StandardCardStyle cardStyle,
        double bodyTopInContent)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(cardStyle);

        double footerHeight = 0;
        if (view.ActionBadges.Count > 0)
        {
            footerHeight += options.RowHeight + options.SmallGap;
        }

        if (view.ArtifactBadges.Count > 0)
        {
            footerHeight += options.RowHeight + options.SmallGap;
        }

        return PresenterCardLayoutHelper.ComputeLayout(
            options.Width,
            options.Height,
            cardStyle.ContentInset,
            bodyTopInContent,
            footerHeight);
    }

    public static PresenterCardLayout ComputeLayout(
        OblivionCard card,
        OblivionCardRenderOptions options,
        StandardCardStyle cardStyle,
        double bodyTopInContent)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(cardStyle);

        double footerHeight = 0;
        if (card.Actions.Count > 0)
        {
            footerHeight += options.RowHeight + options.SmallGap;
        }

        if (card.Artifacts.Count > 0)
        {
            footerHeight += options.RowHeight + options.SmallGap;
        }

        return PresenterCardLayoutHelper.ComputeLayout(
            options.Width,
            options.Height,
            cardStyle.ContentInset,
            bodyTopInContent,
            footerHeight);
    }

    private static UiNode BuildBody(
        OblivionCompactCardView view,
        StandardTheme theme,
        OblivionCardRenderOptions options,
        PresenterCardLayout layout)
    {
        IReadOnlyList<string> actionLabels = LimitLabels(view.ActionBadges, options.MaxActionsToShow);
        IReadOnlyList<string> artifactLabels = LimitLabels(view.ArtifactBadges, options.MaxArtifactsToShow);

        List<UiNode> children = [];
        double currentTop = 0;

        if (view.Body is OblivionCompactMarkdownBodyContent markdownBody)
        {
            children.Add(
                UI.Anchor(
                    OblivionMarkdownRenderer.BuildPreviewBody(
                        view.CardId,
                        markdownBody.Body,
                        theme,
                        layout.BodyWidth,
                        layout.BodyHeight),
                    id: $"{view.CardId}.markdown-preview.slot",
                    left: 0,
                    width: layout.BodyWidth,
                    top: 0,
                    height: layout.BodyHeight));
        }
        else
        {
            IReadOnlyList<string> lines = view.Body is OblivionCompactPlainBodyContent plainBody
                ? plainBody.Lines
                : [];
            IReadOnlyList<string> visibleLines = WrapLinesToFit(
                lines,
                layout.BodyWidth,
                layout.BodyHeight,
                options,
                PreviewBodyForeground);

            foreach ((string line, int index) in visibleLines.Select((line, index) => (line, index)))
            {
                children.Add(
                    UI.Anchor(
                        UI.Text(
                            line,
                            id: view.CardId + BodyLineSuffixPrefix + index,
                            size: TextSize.Sm,
                            color: PreviewBodyForeground),
                        id: $"{view.CardId}{BodyLineSuffixPrefix}{index}.slot",
                        left: 0,
                        right: 0,
                        top: currentTop,
                        height: options.BodyLineHeight));
                currentTop += options.BodyLineHeight + options.BodyLineGap;
            }
        }

        double footerCursorTop = Math.Max(0, (layout.FooterRectInContent?.Y ?? layout.BodyHeight) - layout.BodyRectInContent.Y);

        if (actionLabels.Count > 0)
        {
            children.Add(
                UI.Anchor(
                    UI.Row(
                        id: view.CardId + ActionsRowSuffix,
                        gap: 8,
                        children: actionLabels
                            .Select((label, index) => BuildBadge(label, $"{view.CardId}.action-{index}", theme))
                            .ToArray()),
                    id: view.CardId + ActionsRowSuffix + ".slot",
                    left: 0,
                    right: 0,
                    top: footerCursorTop,
                    height: options.RowHeight));
            footerCursorTop += options.RowHeight + options.SmallGap;
        }

        if (artifactLabels.Count > 0)
        {
            children.Add(
                UI.Anchor(
                    UI.Row(
                        id: view.CardId + ArtifactsRowSuffix,
                        gap: 8,
                        children: artifactLabels
                            .Select((label, index) => BuildBadge(label, $"{view.CardId}.artifact-{index}", theme))
                            .ToArray()),
                    id: view.CardId + ArtifactsRowSuffix + ".slot",
                    left: 0,
                    right: 0,
                    top: footerCursorTop,
                    height: options.RowHeight));
        }

        return UI.Rect(
            child: UI.Layer(
                id: view.CardId + ".body-layout",
                children: children),
            id: view.CardId + BodyFrameSuffix,
            style: new UiStyle(
                Background: PreviewFrameBackground,
                BorderColor: PreviewFrameBorder,
                BorderThickness: 1));
    }

    private static UiNode BuildBadge(string label, string id, StandardTheme theme)
    {
        return StandardUI.Badge(
            label,
            id: id,
            theme: theme,
            variant: BadgeVariant.Secondary);
    }

    private static UiNode[] BuildBadges(
        IReadOnlyList<string> labels,
        string idPrefix,
        StandardTheme theme)
    {
        return labels
            .Select((label, index) => BuildBadge(label, $"{idPrefix}-{index}", theme))
            .ToArray();
    }

    private static IReadOnlyList<string> LimitLabels(IReadOnlyList<string> values, int maxToShow)
    {
        if (values.Count <= maxToShow)
        {
            return values;
        }

        List<string> limited = values.Take(maxToShow).ToList();
        limited.Add($"+{values.Count - maxToShow}");
        return limited;
    }

    private static IReadOnlyList<string> WrapLinesToFit(
        IReadOnlyList<string> lines,
        double width,
        double height,
        OblivionCardRenderOptions options,
        ColorToken color)
    {
        TextStyle style = new(
            Color: color,
            Size: TextSize.Sm,
            AlignX: TextAlignX.Left,
            AlignY: TextAlignY.Top);

        return PresenterCardLayoutHelper.WrapOrClipLinesToFit(
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

    private static StandardTheme CreateSelectedTheme(StandardTheme theme)
    {
        return theme with
        {
            Card = theme.Card with
            {
                Default = theme.Card.Default with
                {
                    BorderColor = ColorToken.Hex(0x2563EBFF),
                    Background = ColorToken.Hex(0xF8FBFFFF),
                },
            },
        };
    }
}
