using Machina.Core.Actions;
using Machina.Layout.Geometry;

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
                    new Rect(layout.SidebarLeft + 16, sidebarItemTop, layout.SidebarWidth - 32, 36)));

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
                    new Rect(tabsLeft + (index * (150 + layout.TabsGap)), tabsTop, 150, layout.TabsHeight)));
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
        PresenterInputPoint point)
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

    private static bool Contains(Rect rect, PresenterInputPoint point)
    {
        return point.X >= rect.X &&
               point.Y >= rect.Y &&
               point.X < rect.X + rect.Width &&
               point.Y < rect.Y + rect.Height;
    }
}

public sealed record PresenterNavigationInputRoutingResult(
    PresenterNavigationHitTarget HitTarget,
    UiActionId? ActionId);

public static class PresenterNavigationInputRouter
{
    public const float ScrollWheelMultiplier = 48f;

    public static PresenterNavigationInputRoutingResult Route(
        PresenterNavigationShellRenderResult render,
        PresenterInputEvent inputEvent)
    {
        ArgumentNullException.ThrowIfNull(render);

        PresenterNavigationHitTarget hitTarget = PresenterNavigationHitTesting.HitTest(render.ChromeGeometry, inputEvent.Position);

        if (inputEvent.Kind == PresenterInputKind.PointerPressed &&
            inputEvent.Button == PresenterInputButton.Primary)
        {
            if (hitTarget.Kind == PresenterNavigationHitKind.SidebarSection &&
                !string.IsNullOrWhiteSpace(hitTarget.SectionId))
            {
                return new PresenterNavigationInputRoutingResult(
                    hitTarget,
                    PresenterNavigationActions.SelectSection(hitTarget.SectionId));
            }

            if (hitTarget.Kind == PresenterNavigationHitKind.LocalTab &&
                !string.IsNullOrWhiteSpace(hitTarget.SectionId) &&
                !string.IsNullOrWhiteSpace(hitTarget.TabId))
            {
                return new PresenterNavigationInputRoutingResult(
                    hitTarget,
                    PresenterNavigationActions.SelectTab(hitTarget.SectionId, hitTarget.TabId));
            }

            if (hitTarget.Kind == PresenterNavigationHitKind.ScrollbarTrack &&
                render.ScrollbarGeometry.IsVisible)
            {
                double pageDelta = render.Layout.ViewportHeight * 0.9;
                double nextOffset = inputEvent.Position.Y < render.ScrollbarGeometry.ThumbRect.Y
                    ? render.ScrollbarGeometry.ScrollOffset - pageDelta
                    : render.ScrollbarGeometry.ScrollOffset + pageDelta;

                return new PresenterNavigationInputRoutingResult(
                    hitTarget,
                    PresenterNavigationActions.SetScrollOffset(render.SelectedTab.PageId, nextOffset));
            }
        }

        if (inputEvent.Kind == PresenterInputKind.Wheel &&
            hitTarget.Kind == PresenterNavigationHitKind.ContentViewport &&
            render.ScrollbarGeometry.MaxScrollOffset > 0)
        {
            double nextOffset = render.ScrollbarGeometry.ScrollOffset - (inputEvent.WheelDeltaY * ScrollWheelMultiplier);
            return new PresenterNavigationInputRoutingResult(
                hitTarget,
                PresenterNavigationActions.SetScrollOffset(render.SelectedTab.PageId, nextOffset));
        }

        return new PresenterNavigationInputRoutingResult(hitTarget, null);
    }
}
