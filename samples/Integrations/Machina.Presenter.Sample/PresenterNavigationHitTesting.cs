using Machina.Core.Actions;
using Machina.Layout.Geometry;
using Machina.Runtime.Input;

namespace Machina.Presenter.Sample;

public enum PresenterNavigationHitKind
{
    None,
    SidebarSection,
    LocalTab,
    ContentViewport,
    ScrollbarTrack,
    ScrollbarThumb,
}

public sealed record PresenterNavigationHitTarget(
    PresenterNavigationHitKind Kind,
    string? SectionId = null,
    string? TabId = null,
    string? PageId = null)
{
    public static PresenterNavigationHitTarget None { get; } = new(PresenterNavigationHitKind.None);
}

public sealed record PresenterNavigationSidebarHitRegion(
    string SectionId,
    Rect Rect);

public sealed record PresenterNavigationTabHitRegion(
    string SectionId,
    string TabId,
    string PageId,
    Rect Rect);

public sealed record PresenterNavigationChromeGeometry(
    IReadOnlyList<PresenterNavigationSidebarHitRegion> SidebarSections,
    IReadOnlyList<PresenterNavigationTabHitRegion> LocalTabs,
    Rect ContentViewportRect,
    Rect ScrollbarTrackRect,
    Rect ScrollbarThumbRect,
    bool IsScrollbarVisible);

public static class PresenterNavigationChromeGeometryBuilder
{
    public static PresenterNavigationChromeGeometry Build(
        PresenterNavigationModel model,
        PresenterNavigationState navigationState,
        PresenterNavigationLayout layout,
        PresenterNavigationSection selectedSection,
        ScrollbarGeometry scrollbarGeometry)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(navigationState);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(selectedSection);

        List<PresenterNavigationSidebarHitRegion> sidebarSections = [];
        double sidebarItemTop = layout.SidebarTop + 60;
        foreach (PresenterNavigationSection section in model.Sections)
        {
            sidebarSections.Add(
                new PresenterNavigationSidebarHitRegion(
                    section.Id,
                    new Rect(layout.SidebarLeft + 8, sidebarItemTop, layout.SidebarWidth - 16, 36)));

            sidebarItemTop += 44;
        }

        List<PresenterNavigationTabHitRegion> localTabs = [];
        double tabsLeft = layout.ContentLeft + layout.ContentPanelPadding;
        double tabsTop = layout.ContentTop + 80;

        foreach ((PresenterNavigationTab tab, int index) in selectedSection.Tabs.Select((tab, index) => (tab, index)))
        {
            localTabs.Add(
                new PresenterNavigationTabHitRegion(
                    selectedSection.Id,
                    tab.Id,
                    tab.PageId,
                    new Rect(tabsLeft + (index * (layout.TabWidth + layout.TabsGap)), tabsTop, layout.TabWidth, layout.TabsHeight)));
        }

        return new PresenterNavigationChromeGeometry(
            sidebarSections,
            localTabs,
            layout.ViewportRect,
            scrollbarGeometry.TrackRect,
            scrollbarGeometry.ThumbRect,
            scrollbarGeometry.IsVisible);
    }
}

public static class PresenterNavigationHitTesting
{
    public static PresenterNavigationHitTarget HitTest(
        PresenterNavigationChromeGeometry geometry,
        PointerPoint point)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        if (geometry.IsScrollbarVisible && Contains(geometry.ScrollbarThumbRect, point))
        {
            return new PresenterNavigationHitTarget(PresenterNavigationHitKind.ScrollbarThumb);
        }

        if (geometry.IsScrollbarVisible && Contains(geometry.ScrollbarTrackRect, point))
        {
            return new PresenterNavigationHitTarget(PresenterNavigationHitKind.ScrollbarTrack);
        }

        foreach (PresenterNavigationTabHitRegion tab in geometry.LocalTabs)
        {
            if (Contains(tab.Rect, point))
            {
                return new PresenterNavigationHitTarget(
                    PresenterNavigationHitKind.LocalTab,
                    tab.SectionId,
                    tab.TabId,
                    tab.PageId);
            }
        }

        foreach (PresenterNavigationSidebarHitRegion section in geometry.SidebarSections)
        {
            if (Contains(section.Rect, point))
            {
                return new PresenterNavigationHitTarget(
                    PresenterNavigationHitKind.SidebarSection,
                    section.SectionId);
            }
        }

        if (Contains(geometry.ContentViewportRect, point))
        {
            return new PresenterNavigationHitTarget(PresenterNavigationHitKind.ContentViewport);
        }

        return PresenterNavigationHitTarget.None;
    }

    private static bool Contains(Rect rect, PointerPoint point)
    {
        return point.X >= rect.X &&
               point.Y >= rect.Y &&
               point.X < rect.X + rect.Width &&
               point.Y < rect.Y + rect.Height;
    }
}

public sealed record PresenterNavigationInputRoutingResult(
    PresenterNavigationHitTarget HitTarget,
    UiActionId? ActionId,
    ScrollbarInteractionState InteractionState,
    PointerCaptureRequest PointerCaptureRequest,
    bool SuppressFurtherRouting,
    bool InputConsumed = false,
    OblivionInteractionHitResult? ContentHitResult = null);

public static class PresenterNavigationInputRouter
{
    public const float ScrollWheelMultiplier = 48f;

    public static PresenterNavigationInputRoutingResult Route(
        PresenterNavigationShellRenderResult render,
        UiInputEvent inputEvent)
    {
        return Route(render, inputEvent, ScrollbarInteractionState.Default);
    }

    public static PresenterNavigationInputRoutingResult Route(
        PresenterNavigationShellRenderResult render,
        UiInputEvent inputEvent,
        ScrollbarInteractionState? interactionState)
    {
        ArgumentNullException.ThrowIfNull(render);

        ScrollbarInteractionState effectiveInteractionState = interactionState ?? ScrollbarInteractionState.Default;

        if (inputEvent is UiKeyChanged or UiTextEntered)
        {
            return PresenterKeyboardRouter.Route(
                render,
                inputEvent,
                effectiveInteractionState);
        }

        if (render.PageRender?.OblivionInteraction is not null &&
            render.ChromeGeometry.ContentViewportRect.Width > 0 &&
            render.ChromeGeometry.ContentViewportRect.Height > 0 &&
            inputEvent.TryGetPointerPosition(out PointerPoint pointerPosition) &&
            pointerPosition.X >= render.ChromeGeometry.ContentViewportRect.X &&
            pointerPosition.Y >= render.ChromeGeometry.ContentViewportRect.Y &&
            pointerPosition.X < render.ChromeGeometry.ContentViewportRect.X + render.ChromeGeometry.ContentViewportRect.Width &&
            pointerPosition.Y < render.ChromeGeometry.ContentViewportRect.Y + render.ChromeGeometry.ContentViewportRect.Height)
        {
            PointerPoint localPoint = new(
                pointerPosition.X - render.ChromeGeometry.ContentViewportRect.X,
                pointerPosition.Y - render.ChromeGeometry.ContentViewportRect.Y);
            OblivionPageInteractionRoutingResult routedOblivion = render.PageRender.OblivionInteraction.RouteInput(
                TranslatePointerPosition(inputEvent, localPoint),
                render.ScrollbarGeometry.ScrollOffset,
                effectiveInteractionState);

            if (routedOblivion.Consumed)
            {
                return new PresenterNavigationInputRoutingResult(
                    new PresenterNavigationHitTarget(PresenterNavigationHitKind.ContentViewport),
                    routedOblivion.Action?.Id,
                    routedOblivion.InteractionState,
                    routedOblivion.PointerCaptureRequest,
                    SuppressFurtherRouting: true,
                    InputConsumed: true,
                    ContentHitResult: routedOblivion.HitResult);
            }
        }

        PointerPoint hitTestPosition = inputEvent.TryGetPointerPosition(out PointerPoint position)
            ? position
            : default;
        PresenterNavigationHitTarget hitTarget = PresenterNavigationHitTesting.HitTest(render.ChromeGeometry, hitTestPosition);
        var context = new PresenterScrollbarInteractionContext(
            new PresenterScrollbarTarget(render.SelectedTab.PageId),
            render.ScrollbarGeometry,
            render.Layout.ViewportHeight);
        ScrollbarHitPart scrollbarHitPart = hitTarget.Kind switch
        {
            PresenterNavigationHitKind.ContentViewport => ScrollbarHitPart.Viewport,
            PresenterNavigationHitKind.ScrollbarTrack => ScrollbarHitPart.Track,
            PresenterNavigationHitKind.ScrollbarThumb => ScrollbarHitPart.Thumb,
            _ => ScrollbarHitPart.None,
        };
        PresenterScrollbarInteractionResult interaction = PresenterScrollbarInteraction.Reduce(
            effectiveInteractionState,
            context,
            scrollbarHitPart,
            inputEvent);

        if (interaction.SuppressFurtherRouting)
        {
            return new PresenterNavigationInputRoutingResult(
                hitTarget,
                interaction.ActionId,
                interaction.State,
                interaction.PointerCaptureRequest,
                SuppressFurtherRouting: true,
                InputConsumed: true,
                ContentHitResult: null);
        }

        if (inputEvent.IsPrimaryPressed())
        {
            if (hitTarget.Kind == PresenterNavigationHitKind.SidebarSection &&
                !string.IsNullOrWhiteSpace(hitTarget.SectionId))
            {
                return new PresenterNavigationInputRoutingResult(
                    hitTarget,
                    PresenterNavigationActions.SelectSection(hitTarget.SectionId),
                    interaction.State,
                    interaction.PointerCaptureRequest,
                    SuppressFurtherRouting: false,
                    InputConsumed: false,
                    ContentHitResult: null);
            }

            if (hitTarget.Kind == PresenterNavigationHitKind.LocalTab &&
                !string.IsNullOrWhiteSpace(hitTarget.SectionId) &&
                !string.IsNullOrWhiteSpace(hitTarget.TabId))
            {
                return new PresenterNavigationInputRoutingResult(
                    hitTarget,
                    PresenterNavigationActions.SelectTab(hitTarget.SectionId, hitTarget.TabId),
                    interaction.State,
                    interaction.PointerCaptureRequest,
                    SuppressFurtherRouting: false,
                    InputConsumed: false,
                    ContentHitResult: null);
            }
        }

        return new PresenterNavigationInputRoutingResult(
            hitTarget,
            interaction.ActionId,
            interaction.State,
            interaction.PointerCaptureRequest,
            SuppressFurtherRouting: false,
            InputConsumed: false,
            ContentHitResult: null);
    }

    private static UiInputEvent TranslatePointerPosition(UiInputEvent inputEvent, PointerPoint position)
    {
        return inputEvent switch
        {
            UiPointerMoved moved => moved with { Position = position },
            UiPointerButtonChanged button => button with { Position = position },
            UiPointerWheel wheel => wheel with { Position = position },
            _ => inputEvent,
        };
    }
}
