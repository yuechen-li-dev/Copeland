using Machina.Layout.Geometry;

namespace Machina.Presenter.Sample;

public sealed record PresenterNavigationLayout(
    int RootWidth,
    int RootHeight,
    int OuterPadding,
    int SidebarWidth,
    int SectionGap,
    int ContentPanelPadding,
    int TabsHeight,
    int TabsGap,
    int ViewportTopOffset,
    int ViewportBottomPadding,
    int ScrollbarWidth,
    int ScrollbarGap)
{
    public static PresenterNavigationLayout Default { get; } = new(
        RootWidth: 1120,
        RootHeight: 760,
        OuterPadding: 24,
        SidebarWidth: 184,
        SectionGap: 24,
        ContentPanelPadding: 24,
        TabsHeight: 36,
        TabsGap: 12,
        ViewportTopOffset: 124,
        ViewportBottomPadding: 24,
        ScrollbarWidth: 12,
        ScrollbarGap: 12);

    public int SidebarLeft => OuterPadding;

    public int SidebarTop => OuterPadding;

    public int SidebarHeight => RootHeight - (OuterPadding * 2);

    public int ContentLeft => SidebarLeft + SidebarWidth + SectionGap;

    public int ContentTop => OuterPadding;

    public int ContentWidth => RootWidth - ContentLeft - OuterPadding;

    public int ContentHeight => RootHeight - (OuterPadding * 2);

    public int ViewportLeft => ContentLeft + ContentPanelPadding;

    public int ViewportTop => ContentTop + ViewportTopOffset;

    public int ViewportHeight => ContentHeight - ViewportTopOffset - ViewportBottomPadding;

    public int ViewportWidth => ContentWidth - (ContentPanelPadding * 2);

    public int ScrollbarTrackLeft => ViewportLeft + ViewportWidth - ScrollbarWidth;

    public int ContentVisibleWidth => ViewportWidth - ScrollbarWidth - ScrollbarGap;

    public Rect ViewportRect => new(ViewportLeft, ViewportTop, ContentVisibleWidth, ViewportHeight);

    public Rect ScrollbarTrackRect => new(ScrollbarTrackLeft, ViewportTop, ScrollbarWidth, ViewportHeight);
}
