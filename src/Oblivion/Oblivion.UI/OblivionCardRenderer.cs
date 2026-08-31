using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Documents;
using Machina.Layout.Geometry;
using Machina.Standard.Authoring;
using Machina.Standard.Components;
using Machina.Standard.Theme;

namespace Oblivion.Product;

public sealed record OblivionCardRenderOptions(
    double Width,
    double Height,
    double TitleHeight = 24,
    double SubtitleHeight = 18,
    double SourceHeight = 18,
    double RowHeight = 24,
    double SmallGap = 6,
    double SectionGap = 10,
    double BodyLineHeight = 18,
    double BodyLineGap = 4,
    int MaxTagsToShow = 4,
    int MaxActionsToShow = 3,
    int MaxArtifactsToShow = 3,
    bool RenderBodyContent = true,
    bool ShowSquareExpansionAffordance = false);

public sealed record OblivionExpandedBodyViewport(
    Rect Bounds,
    double ContentHeight,
    ScrollbarGeometry ScrollbarGeometry);

public static class OblivionCardRenderer
{
    private const string BodyFrameSuffix = ".body-frame";
    private const string HeaderHitSuffix = ".header-hit";
    private const string ExpandedBodyViewportSuffix = ".expanded-body-viewport";
    private const string HeaderStackSuffix = ".header-stack";
    private const string HeaderFrameSuffix = ".header-frame";
    private const string FooterStackSuffix = ".footer-stack";
    private const string FooterFrameSuffix = ".footer-frame";
    private const string BodyTextStackSuffix = ".body-text-stack";
    private const string MetaRowFrameSuffix = ".meta-row-frame";
    private const string TagsRowFrameSuffix = ".tags-row-frame";
    private const string TitleSuffix = ".title";
    private const string ExpansionAffordanceSuffix = ".expansion-affordance";
    private const string ExpansionAffordanceMarkSuffix = ".expansion-affordance-mark";
    private const string SubtitleSuffix = ".subtitle";
    private const string SourceSuffix = ".source";
    private const string MetaRowSuffix = ".meta-row";
    private const string TagsRowSuffix = ".tags-row";
    private const string SummarySuffix = ".summary";
    private const string BodyLineSuffixPrefix = ".body-line-";
    private const string ActionsRowSuffix = ".actions-row";
    private const string ArtifactsRowSuffix = ".artifacts-row";
    private static readonly ColorToken PreviewFrameBackground = ColorToken.Hex(0x0B1220FF);
    private static readonly ColorToken PreviewFrameBorder = ColorToken.Hex(0x334155FF);
    private static readonly ColorToken PreviewBodyForeground = ColorToken.Hex(0xCBD5E1FF);
    private static readonly CardTextLayout BodyTextLayout = new(
        LineHeight: 16,
        LineGap: 6);
    public static OblivionMarkdownReadingStyle MarkdownReadingStyle { get; } = OblivionMarkdownReadingStyle.Default;

    public static UiNode BuildCard(
        OblivionCompactCardView view,
        StandardTheme theme,
        OblivionCardRenderOptions options,
        bool isSelected = false)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(options);

        StandardCardStyle cardStyle = theme.Card.Default;
        OblivionCardCompositionModel composition = BuildCompositionModel(view, options);
        CardLayout layout = ComputeLayout(view, options, cardStyle, composition.ComputeHeaderHeight(options));
        double contentWidth = layout.InnerWidth;

        List<UiStackItem> cardItems =
        [
            UI.StackItem.Fixed(
                main: layout.BodyTop,
                child: WrapSection(
                    BuildHeader(view, theme, options, composition, contentWidth),
                    contentWidth,
                    view.CardId + HeaderFrameSuffix)),
        ];
        if (view.IsExpanded || options.RenderBodyContent)
        {
            cardItems.Add(
                UI.StackItem.Fill(
                    weight: 1,
                    child: BuildBody(view, theme, options, layout, composition.Footer)));
        }

        UiNode cardLayout = UI.VStack(
            id: view.CardId + ".layout",
            children: cardItems);

        UiNode headerHit = UI.Anchor(
            UI.Rect(
                id: view.CardId + HeaderHitSuffix,
                style: new UiStyle(
                    Background: ColorToken.Hex(0x00000000))),
            left: 0,
            width: layout.InnerWidth,
            top: 0,
            height: layout.BodyTop);

        return StandardUI.Card(
            id: view.CardId,
            theme: isSelected ? CreateSelectedTheme(theme) : theme,
            width: options.Width,
            height: options.Height,
            child: UI.Layer(
                children:
                [
                    cardLayout,
                    headerHit,
                ]));
    }

    public static CardFrame DescribeFrame(ResolvedLayoutDocument resolved, string cardId)
    {
        return StandardCard.DescribeFrame(resolved, cardId);
    }

    public static double ComputeBodyTop(OblivionCompactCardView view, OblivionCardRenderOptions options)
    {
        return BuildCompositionModel(view, options).ComputeHeaderHeight(options);
    }

    public static Rect DescribeHeaderHitRect(ResolvedLayoutDocument resolved, string cardId)
    {
        return FindRectBySuffix(resolved, cardId + HeaderHitSuffix);
    }

    public static Rect DescribeExpansionAffordanceRect(ResolvedLayoutDocument resolved, string cardId)
    {
        return FindRectBySuffix(resolved, cardId + ExpansionAffordanceSuffix);
    }

    public static OblivionExpandedBodyViewport? DescribeExpandedBodyViewport(
        ResolvedLayoutDocument resolved,
        OblivionCompactCardView view,
        string cardId)
    {
        if (!view.IsExpanded)
        {
            return null;
        }

        if (view.Body is not OblivionCompactMarkdownBodyContent markdownBody)
        {
            Rect fallbackBounds = FindRectBySuffix(resolved, cardId + BodyFrameSuffix);
            ScrollbarGeometry fallbackScrollbar = ScrollRegion.ComputeScrollbarGeometry(
                new Rect(fallbackBounds.Width, 0, 0, fallbackBounds.Height),
                fallbackBounds.Height,
                fallbackBounds.Height,
                0);
            return new OblivionExpandedBodyViewport(
                fallbackBounds,
                fallbackBounds.Height,
                fallbackScrollbar);
        }

        Rect bounds = FindRectBySuffix(resolved, cardId + ExpandedBodyViewportSuffix);
        double initialContentHeight = OblivionMarkdownRenderer.MeasureExpandedContentHeight(markdownBody.Body, bounds.Width);
        bool needsScrollbar = initialContentHeight > bounds.Height;
        double contentWidth = needsScrollbar
            ? Math.Max(120, bounds.Width - 8 - 8)
            : bounds.Width;
        double contentHeight = OblivionMarkdownRenderer.MeasureExpandedContentHeight(markdownBody.Body, contentWidth);
        ScrollbarGeometry scrollbarGeometry = ScrollRegion.ComputeScrollbarGeometry(
            new Rect(contentWidth + 8, 0, 8, bounds.Height),
            contentHeight,
            bounds.Height,
            view.BodyScrollOffset);

        return new OblivionExpandedBodyViewport(bounds, contentHeight, scrollbarGeometry);
    }

    public static CardLayout ComputeLayout(
        OblivionCompactCardView view,
        OblivionCardRenderOptions options,
        StandardCardStyle cardStyle,
        double bodyTopInContent)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(cardStyle);

        double footerHeight = BuildCompositionModel(view, options).Footer.ComputeRequiredHeight(options);

        return CardLayoutHelper.ComputeLayout(
            options.Width,
            options.Height,
            cardStyle.ContentInset,
            bodyTopInContent,
            footerHeight);
    }

    public static CardLayout ComputeLayout(
        OblivionCard card,
        OblivionCardRenderOptions options,
        StandardCardStyle cardStyle,
        double bodyTopInContent)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(cardStyle);

        return CardLayoutHelper.ComputeLayout(
            options.Width,
            options.Height,
            cardStyle.ContentInset,
            bodyTopInContent);
    }

    private static UiNode BuildBody(
        OblivionCompactCardView view,
        StandardTheme theme,
        OblivionCardRenderOptions options,
        CardLayout layout,
        OblivionCardFooterModel footer)
    {
        if (!options.RenderBodyContent)
        {
            return BuildHostedBodyFrame(view, layout);
        }

        if (view.IsExpanded && view.Body is OblivionCompactMarkdownBodyContent expandedMarkdownBody)
        {
            return BuildExpandedMarkdownBody(view, expandedMarkdownBody.Body, layout);
        }

        if (view.Body is OblivionCompactMarkdownBodyContent collapsedMarkdownBody)
        {
            return BuildCollapsedMarkdownBody(view, collapsedMarkdownBody.Body, theme, options, layout, footer);
        }

        return BuildCollapsedPlainBody(view, theme, options, layout, footer);
    }

    private static UiNode BuildHostedBodyFrame(
        OblivionCompactCardView view,
        CardLayout layout)
    {
        return UI.Rect(
            child: UI.Rect(
                id: view.CardId + ExpandedBodyViewportSuffix,
                style: new UiStyle(
                    Background: MarkdownReadingStyle.Surface,
                    BorderColor: MarkdownReadingStyle.Border,
                    BorderThickness: 1,
                    ClipToBounds: true)),
            id: view.CardId + BodyFrameSuffix,
            width: layout.BodyWidth,
            height: layout.BodyHeight,
            style: new UiStyle(
                Background: PreviewFrameBackground,
                BorderColor: MarkdownReadingStyle.Border,
                BorderThickness: 1));
    }

    private static UiNode BuildHeader(
        OblivionCompactCardView view,
        StandardTheme theme,
        OblivionCardRenderOptions options,
        OblivionCardCompositionModel composition,
        double contentWidth)
    {
        UiNode title = UI.Text(
            view.Title,
            id: view.CardId + TitleSuffix,
            size: options.ShowSquareExpansionAffordance ? TextSize.H1 : TextSize.Md,
            color: theme.Colors.Foreground);
        UiNode titleRow = options.ShowSquareExpansionAffordance
            ? UI.Layer(
                children:
                [
                    UI.Anchor(
                        title,
                        left: 0,
                        right: options.TitleHeight + options.SmallGap,
                        top: 0,
                        height: options.TitleHeight),
                    UI.Anchor(
                        BuildSquareExpansionAffordance(view),
                        left: contentWidth - options.TitleHeight,
                        width: options.TitleHeight,
                        top: 0,
                        height: options.TitleHeight),
                ])
            : title;

        List<UiStackItem> items =
        [
            UI.StackItem.Fixed(
                main: options.TitleHeight,
                child: titleRow),
        ];

        if (!string.IsNullOrWhiteSpace(view.Subtitle))
        {
            items.Add(UI.StackItem.Fixed(main: options.SmallGap, child: UI.VSpace(options.SmallGap)));
            items.Add(
                UI.StackItem.Fixed(
                    main: options.SubtitleHeight,
                    child: UI.Text(
                        view.Subtitle,
                        id: view.CardId + SubtitleSuffix,
                        size: TextSize.Sm,
                        color: theme.Colors.MutedForeground)));
        }

        if (!string.IsNullOrWhiteSpace(view.SourceLabel))
        {
            items.Add(UI.StackItem.Fixed(main: options.SmallGap, child: UI.VSpace(options.SmallGap)));
            items.Add(
                UI.StackItem.Fixed(
                    main: options.SourceHeight,
                    child: UI.Text(
                        view.SourceLabel,
                        id: view.CardId + SourceSuffix,
                        size: TextSize.Sm,
                        color: ColorToken.Hex(0x93C5FDFF))));
        }

        items.Add(UI.StackItem.Fixed(main: options.SectionGap, child: UI.VSpace(options.SectionGap)));
        items.Add(
                UI.StackItem.Fixed(
                    main: options.RowHeight,
                    child: WrapSection(
                        BuildBadgeRow(
                            view.MetaBadges,
                            view.CardId + MetaRowSuffix,
                            view.CardId + ".meta",
                            theme),
                        contentWidth,
                        view.CardId + MetaRowFrameSuffix)));

        if (composition.VisibleTags.Count > 0)
        {
            items.Add(UI.StackItem.Fixed(main: options.SmallGap, child: UI.VSpace(options.SmallGap)));
            items.Add(
                UI.StackItem.Fixed(
                    main: options.RowHeight,
                    child: WrapSection(
                        BuildBadgeRow(
                            composition.VisibleTags,
                            view.CardId + TagsRowSuffix,
                            view.CardId + ".tag",
                            theme),
                        contentWidth,
                        view.CardId + TagsRowFrameSuffix)));
        }

        items.Add(UI.StackItem.Fixed(main: options.SectionGap, child: UI.VSpace(options.SectionGap)));

        return UI.VStack(
            id: view.CardId + HeaderStackSuffix,
            children: items);
    }

    private static UiNode BuildSquareExpansionAffordance(OblivionCompactCardView view)
    {
        ColorToken accent = ColorToken.Hex(0x38BDF8FF);
        UiStyle markStyle = view.IsExpanded
            ? new UiStyle(
                Background: ColorToken.Hex(0x00000000),
                BorderColor: accent,
                BorderThickness: 2)
            : new UiStyle(Background: accent);

        return UI.Rect(
            id: view.CardId + ExpansionAffordanceSuffix,
            child: UI.Anchor(
                UI.Rect(
                    id: view.CardId + ExpansionAffordanceMarkSuffix,
                    style: markStyle),
                left: 13,
                width: 14,
                top: 13,
                height: 14),
            style: new UiStyle(
                Background: ColorToken.Hex(0x111827FF),
                BorderColor: accent,
                BorderThickness: 1));
    }

    private static UiNode BuildCollapsedMarkdownBody(
        OblivionCompactCardView view,
        OblivionCardBody body,
        StandardTheme theme,
        OblivionCardRenderOptions options,
        CardLayout layout,
        OblivionCardFooterModel footer)
    {
        string summary = !string.IsNullOrWhiteSpace(view.SummaryLine)
            ? view.SummaryLine!
            : OblivionMarkdownBody.Project(body).Preview.FirstOrDefault() ?? "<empty markdown body>";

        IReadOnlyList<string> lines = WrapLinesToFit(
            [summary],
            layout.BodyWidth,
            layout.BodyHeight,
            options,
            PreviewBodyForeground);

        return BuildCollapsedPreviewBody(view, lines, theme, options, layout, footer, highlightFirstLine: true);
    }

    private static UiNode BuildCollapsedPlainBody(
        OblivionCompactCardView view,
        StandardTheme theme,
        OblivionCardRenderOptions options,
        CardLayout layout,
        OblivionCardFooterModel footer)
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

        return BuildCollapsedPreviewBody(view, visibleLines, theme, options, layout, footer, highlightFirstLine: false);
    }

    private static UiNode BuildCollapsedPreviewBody(
        OblivionCompactCardView view,
        IReadOnlyList<string> visibleLines,
        StandardTheme theme,
        OblivionCardRenderOptions options,
        CardLayout layout,
        OblivionCardFooterModel footer,
        bool highlightFirstLine)
    {
        List<UiStackItem> bodyItems =
        [
            UI.StackItem.Fill(
                weight: 1,
                child: BuildPreviewTextStack(view, visibleLines, theme, options, highlightFirstLine)),
        ];

        if (footer.Rows.Count > 0)
        {
            bodyItems.Add(
                UI.StackItem.Fixed(
                    main: footer.ComputeRequiredHeight(options),
                    child: WrapSection(
                        BuildFooter(view, theme, options, layout.BodyWidth, footer),
                        layout.BodyWidth,
                        view.CardId + FooterFrameSuffix)));
        }

        return UI.Rect(
            child: UI.VStack(
                id: view.CardId + ".body-layout",
                children: bodyItems),
            id: view.CardId + BodyFrameSuffix,
            style: new UiStyle(
                Background: PreviewFrameBackground,
                BorderColor: PreviewFrameBorder,
                BorderThickness: 1));
    }

    private static UiNode BuildExpandedMarkdownBody(
        OblivionCompactCardView view,
        OblivionCardBody body,
        CardLayout layout)
    {
        double viewportHeight = Math.Max(120, layout.BodyHeight);
        OblivionMarkdownRenderer.OblivionExpandedMarkdownBodyRenderResult bodyRender = OblivionMarkdownRenderer.BuildExpandedBody(
            view.CardId,
            body,
            MarkdownReadingStyle,
            layout.BodyWidth,
            viewportHeight,
            view.BodyScrollOffset);

        return UI.Rect(
            child: UI.Rect(
                child: bodyRender.Node,
                id: view.CardId + ExpandedBodyViewportSuffix,
                style: new UiStyle(
                    Background: MarkdownReadingStyle.Surface,
                    BorderColor: MarkdownReadingStyle.Border,
                    BorderThickness: 1,
                    ClipToBounds: true)),
            id: view.CardId + BodyFrameSuffix,
            style: new UiStyle(
                Background: PreviewFrameBackground,
                BorderColor: MarkdownReadingStyle.Border,
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

        return CardLayoutHelper.WrapOrClipLinesToFit(
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

    private static Rect FindRectBySuffix(ResolvedLayoutDocument resolved, string suffix)
    {
        foreach ((Machina.Layout.Rows.NodeId nodeId, ResolvedLayoutNode node) in resolved.Nodes)
        {
            if (nodeId.Value.EndsWith(suffix, StringComparison.Ordinal))
            {
                return node.Rect;
            }
        }

        throw new KeyNotFoundException($"No resolved layout node ended with '{suffix}'.");
    }

    private static UiNode BuildPreviewTextStack(
        OblivionCompactCardView view,
        IReadOnlyList<string> lines,
        StandardTheme theme,
        OblivionCardRenderOptions options,
        bool highlightFirstLine)
    {
        List<UiNode> children = [];
        double currentTop = 0;

        foreach ((string line, int index) in lines.Select((line, index) => (line, index)))
        {
            children.Add(
                UI.Anchor(
                    UI.Text(
                        line,
                        id: view.CardId + BodyLineSuffixPrefix + index,
                        size: TextSize.Sm,
                        color: highlightFirstLine && index == 0
                            ? theme.Colors.Foreground
                            : PreviewBodyForeground),
                    left: 0,
                    right: 0,
                    top: currentTop,
                    height: options.BodyLineHeight));
            currentTop += options.BodyLineHeight + options.BodyLineGap;
        }

        return UI.Layer(
            id: view.CardId + BodyTextStackSuffix,
            children: children);
    }

    private static UiNode BuildFooter(
        OblivionCompactCardView view,
        StandardTheme theme,
        OblivionCardRenderOptions options,
        double contentWidth,
        OblivionCardFooterModel footer)
    {
        return UI.VStack(
            id: view.CardId + FooterStackSuffix,
            gap: options.SmallGap,
            children: footer.Rows
                .Select(row => UI.StackItem.Fixed(
                    main: options.RowHeight,
                    child: WrapSection(
                        BuildBadgeRow(
                            row.Labels,
                            view.CardId + row.RowSuffix,
                            $"{view.CardId}{row.BadgeIdPrefix}",
                            theme),
                        contentWidth,
                        $"{view.CardId}{row.RowSuffix}-frame")))
                .ToArray());
    }

    private static UiNode BuildBadgeRow(
        IReadOnlyList<string> labels,
        string rowId,
        string badgeIdPrefix,
        StandardTheme theme)
    {
        return UI.Row(
            id: rowId,
            gap: 8,
            children: labels
                .Select((label, index) => BuildBadge(label, $"{badgeIdPrefix}-{index}", theme))
                .ToArray());
    }

    private static UiNode WrapSection(UiNode child, double width, string id)
    {
        return UI.Rect(
            id: id,
            width: width,
            child: child);
    }

    private static OblivionCardCompositionModel BuildCompositionModel(
        OblivionCompactCardView view,
        OblivionCardRenderOptions options)
    {
        return new OblivionCardCompositionModel(
            !string.IsNullOrWhiteSpace(view.Subtitle),
            !string.IsNullOrWhiteSpace(view.SourceLabel),
            LimitLabels(view.Tags, options.MaxTagsToShow),
            BuildFooterModel(view, options));
    }

    private static OblivionCardFooterModel BuildFooterModel(
        OblivionCompactCardView view,
        OblivionCardRenderOptions options)
    {
        if (view.IsExpanded)
        {
            return OblivionCardFooterModel.Empty;
        }

        List<OblivionBadgeRowModel> rows = [];
        IReadOnlyList<string> actionLabels = LimitLabels(view.ActionBadges, options.MaxActionsToShow);
        IReadOnlyList<string> artifactLabels = LimitLabels(view.ArtifactBadges, options.MaxArtifactsToShow);

        if (actionLabels.Count > 0)
        {
            rows.Add(new OblivionBadgeRowModel(ActionsRowSuffix, ".action", actionLabels));
        }

        if (artifactLabels.Count > 0)
        {
            rows.Add(new OblivionBadgeRowModel(ArtifactsRowSuffix, ".artifact", artifactLabels));
        }

        return new OblivionCardFooterModel(rows);
    }

    private sealed record OblivionCardCompositionModel(
        bool VisibleSubtitle,
        bool VisibleSource,
        IReadOnlyList<string> VisibleTags,
        OblivionCardFooterModel Footer)
    {
        public double ComputeHeaderHeight(OblivionCardRenderOptions options)
        {
            double height = options.TitleHeight;

            if (VisibleSubtitle)
            {
                height += options.SmallGap + options.SubtitleHeight;
            }

            if (VisibleSource)
            {
                height += options.SmallGap + options.SourceHeight;
            }

            height += options.SectionGap + options.RowHeight;

            if (VisibleTags.Count > 0)
            {
                height += options.SmallGap + options.RowHeight;
            }

            return height + options.SectionGap;
        }
    }

    private sealed record OblivionCardFooterModel(
        IReadOnlyList<OblivionBadgeRowModel> Rows)
    {
        public static OblivionCardFooterModel Empty { get; } = new([]);

        public double ComputeRequiredHeight(OblivionCardRenderOptions options)
        {
            if (Rows.Count == 0)
            {
                return 0;
            }

            return (Rows.Count * options.RowHeight) + ((Rows.Count - 1) * options.SmallGap);
        }
    }

    private sealed record OblivionBadgeRowModel(
        string RowSuffix,
        string BadgeIdPrefix,
        IReadOnlyList<string> Labels);
}
