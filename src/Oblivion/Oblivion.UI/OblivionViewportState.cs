using Machina.Layout.Geometry;

namespace Oblivion.Product;

public enum OblivionViewportLayoutMode
{
    Single,
    VerticalSplit,
    HorizontalSplit,
}

public enum OblivionViewportSlotId
{
    A,
    B,
}

public sealed record OblivionViewportState(
    OblivionViewportLayoutMode LayoutMode,
    OblivionViewportSlotId FocusedSlot)
{
    public static OblivionViewportState Single { get; } = new(
        OblivionViewportLayoutMode.Single,
        OblivionViewportSlotId.A);

    public OblivionViewportState WithLayout(OblivionViewportLayoutMode mode)
    {
        return new OblivionViewportState(mode, OblivionViewportSlotId.A);
    }

    public OblivionViewportState FocusNext()
    {
        if (LayoutMode == OblivionViewportLayoutMode.Single)
        {
            return this with { FocusedSlot = OblivionViewportSlotId.A };
        }

        return this with
        {
            FocusedSlot = FocusedSlot == OblivionViewportSlotId.A
                ? OblivionViewportSlotId.B
                : OblivionViewportSlotId.A,
        };
    }
}

public sealed record OblivionViewportAssignment(
    OblivionViewportSlotId SlotId,
    string? CardId);

public static class OblivionViewportAssignments
{
    public static IReadOnlyList<OblivionViewportAssignment> Resolve(
        OblivionViewportState viewport,
        IReadOnlyList<OblivionCard> cards,
        string? selectedCardId)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(cards);

        int selectedIndex = selectedCardId is null
            ? -1
            : IndexOf(cards, selectedCardId);
        if (selectedIndex < 0 && cards.Count > 0)
        {
            selectedIndex = 0;
        }

        string? first = selectedIndex >= 0 ? cards[selectedIndex].Id.Value : null;
        if (viewport.LayoutMode == OblivionViewportLayoutMode.Single)
        {
            return [new OblivionViewportAssignment(OblivionViewportSlotId.A, first)];
        }

        string? second = selectedIndex >= 0 && selectedIndex + 1 < cards.Count
            ? cards[selectedIndex + 1].Id.Value
            : null;
        return
        [
            new OblivionViewportAssignment(OblivionViewportSlotId.A, first),
            new OblivionViewportAssignment(OblivionViewportSlotId.B, second),
        ];
    }

    public static string? ResolveFocusedCardId(
        OblivionViewportState viewport,
        IReadOnlyList<OblivionCard> cards,
        string? selectedCardId)
    {
        return Resolve(viewport, cards, selectedCardId)
            .FirstOrDefault(assignment => assignment.SlotId == viewport.FocusedSlot)
            ?.CardId;
    }

    private static int IndexOf(IReadOnlyList<OblivionCard> cards, string cardId)
    {
        for (int index = 0; index < cards.Count; index++)
        {
            if (string.Equals(cards[index].Id.Value, cardId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}

public sealed record OblivionViewportSlotGeometry(
    OblivionViewportSlotId SlotId,
    Rect Bounds);

public static class OblivionViewportGeometry
{
    public static IReadOnlyList<OblivionViewportSlotGeometry> Resolve(
        OblivionViewportLayoutMode mode,
        double width,
        double height,
        double horizontalMargin,
        double verticalMargin,
        double gap)
    {
        double usableWidth = Math.Max(0, width - (horizontalMargin * 2));
        double usableHeight = Math.Max(0, height - (verticalMargin * 2));
        Rect whole = new(horizontalMargin, verticalMargin, usableWidth, usableHeight);

        if (mode == OblivionViewportLayoutMode.Single)
        {
            return [new OblivionViewportSlotGeometry(OblivionViewportSlotId.A, whole)];
        }

        if (mode == OblivionViewportLayoutMode.VerticalSplit)
        {
            double firstHeight = Math.Max(0, (usableHeight - gap) / 2);
            double secondHeight = Math.Max(0, usableHeight - gap - firstHeight);
            return
            [
                new OblivionViewportSlotGeometry(
                    OblivionViewportSlotId.A,
                    new Rect(horizontalMargin, verticalMargin, usableWidth, firstHeight)),
                new OblivionViewportSlotGeometry(
                    OblivionViewportSlotId.B,
                    new Rect(horizontalMargin, verticalMargin + firstHeight + gap, usableWidth, secondHeight)),
            ];
        }

        double firstWidth = Math.Max(0, (usableWidth - gap) / 2);
        double secondWidth = Math.Max(0, usableWidth - gap - firstWidth);
        return
        [
            new OblivionViewportSlotGeometry(
                OblivionViewportSlotId.A,
                new Rect(horizontalMargin, verticalMargin, firstWidth, usableHeight)),
            new OblivionViewportSlotGeometry(
                OblivionViewportSlotId.B,
                new Rect(horizontalMargin + firstWidth + gap, verticalMargin, secondWidth, usableHeight)),
        ];
    }
}

public enum OblivionDiagramFitMode
{
    Fit,
    Manual,
}

public sealed record OblivionDiagramViewportState(
    double Zoom,
    double PanX,
    double PanY,
    OblivionDiagramFitMode FitMode)
{
    public const double MinimumZoom = 0.25;
    public const double MaximumZoom = 4;
    public const double ZoomStep = 1.25;

    public static OblivionDiagramViewportState Fit { get; } = new(
        Zoom: 1,
        PanX: 0,
        PanY: 0,
        OblivionDiagramFitMode.Fit);

    public OblivionDiagramViewportState ZoomBy(double factor)
    {
        double nextZoom = Math.Clamp(Zoom * factor, MinimumZoom, MaximumZoom);
        return this with
        {
            Zoom = nextZoom,
            FitMode = OblivionDiagramFitMode.Manual,
        };
    }

    public OblivionDiagramViewportState PanBy(double deltaX, double deltaY)
    {
        return this with
        {
            PanX = PanX + deltaX,
            PanY = PanY + deltaY,
            FitMode = OblivionDiagramFitMode.Manual,
        };
    }

    public OblivionDiagramViewportState Reset()
    {
        return Fit;
    }
}

public sealed record OblivionDiagramCamera(
    double Scale,
    double OffsetX,
    double OffsetY,
    double WorldWidth,
    double WorldHeight,
    double ViewportWidth,
    double ViewportHeight);

public static class OblivionDiagramCameraMath
{
    public static OblivionDiagramCamera Resolve(
        OblivionDiagramViewportState state,
        double worldWidth,
        double worldHeight,
        double viewportWidth,
        double viewportHeight)
    {
        double safeWorldWidth = Math.Max(1, worldWidth);
        double safeWorldHeight = Math.Max(1, worldHeight);
        double safeViewportWidth = Math.Max(1, viewportWidth);
        double safeViewportHeight = Math.Max(1, viewportHeight);
        double fitScale = Math.Min(
            safeViewportWidth / safeWorldWidth,
            safeViewportHeight / safeWorldHeight);
        double scale = state.FitMode == OblivionDiagramFitMode.Fit
            ? fitScale
            : fitScale * state.Zoom;
        double centeredX = (safeViewportWidth - (safeWorldWidth * scale)) / 2;
        double centeredY = (safeViewportHeight - (safeWorldHeight * scale)) / 2;
        double maximumPanX = Math.Max(safeViewportWidth, safeWorldWidth * scale) * 0.9;
        double maximumPanY = Math.Max(safeViewportHeight, safeWorldHeight * scale) * 0.9;

        return new OblivionDiagramCamera(
            scale,
            centeredX + Math.Clamp(state.PanX, -maximumPanX, maximumPanX),
            centeredY + Math.Clamp(state.PanY, -maximumPanY, maximumPanY),
            safeWorldWidth,
            safeWorldHeight,
            safeViewportWidth,
            safeViewportHeight);
    }
}
