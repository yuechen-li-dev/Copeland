using Aurelian.Machina;
using Aurelian.Rendering.Raster;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Pipeline;
using Machina.Standard.Theme;
using Oblivion.Model;
using Oblivion.Product;

namespace Oblivion.Standalone;

public static class OblivionStandaloneRenderer
{
    public const int DevelopmentWidth = 2560;
    public const int DevelopmentHeight = 1440;
    public const double OuterHorizontalMargin = 88;
    public const double OuterVerticalMargin = 72;
    public const double StackGap = 24;
    public const double CollapsedCardHeight = 174;
    public const double ExpandedCardHeight = 960;
    public const double MaximumReadableWidth = 1040;

    private static readonly StandardTheme Theme = CreateTheme();

    public static OblivionStandaloneSurfaceSnapshot Render(
        int width,
        int height,
        OblivionCard card,
        OblivionCompactCardView cardView,
        OblivionContentPresentationPlan contentPlan)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(cardView);
        ArgumentNullException.ThrowIfNull(contentPlan);

        double cardWidth = Math.Max(640, width - (OuterHorizontalMargin * 2));
        double cardHeight = cardView.IsExpanded
            ? Math.Min(
                Math.Max(520, height - (OuterVerticalMargin * 2)),
                ExpandedCardHeight)
            : CollapsedCardHeight;
        OblivionCardRenderOptions cardOptions = new(
            Width: cardWidth,
            Height: cardHeight,
            TitleHeight: 40,
            SubtitleHeight: 22,
            RowHeight: 28,
            SmallGap: 8,
            SectionGap: 14,
            RenderBodyContent: false,
            ShowSquareExpansionAffordance: true);
        UiNode cardNode = OblivionCardRenderer.BuildCard(
            cardView,
            Theme,
            cardOptions);
        UiNode cardStack = UI.VStack(
            id: "m19g.page.card-stack",
            gap: StackGap,
            children:
            [
                UI.StackItem.Fixed(cardHeight, cardNode),
            ]);
        UiNode document = UI.Rect(
            id: "m19g.page",
            width: width,
            height: height,
            child: UI.Anchor(
                cardStack,
                id: "m19g.page.card-stack-anchor",
                left: OuterHorizontalMargin,
                width: cardWidth,
                top: OuterVerticalMargin,
                height: cardHeight),
            style: new UiStyle(Background: ColorToken.Hex(0x050914FF)));

        MachinaPreparedPresentation prepared = new MachinaPresentationPipeline().Prepare(document, width, height);
        RasterFrame shellFrame = new AurelianCpuRasterRenderer().Render(
            MachinaPresentationTranslator.Translate(prepared.PresentationFrame));
        Rect cardBounds = new(
            OuterHorizontalMargin,
            OuterVerticalMargin,
            cardWidth,
            cardHeight);
        Rect affordanceBounds = OblivionCardRenderer.DescribeExpansionAffordanceRect(
            prepared.Resolved,
            card.Id.Value);
        Rect? matureContentBounds = null;
        if (cardView.IsExpanded)
        {
            matureContentBounds = OblivionCardRenderer.DescribeExpandedBodyViewport(
                prepared.Resolved,
                cardView,
                card.Id.Value)?.Bounds;
        }

        return new OblivionStandaloneSurfaceSnapshot(
            width,
            height,
            card,
            cardView,
            contentPlan,
            shellFrame,
            cardBounds,
            affordanceBounds,
            matureContentBounds);
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
            Border = ColorToken.Hex(0x334155FF),
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
                    Background = ColorToken.Hex(0x0B1220FF),
                    Foreground = colors.Foreground,
                    BorderColor = ColorToken.Hex(0x334155FF),
                    ContentInset = 24,
                },
            },
        };
    }
}
