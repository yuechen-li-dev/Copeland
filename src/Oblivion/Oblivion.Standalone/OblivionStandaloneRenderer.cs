using Aurelian.Machina;
using Aurelian.Rendering.Raster;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Pipeline;
using Machina.Standard.Theme;
using Oblivion.Product;

namespace Oblivion.Standalone;

public static class OblivionStandaloneRenderer
{
    public static OblivionStandaloneSurfaceSnapshot Render(
        int width,
        int viewportHeight,
        OblivionViewportState viewport,
        IReadOnlyList<OblivionViewportAssignment> assignments,
        IReadOnlyList<OblivionStandaloneCardPresentation> cards,
        OblivionStandaloneStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(assignments);
        OblivionStandaloneStyle effectiveStyle = style ?? OblivionStandaloneStyles.Dark;
        StandardTheme theme = CreateTheme(effectiveStyle);
        IReadOnlyList<OblivionViewportSlotGeometry> slotGeometry = OblivionViewportGeometry.Resolve(
            viewport.LayoutMode,
            width,
            viewportHeight,
            effectiveStyle.OuterHorizontalMargin,
            effectiveStyle.OuterVerticalMargin,
            effectiveStyle.StackGap);
        List<UiNode> layers = [];
        foreach (OblivionViewportSlotGeometry slot in slotGeometry)
        {
            layers.Add(UI.Anchor(
                UI.Rect(
                    id: $"m20b.slot.{slot.SlotId.ToString().ToLowerInvariant()}.surface",
                    width: slot.Bounds.Width,
                    height: slot.Bounds.Height,
                    style: new UiStyle(
                        Background: effectiveStyle.PageBackground,
                        BorderColor: slot.SlotId == viewport.FocusedSlot
                            ? effectiveStyle.SelectedCardBorder
                            : effectiveStyle.CardBorder,
                        BorderThickness: slot.SlotId == viewport.FocusedSlot ? 2 : 1)),
                id: $"m20b.slot.{slot.SlotId.ToString().ToLowerInvariant()}",
                left: slot.Bounds.X,
                top: slot.Bounds.Y,
                width: slot.Bounds.Width,
                height: slot.Bounds.Height));
        }

        foreach (OblivionStandaloneCardPresentation card in cards)
        {
            Rect slotBounds = slotGeometry.Single(slot => slot.SlotId == card.SlotId).Bounds;
            double cardHeight = card.CardView.IsExpanded
                ? slotBounds.Height
                : Math.Min(effectiveStyle.CollapsedCardHeight, slotBounds.Height);
            OblivionCardRenderOptions cardOptions = CreateCardOptions(
                effectiveStyle,
                slotBounds.Width,
                cardHeight);
            StandardTheme cardTheme = card.SlotId == viewport.FocusedSlot
                ? CreateSelectedTheme(effectiveStyle, theme)
                : theme;
            UiNode cardNode = OblivionCardRenderer.BuildCard(
                card.CardView,
                cardTheme,
                cardOptions);
            layers.Add(UI.Anchor(
                cardNode,
                id: $"m20b.slot.{card.SlotId.ToString().ToLowerInvariant()}.card",
                left: slotBounds.X,
                top: slotBounds.Y,
                width: slotBounds.Width,
                height: cardHeight));
        }

        UiNode document = UI.Rect(
            id: "m20b.viewport",
            width: width,
            height: viewportHeight,
            child: UI.Layer(
                id: "m20b.viewport.slots",
                children: layers),
            style: new UiStyle(Background: effectiveStyle.PageBackground));

        MachinaPreparedPresentation prepared = new MachinaPresentationPipeline().Prepare(
            document,
            width,
            viewportHeight);
        RasterFrame shellFrame = new AurelianCpuRasterRenderer().Render(
            MachinaPresentationTranslator.Translate(prepared.PresentationFrame));

        List<OblivionStandaloneCardSnapshot> snapshots = [];
        foreach (OblivionStandaloneCardPresentation card in cards)
        {
            Rect slotBounds = slotGeometry.Single(slot => slot.SlotId == card.SlotId).Bounds;
            double cardHeight = card.CardView.IsExpanded
                ? slotBounds.Height
                : Math.Min(effectiveStyle.CollapsedCardHeight, slotBounds.Height);
            Machina.Layout.Geometry.Rect cardBounds = new(
                slotBounds.X,
                slotBounds.Y,
                slotBounds.Width,
                cardHeight);
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
                card.SlotId,
                card.DiagramViewportState,
                slotBounds,
                cardBounds,
                affordanceBounds,
                matureContentBounds));
        }

        IReadOnlyList<OblivionStandaloneSlotSnapshot> slots = slotGeometry.Select(slot =>
        {
            string? cardId = assignments.FirstOrDefault(assignment => assignment.SlotId == slot.SlotId)?.CardId;
            return new OblivionStandaloneSlotSnapshot(
                slot.SlotId,
                slot.Bounds,
                cardId,
                slot.SlotId == viewport.FocusedSlot);
        }).ToArray();

        return new OblivionStandaloneSurfaceSnapshot(
            width,
            viewportHeight,
            viewportHeight,
            viewport,
            slots,
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
