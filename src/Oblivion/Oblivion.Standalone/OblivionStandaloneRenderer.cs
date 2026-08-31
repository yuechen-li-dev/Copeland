using Aurelian.Machina;
using Aurelian.Rendering.Raster;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Pipeline;
using Machina.Standard.Theme;
using Oblivion.Product;

namespace Oblivion.Standalone;

public static class OblivionStandaloneRenderer
{
    public static OblivionStandaloneSurfaceSnapshot Render(
        int width,
        int viewportHeight,
        IReadOnlyList<OblivionStandaloneCardPresentation> cards,
        OblivionStandaloneStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(cards);
        OblivionStandaloneStyle effectiveStyle = style ?? OblivionStandaloneStyles.Dark;
        StandardTheme theme = CreateTheme(effectiveStyle);
        double cardWidth = Math.Max(640, width - (effectiveStyle.OuterHorizontalMargin * 2));
        double[] cardHeights = cards
            .Select(card => card.CardView.IsExpanded
                ? effectiveStyle.ExpandedCardHeight
                : effectiveStyle.CollapsedCardHeight)
            .ToArray();
        int pageContentHeight = (int)Math.Ceiling(Math.Max(
            viewportHeight,
            (effectiveStyle.OuterVerticalMargin * 2) + cardHeights.Sum() +
                (effectiveStyle.StackGap * Math.Max(0, cards.Count - 1))));

        List<UiStackItem> stackItems = [];
        foreach ((OblivionStandaloneCardPresentation card, int index) in cards.Select((card, index) => (card, index)))
        {
            OblivionCardRenderOptions cardOptions = CreateCardOptions(
                effectiveStyle,
                cardWidth,
                cardHeights[index]);
            StandardTheme cardTheme = card.IsSelected
                ? CreateSelectedTheme(effectiveStyle, theme)
                : theme;
            UiNode cardNode = OblivionCardRenderer.BuildCard(
                card.CardView,
                cardTheme,
                cardOptions);
            stackItems.Add(UI.StackItem.Fixed(cardHeights[index], cardNode));
        }

        double stackHeight = cardHeights.Sum() +
            (effectiveStyle.StackGap * Math.Max(0, cards.Count - 1));
        UiNode cardStack = UI.VStack(
            id: "m19h.page.card-stack",
            gap: effectiveStyle.StackGap,
            children: stackItems.ToArray());
        UiNode document = UI.Rect(
            id: "m19h.page",
            width: width,
            height: pageContentHeight,
            child: UI.Anchor(
                cardStack,
                id: "m19h.page.card-stack-anchor",
                left: effectiveStyle.OuterHorizontalMargin,
                width: cardWidth,
                top: effectiveStyle.OuterVerticalMargin,
                height: stackHeight),
            style: new UiStyle(Background: effectiveStyle.PageBackground));

        MachinaPreparedPresentation prepared = new MachinaPresentationPipeline().Prepare(
            document,
            width,
            pageContentHeight);
        RasterFrame shellFrame = new AurelianCpuRasterRenderer().Render(
            MachinaPresentationTranslator.Translate(prepared.PresentationFrame));

        List<OblivionStandaloneCardSnapshot> snapshots = [];
        double cardY = effectiveStyle.OuterVerticalMargin;
        foreach ((OblivionStandaloneCardPresentation card, int index) in cards.Select((card, index) => (card, index)))
        {
            Machina.Layout.Geometry.Rect cardBounds = new(
                effectiveStyle.OuterHorizontalMargin,
                cardY,
                cardWidth,
                cardHeights[index]);
            Machina.Layout.Geometry.Rect affordanceBounds = OblivionCardRenderer.DescribeExpansionAffordanceRect(
                prepared.Resolved,
                card.Card.Id.Value);
            Machina.Layout.Geometry.Rect? matureContentBounds = null;
            if (card.CardView.IsExpanded)
            {
                matureContentBounds = OblivionCardRenderer.DescribeExpandedBodyViewport(
                    prepared.Resolved,
                    card.CardView,
                    card.Card.Id.Value)?.Bounds;
            }

            snapshots.Add(new OblivionStandaloneCardSnapshot(
                card.Card,
                card.CardView,
                card.ContentPlan,
                card.IsSelected,
                cardBounds,
                affordanceBounds,
                matureContentBounds));
            cardY += cardHeights[index] + effectiveStyle.StackGap;
        }

        return new OblivionStandaloneSurfaceSnapshot(
            width,
            viewportHeight,
            pageContentHeight,
            shellFrame,
            snapshots);
    }

    private static OblivionCardRenderOptions CreateCardOptions(
        OblivionStandaloneStyle style,
        double width,
        double height)
    {
        return new OblivionCardRenderOptions(
            Width: width,
            Height: height,
            TitleHeight: 40,
            SubtitleHeight: 22,
            RowHeight: 28,
            SmallGap: 8,
            SectionGap: 14,
            RenderBodyContent: false,
            ShowSquareExpansionAffordance: true,
            HostedBodyBackground: style.DocumentSurface,
            HostedBodyBorder: style.DocumentBorder,
            ExpansionAffordanceBackground: style.AffordanceSurface,
            ExpansionAffordanceAccent: style.AffordanceAccent);
    }

    private static StandardTheme CreateSelectedTheme(
        OblivionStandaloneStyle style,
        StandardTheme theme)
    {
        return theme with
        {
            Card = theme.Card with
            {
                Default = theme.Card.Default with
                {
                    BorderColor = style.SelectedCardBorder,
                    BorderThickness = Math.Max(2, theme.Card.Default.BorderThickness),
                },
            },
        };
    }

    private static StandardTheme CreateTheme(OblivionStandaloneStyle style)
    {
        StandardTheme baseline = StandardTheme.Default;
        StandardColors colors = baseline.Colors with
        {
            Background = style.CardBackground,
            Foreground = style.PrimaryText,
            Muted = style.BadgeSurface,
            MutedForeground = style.SecondaryText,
            Border = style.CardBorder,
            Secondary = style.BadgeSurface,
            SecondaryForeground = style.BadgeText,
        };

        return baseline with
        {
            Colors = colors,
            Badge = baseline.Badge with
            {
                Secondary = baseline.Badge.Secondary with
                {
                    Background = colors.Muted,
                    Foreground = colors.SecondaryForeground,
                    BorderColor = style.BadgeBorder,
                    BorderThickness = 1,
                    TextStyle = baseline.Badge.Secondary.TextStyle with
                    {
                        Color = colors.SecondaryForeground,
                    },
                },
            },
            Card = baseline.Card with
            {
                Default = baseline.Card.Default with
                {
                    Background = style.CardBackground,
                    Foreground = colors.Foreground,
                    BorderColor = style.CardBorder,
                    ContentInset = 24,
                },
            },
        };
    }
}
