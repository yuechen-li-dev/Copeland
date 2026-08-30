using Machina.Layout.Geometry;

namespace Machina.Presenter.Sample;

public sealed record PresenterNavigationLayout(
    PresenterShellMode ShellMode,
    int RootWidth,
    int RootHeight,
    int OuterPadding,
    int SidebarWidth,
    int SectionGap,
    int ContentPanelPadding,
    int TabsHeight,
    int TabsGap,
    int TabWidth,
    int ViewportTopOffset,
    int ViewportBottomPadding,
    int ScrollbarWidth,
    int ScrollbarGap)
{
    public OblivionHostLayout OblivionHostLayout => new(
        ShellMode == PresenterShellMode.Wide ? OblivionShellMode.Wide : OblivionShellMode.Compact,
        ContentVisibleWidth,
        ViewportHeight);

    public static implicit operator OblivionHostLayout(PresenterNavigationLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return layout.OblivionHostLayout;
    }

    private const int ScrollbarTrackInset = 2;

    public static PresenterNavigationLayout Default { get; } = new(
        ShellMode: PresenterShellMode.Wide,
        RootWidth: 1120,
        RootHeight: 760,
        OuterPadding: 24,
        SidebarWidth: 184,
        SectionGap: 24,
        ContentPanelPadding: 24,
        TabsHeight: 36,
        TabsGap: 12,
        TabWidth: 150,
        ViewportTopOffset: 124,
        ViewportBottomPadding: 24,
        ScrollbarWidth: 12,
        ScrollbarGap: 12);

    public static PresenterNavigationLayout Create(
        int rootWidth,
        int rootHeight,
        PresenterShellMode shellMode)
    {
        return shellMode switch
        {
            PresenterShellMode.Wide => new PresenterNavigationLayout(
                ShellMode: shellMode,
                RootWidth: rootWidth,
                RootHeight: rootHeight,
                OuterPadding: 24,
                SidebarWidth: 184,
                SectionGap: 24,
                ContentPanelPadding: 24,
                TabsHeight: 36,
                TabsGap: 12,
                TabWidth: 150,
                ViewportTopOffset: 124,
                ViewportBottomPadding: 24,
                ScrollbarWidth: 12,
                ScrollbarGap: 12),
            PresenterShellMode.Compact => new PresenterNavigationLayout(
                ShellMode: shellMode,
                RootWidth: rootWidth,
                RootHeight: rootHeight,
                OuterPadding: 20,
                SidebarWidth: 64,
                SectionGap: 16,
                ContentPanelPadding: 20,
                TabsHeight: 36,
                TabsGap: 8,
                TabWidth: 110,
                ViewportTopOffset: 124,
                ViewportBottomPadding: 20,
                ScrollbarWidth: 12,
                ScrollbarGap: 10),
            _ => throw new InvalidOperationException($"Unsupported presenter shell mode '{shellMode}'."),
        };
    }

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

    public int ScrollbarTrackLeft => ViewportLeft + ViewportWidth - ScrollbarWidth - ScrollbarTrackInset;

    public int ScrollbarTrackTop => ViewportTop + ScrollbarTrackInset;

    public int ScrollbarTrackHeight => Math.Max(0, ViewportHeight - (ScrollbarTrackInset * 2));

    public int ContentVisibleWidth => ViewportWidth - ScrollbarWidth - ScrollbarGap;

    public Rect ViewportRect => new(ViewportLeft, ViewportTop, ContentVisibleWidth, ViewportHeight);

    public Rect ScrollbarTrackRect => new(ScrollbarTrackLeft, ScrollbarTrackTop, ScrollbarWidth, ScrollbarTrackHeight);
}
