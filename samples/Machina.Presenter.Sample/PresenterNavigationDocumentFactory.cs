using Machina.Core.Authoring;
using Machina.Core.Flat;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Standard.Authoring;
using Machina.Standard.Components;
using Machina.Standard.Theme;

namespace Machina.Presenter.Sample;

public static class PresenterNavigationDocumentFactory
{
    public static UiDocument BuildShellDocument(
        PresenterNavigationModel model,
        PresenterNavigationState navigationState,
        PresenterNavigationChromeGeometry chromeGeometry,
        PresenterNavigationLayout layout,
        StandardTheme theme,
        string selectedPageId,
        ScrollbarGeometry scrollbarGeometry,
        PresenterProofOptions proofOptions)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(navigationState);
        ArgumentNullException.ThrowIfNull(chromeGeometry);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(proofOptions);

        PresenterNavigationSection selectedSection = model.FindSection(navigationState.SelectedSectionId) ?? model.Sections[0];
        string selectedTabId = navigationState.GetSelectedTabId(selectedSection.Id, model);

        List<UiRow> rows =
        [
            Row.Root(
                id: "root",
                view: View.Rect(background: ColorToken.Hex(0xF3F5F8FF))),

            Row.Anchor(
                id: "sidebar-panel",
                parent: "root",
                left: layout.SidebarLeft,
                top: layout.SidebarTop,
                width: layout.SidebarWidth,
                height: layout.SidebarHeight,
                view: View.Rect(
                    background: ColorToken.Hex(0xE7EBF1FF),
                    borderColor: theme.Colors.Border,
                    borderThickness: 1)),

            Row.Anchor(
                id: "content-panel",
                parent: "root",
                left: layout.ContentLeft,
                top: layout.ContentTop,
                width: layout.ContentWidth,
                height: layout.ContentHeight,
                view: View.Rect(
                    background: theme.Colors.Background,
                    borderColor: theme.Colors.Border,
                    borderThickness: 1)),

            Row.Anchor(
                id: "app-kicker",
                parent: "root",
                left: layout.SidebarLeft + 20,
                top: layout.SidebarTop + 20,
                width: layout.SidebarWidth - 40,
                height: 20,
                view: View.Text("Machina M10c", color: theme.Colors.MutedForeground, size: TextSize.Sm)),

            Row.Anchor(
                id: "app-title",
                parent: "root",
                left: layout.ContentLeft + layout.ContentPanelPadding,
                top: layout.ContentTop + 20,
                width: layout.ContentWidth - (layout.ContentPanelPadding * 2),
                height: 28,
                view: View.Text(PresenterNavigationCatalog.GetPageTitle(selectedPageId), color: theme.Colors.Foreground, size: TextSize.H1)),

            Row.Anchor(
                id: "page-description",
                parent: "root",
                left: layout.ContentLeft + layout.ContentPanelPadding,
                top: layout.ContentTop + 52,
                width: layout.ContentWidth - (layout.ContentPanelPadding * 2),
                height: 24,
                view: View.Text(PresenterNavigationCatalog.GetPageDescription(selectedPageId, proofOptions), color: theme.Colors.MutedForeground, size: TextSize.Sm)),

            Row.Anchor(
                id: "viewport",
                parent: "root",
                left: layout.ViewportLeft,
                top: layout.ViewportTop,
                width: layout.ViewportWidth,
                height: layout.ViewportHeight,
                view: View.Rect(
                    background: ColorToken.Hex(0xF8FAFCFF),
                    borderColor: theme.Colors.Border,
                    borderThickness: 1)),
        ];

        foreach (PresenterNavigationSidebarHitRegion sectionRegion in chromeGeometry.SidebarSections)
        {
            PresenterNavigationSection section = model.FindSection(sectionRegion.SectionId) ?? model.Sections[0];
            bool isSelected = string.Equals(section.Id, selectedSection.Id, StringComparison.Ordinal);
            rows.Add(
                Row.Anchor(
                    id: $"sidebar-section-{section.Id}",
                    parent: "root",
                    left: sectionRegion.Rect.X,
                    top: sectionRegion.Rect.Y,
                    width: sectionRegion.Rect.Width,
                    height: sectionRegion.Rect.Height,
                    component: BuildNavButton(
                        id: $"sidebar-section-{section.Id}.button",
                        label: section.Label,
                        width: sectionRegion.Rect.Width,
                        height: sectionRegion.Rect.Height,
                        selected: isSelected,
                        theme: theme,
                        action: PresenterNavigationActions.SelectSection(section.Id).ToAction())));
        }

        foreach (PresenterNavigationTabHitRegion tabRegion in chromeGeometry.LocalTabs)
        {
            PresenterNavigationTab tab = model.FindTab(tabRegion.SectionId, tabRegion.TabId) ?? selectedSection.Tabs[0];
            bool isSelected = string.Equals(tab.Id, selectedTabId, StringComparison.Ordinal);
            rows.Add(
                Row.Anchor(
                    id: $"tab-{selectedSection.Id}-{tab.Id}",
                    parent: "root",
                    left: tabRegion.Rect.X,
                    top: tabRegion.Rect.Y,
                    width: tabRegion.Rect.Width,
                    height: tabRegion.Rect.Height,
                    component: BuildNavButton(
                        id: $"tab-{selectedSection.Id}-{tab.Id}.button",
                        label: tab.Label,
                        width: tabRegion.Rect.Width,
                        height: tabRegion.Rect.Height,
                        selected: isSelected,
                        theme: theme,
                        action: PresenterNavigationActions.SelectTab(selectedSection.Id, tab.Id).ToAction())));
        }

        if (scrollbarGeometry.IsVisible)
        {
            rows.Add(
                Row.Anchor(
                    id: "scrollbar-track",
                    parent: "root",
                    left: scrollbarGeometry.TrackRect.X,
                    top: scrollbarGeometry.TrackRect.Y,
                    width: scrollbarGeometry.TrackRect.Width,
                    height: scrollbarGeometry.TrackRect.Height,
                    view: View.Rect(background: ColorToken.Hex(0xE2E8F0FF))));

            rows.Add(
                Row.Anchor(
                    id: "scrollbar-thumb",
                    parent: "root",
                    left: scrollbarGeometry.ThumbRect.X,
                    top: scrollbarGeometry.ThumbRect.Y,
                    width: scrollbarGeometry.ThumbRect.Width,
                    height: scrollbarGeometry.ThumbRect.Height,
                    view: View.Rect(background: ColorToken.Hex(0x64748BFF))));
        }

        return UiDocument.Create(rows);
    }

    private static UiNode BuildNavButton(
        string id,
        string label,
        double width,
        double height,
        bool selected,
        StandardTheme theme,
        Machina.Core.Actions.UiAction action)
    {
        StandardButtonStyle baseStyle = selected
            ? new StandardButtonStyle(
                Background: ColorToken.Hex(0x111827FF),
                Foreground: ColorToken.Hex(0xF9FAFBFF),
                BorderColor: ColorToken.Hex(0x111827FF),
                BorderThickness: 1,
                TextStyle: new TextStyle(ColorToken.Hex(0xF9FAFBFF), TextSize.Sm, TextAlignX.Center, TextAlignY.Center),
                Width: width,
                Height: height)
            : new StandardButtonStyle(
                Background: ColorToken.Hex(0xFFFFFFFF),
                Foreground: theme.Colors.Foreground,
                BorderColor: theme.Colors.Border,
                BorderThickness: 1,
                TextStyle: new TextStyle(theme.Colors.Foreground, TextSize.Sm, TextAlignX.Center, TextAlignY.Center),
                Width: width,
                Height: height);

        return StandardUI.Button(
            label,
            id: id,
            action: action,
            theme: theme,
            style: baseStyle,
            variant: ButtonVariant.Outline,
            size: ButtonSize.Medium);
    }
}
