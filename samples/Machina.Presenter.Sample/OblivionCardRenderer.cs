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
    double BodyLineHeight = 16,
    double BodyLineGap = 6,
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
    private const string BodyStackSuffix = ".body-stack";
    private const string BodyLineSuffixPrefix = ".body-line-";
    private const string ActionsRowSuffix = ".actions-row";
    private const string ArtifactsRowSuffix = ".artifacts-row";
    private static readonly PresenterCardTextLayout BodyTextLayout = new(
        LineHeight: 16,
        LineGap: 6);

    public static UiNode BuildCard(
        OblivionCard card,
        StandardTheme theme,
        OblivionCardRenderOptions options,
        bool isSelected = false)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(options);

        double cursorTop = 0;

        List<UiNode> layoutChildren =
        [
            UI.Anchor(
                UI.Text(
                    card.Title,
                    id: card.Id.Value + TitleSuffix,
                    size: TextSize.Md,
                    color: theme.Colors.Foreground),
                id: card.Id.Value + TitleSuffix + ".slot",
                left: 0,
                right: 0,
                top: cursorTop,
                height: options.TitleHeight),
        ];

        cursorTop += options.TitleHeight;

        if (!string.IsNullOrWhiteSpace(card.Subtitle))
        {
            cursorTop += options.SmallGap;
            layoutChildren.Add(
                UI.Anchor(
                    UI.Text(
                        card.Subtitle,
                        id: card.Id.Value + SubtitleSuffix,
                        size: TextSize.Sm,
                        color: theme.Colors.MutedForeground),
                    id: card.Id.Value + SubtitleSuffix + ".slot",
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
                    id: card.Id.Value + MetaRowSuffix,
                    gap: 8,
                    children: BuildMetaBadges(card, theme)),
                id: card.Id.Value + MetaRowSuffix + ".slot",
                left: 0,
                right: 0,
                top: cursorTop,
                height: options.RowHeight));
        cursorTop += options.RowHeight;

        IReadOnlyList<string> tags = LimitLabels(card.Tags, options.MaxTagsToShow);
        if (tags.Count > 0)
        {
            cursorTop += options.SmallGap;
            layoutChildren.Add(
                UI.Anchor(
                    UI.Row(
                        id: card.Id.Value + TagsRowSuffix,
                        gap: 8,
                        children: tags
                            .Select((tag, index) => BuildBadge(tag, $"{card.Id.Value}.tag-{index}", theme))
                            .ToArray()),
                    id: card.Id.Value + TagsRowSuffix + ".slot",
                    left: 0,
                    right: 0,
                    top: cursorTop,
                    height: options.RowHeight));
            cursorTop += options.RowHeight;
        }

        cursorTop += options.SectionGap;
        PresenterCardLayout layout = ComputeLayout(card, options, theme.Card.Default, cursorTop);
        double bodyContainerHeight = layout.BodyRectInContent.Height + (layout.FooterRectInContent?.Height ?? 0);
        layoutChildren.Add(
            UI.Anchor(
                BuildBody(card, theme, options, layout),
                id: card.Id.Value + BodyFrameSuffix + ".slot",
                left: layout.BodyRectInContent.X,
                width: layout.BodyRectInContent.Width,
                top: layout.BodyRectInContent.Y,
                height: bodyContainerHeight));

        return StandardUI.Card(
            id: card.Id.Value,
            theme: isSelected ? CreateSelectedTheme(theme) : theme,
            width: options.Width,
            height: options.Height,
            child: UI.Layer(
                id: card.Id.Value + ".layout",
                children: layoutChildren));
    }

    public static PresenterCardFrame DescribeFrame(ResolvedLayoutDocument resolved, string cardId)
    {
        return PresenterCard.DescribeFrame(resolved, cardId);
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
        OblivionCard card,
        StandardTheme theme,
        OblivionCardRenderOptions options,
        PresenterCardLayout layout)
    {
        IReadOnlyList<string> actionLabels = LimitLabels(
            card.Actions.Select(action => $"{action.Label} {(action.Enabled ? "ready" : "disabled")}").ToArray(),
            options.MaxActionsToShow);
        IReadOnlyList<string> artifactLabels = LimitLabels(
            card.Artifacts.Select(artifact => $"{artifact.Label} ({artifact.Kind})").ToArray(),
            options.MaxArtifactsToShow);

        List<UiNode> children = [];
        double currentTop = 0;

        if (card.Body.Format == OblivionCardBodyFormat.CopelandMarkdown)
        {
            children.Add(
                UI.Anchor(
                    OblivionMarkdownRenderer.BuildPreviewBody(
                        card.Id.Value,
                        card.Body,
                        theme,
                        layout.BodyWidth,
                        layout.BodyHeight),
                    id: $"{card.Id.Value}.markdown-preview.slot",
                    left: 0,
                    width: layout.BodyWidth,
                    top: 0,
                    height: layout.BodyHeight));
        }
        else
        {
            IReadOnlyList<string> visibleLines = ClipLinesToFit(
                card.BodyLines,
                layout.BodyWidth,
                layout.BodyHeight,
                options,
                theme.Colors.MutedForeground);

            foreach ((string line, int index) in visibleLines.Select((line, index) => (line, index)))
            {
                children.Add(
                    UI.Anchor(
                        UI.Text(
                            line,
                            id: card.Id.Value + BodyLineSuffixPrefix + index,
                            size: TextSize.Sm,
                            color: theme.Colors.MutedForeground),
                        id: $"{card.Id.Value}{BodyLineSuffixPrefix}{index}.slot",
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
                        id: card.Id.Value + ActionsRowSuffix,
                        gap: 8,
                        children: actionLabels
                            .Select((label, index) => BuildBadge(label, $"{card.Id.Value}.action-{index}", theme))
                            .ToArray()),
                    id: card.Id.Value + ActionsRowSuffix + ".slot",
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
                        id: card.Id.Value + ArtifactsRowSuffix,
                        gap: 8,
                        children: artifactLabels
                            .Select((label, index) => BuildBadge(label, $"{card.Id.Value}.artifact-{index}", theme))
                            .ToArray()),
                    id: card.Id.Value + ArtifactsRowSuffix + ".slot",
                    left: 0,
                    right: 0,
                    top: footerCursorTop,
                    height: options.RowHeight));
        }

        return UI.Rect(
            child: UI.Layer(
                id: card.Id.Value + ".body-layout",
                children: children),
            id: card.Id.Value + BodyFrameSuffix,
            style: new UiStyle(
                Background: ColorToken.Hex(0x0B1220FF),
                BorderColor: ColorToken.Hex(0x334155FF),
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

    private static UiNode[] BuildMetaBadges(OblivionCard card, StandardTheme theme)
    {
        List<UiNode> badges =
        [
            BuildBadge($"{KindLabel(card.Kind)}", card.Id.Value + ".kind", theme),
            BuildBadge($"{StatusLabel(card.Status)}", card.Id.Value + ".status", theme),
        ];

        if (card.Body.Format == OblivionCardBodyFormat.CopelandMarkdown)
        {
            badges.Add(BuildBadge("Markdown body", card.Id.Value + ".markdown", theme));
        }

        if (card.Body.Diagnostics.Count > 0)
        {
            badges.Add(BuildBadge($"Diagnostics {card.Body.Diagnostics.Count}", card.Id.Value + ".diagnostics", theme));
        }

        return badges.ToArray();
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

    private static IReadOnlyList<string> ClipLinesToFit(
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

        return PresenterCardLayoutHelper.ClipLinesToFit(
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

    private static string KindLabel(OblivionCardKind kind)
    {
        return kind switch
        {
            OblivionCardKind.Note => "Note",
            OblivionCardKind.Status => "Status",
            OblivionCardKind.UiPreview => "UI Preview",
            OblivionCardKind.Artifact => "Artifact",
            OblivionCardKind.CodeFact => "Code Fact",
            OblivionCardKind.CodeTheory => "Code Theory",
            _ => kind.ToString(),
        };
    }

    private static string StatusLabel(OblivionCardStatus status)
    {
        return status switch
        {
            OblivionCardStatus.Idle => "Idle",
            OblivionCardStatus.Passing => "Passing",
            OblivionCardStatus.Failing => "Failing",
            OblivionCardStatus.Warning => "Warning",
            OblivionCardStatus.Deferred => "Deferred",
            OblivionCardStatus.Placeholder => "Placeholder",
            _ => status.ToString(),
        };
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
