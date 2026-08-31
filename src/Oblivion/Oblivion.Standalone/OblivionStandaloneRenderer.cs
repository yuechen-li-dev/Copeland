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
    private static readonly OblivionStandaloneStyle Style = OblivionStandaloneStyles.M19h;
    private static readonly StandardTheme Theme = CreateTheme();

    public static OblivionStandaloneSurfaceSnapshot Render(
        int width,
        int viewportHeight,
        IReadOnlyList<OblivionStandaloneCardPresentation> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        if (cards.Count != 2)
        {
            throw new ArgumentException(
                $"The M19h renderer requires exactly two cards, but received {cards.Count}.",
                nameof(cards));
        }

        double cardWidth = Math.Max(640, width - (Style.OuterHorizontalMargin * 2));
        double[] cardHeights = cards
            .Select(card => card.CardView.IsExpanded ? Style.ExpandedCardHeight : Style.CollapsedCardHeight)
            .ToArray();
        int pageContentHeight = (int)Math.Ceiling(Math.Max(
            viewportHeight,
            (Style.OuterVerticalMargin * 2) + cardHeights.Sum() + (Style.StackGap * (cards.Count - 1))));

        List<UiStackItem> stackItems = [];
        foreach ((OblivionStandaloneCardPresentation card, int index) in cards.Select((card, index) => (card, index)))
        {
            OblivionCardRenderOptions cardOptions = CreateCardOptions(cardWidth, cardHeights[index]);
            StandardTheme cardTheme = card.IsSelected
                ? CreateSelectedTheme()
                : Theme;
            UiNode cardNode = OblivionCardRenderer.BuildCard(
                card.CardView,
                cardTheme,
                cardOptions);
            stackItems.Add(UI.StackItem.Fixed(cardHeights[index], cardNode));
        }

        double stackHeight = cardHeights.Sum() + (Style.StackGap * (cards.Count - 1));
        UiNode cardStack = UI.VStack(
            id: "m19h.page.card-stack",
            gap: Style.StackGap,
            children: stackItems.ToArray());
        UiNode document = UI.Rect(
            id: "m19h.page",
            width: width,
            height: pageContentHeight,
            child: UI.Anchor(
                cardStack,
                id: "m19h.page.card-stack-anchor",
                left: Style.OuterHorizontalMargin,
                width: cardWidth,
                top: Style.OuterVerticalMargin,
                height: stackHeight),
            style: new UiStyle(Background: Style.PageBackground));

        MachinaPreparedPresentation prepared = new MachinaPresentationPipeline().Prepare(
            document,
            width,
            pageContentHeight);
        RasterFrame shellFrame = new AurelianCpuRasterRenderer().Render(
            MachinaPresentationTranslator.Translate(prepared.PresentationFrame));

        List<OblivionStandaloneCardSnapshot> snapshots = [];
        double cardY = Style.OuterVerticalMargin;
        foreach ((OblivionStandaloneCardPresentation card, int index) in cards.Select((card, index) => (card, index)))
        {
            Machina.Layout.Geometry.Rect cardBounds = new(
                Style.OuterHorizontalMargin,
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
            cardY += cardHeights[index] + Style.StackGap;
        }

        return new OblivionStandaloneSurfaceSnapshot(
            width,
            viewportHeight,
            pageContentHeight,
            shellFrame,
            snapshots);
    }

    private static OblivionCardRenderOptions CreateCardOptions(double width, double height)
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
            ShowSquareExpansionAffordance: true);
    }

    private static StandardTheme CreateSelectedTheme()
    {
        return Theme with
        {
            Card = Theme.Card with
            {
                Default = Theme.Card.Default with
                {
                    BorderColor = Style.SelectedCardBorder,
                    BorderThickness = Math.Max(2, Theme.Card.Default.BorderThickness),
                },
            },
        };
    }

    private static StandardTheme CreateTheme()
    {
        StandardTheme baseline = StandardTheme.Default;
        StandardColors colors = baseline.Colors with
        {
            Background = ColorToken.Hex(0x0B1220FF),
            Foreground = ColorToken.Hex(0xF8FAFCFF),
            Muted = ColorToken.Hex(0x1E293BFF),
            MutedForeground = ColorToken.Hex(0xA8B8CCFF),
            Border = Style.CardBorder,
            Secondary = ColorToken.Hex(0x1E293BFF),
            SecondaryForeground = ColorToken.Hex(0xE2E8F0FF),
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
                    BorderColor = colors.Border,
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
                    Background = Style.CardBackground,
                    Foreground = colors.Foreground,
                    BorderColor = Style.CardBorder,
                    ContentInset = 24,
                },
            },
        };
    }
}
