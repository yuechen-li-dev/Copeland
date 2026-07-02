using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Flat;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Standard.Authoring;
using Machina.Standard.Components;
using Machina.Standard.Text;
using Machina.Standard.Theme;

namespace Machina.Presenter.Sample;

public static class PresenterNavigationCatalog
{
    private static readonly IReadOnlyDictionary<string, string> PageAliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["text.bitmap-current"] = "text.current",
        ["text.direct-outline-static"] = "text.direct-outline",
        ["text.msdf-experimental"] = "text.proofs",
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> TabAliases =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["text"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["bitmap-current"] = "current",
                ["direct-outline-static"] = "direct-outline",
                ["msdf-experimental"] = "proofs",
            },
        };

    public static PresenterNavigationModel CreateModel()
    {
        return new PresenterNavigationModel(
        [
            new PresenterNavigationSection(
                "overview",
                "Overview",
                [
                    new PresenterNavigationTab("home", "Home", "overview.home"),
                    new PresenterNavigationTab("status", "Status", "overview.status"),
                ]),
            new PresenterNavigationSection(
                "components",
                "Components",
                [
                    new PresenterNavigationTab("controls", "Controls", "components.controls"),
                    new PresenterNavigationTab("cards", "Cards", "components.cards"),
                ]),
            new PresenterNavigationSection(
                "text",
                "Text",
                [
                    new PresenterNavigationTab("current", "Current", "text.current"),
                    new PresenterNavigationTab("direct-outline", "DirectOutlineStatic", "text.direct-outline"),
                    new PresenterNavigationTab("proofs", "Proofs", "text.proofs"),
                ]),
            new PresenterNavigationSection(
                "diagnostics",
                "Diagnostics",
                [
                    new PresenterNavigationTab("layout", "Layout", "diagnostics.layout"),
                    new PresenterNavigationTab("export", "Export", "diagnostics.export"),
                ]),
            new PresenterNavigationSection(
                "oblivion",
                "Oblivion",
                [
                    new PresenterNavigationTab("cards", "Cards", OblivionWorkbenchCatalog.CardsPageId),
                    new PresenterNavigationTab("docs", "Docs", OblivionWorkbenchCatalog.DocsPageId),
                    new PresenterNavigationTab("execution-roadmap", "Execution Roadmap", OblivionWorkbenchCatalog.ExecutionRoadmapPageId),
                    new PresenterNavigationTab("artifacts", "Artifacts", OblivionWorkbenchCatalog.ArtifactsPageId),
                ]),
            new PresenterNavigationSection(
                "legacy",
                "Legacy",
                [
                    new PresenterNavigationTab("m1e-card", "M1e Card", "legacy.m1e-card"),
                ]),
        ]);
    }

    public static PresenterNavigationState CreateState(
        PresenterNavigationModel model,
        PresenterProofOptions proofOptions,
        PresenterNavigationExportOptions navigationOptions)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(proofOptions);
        ArgumentNullException.ThrowIfNull(navigationOptions);

        PresenterShellMode shellMode = navigationOptions.ShellMode
            ?? PresenterShellModeResolver.Resolve(navigationOptions.Width);
        PresenterNavigationLayout layout = PresenterNavigationLayout.Create(
            navigationOptions.Width,
            navigationOptions.Height,
            shellMode);
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(model);

        string? resolvedPageId = ResolvePageId(navigationOptions.SelectedPageId, model);
        if (resolvedPageId is not null)
        {
            PresenterNavigationSection section = model.FindSectionByPageId(resolvedPageId);
            PresenterNavigationTab tab = model.FindTabByPageId(resolvedPageId);
            state = state
                .WithSelectedTab(section.Id, tab.Id)
                .WithSelectedSection(section.Id);

            if (!string.IsNullOrWhiteSpace(navigationOptions.SelectedCardId) &&
                IsOblivionPage(resolvedPageId))
            {
                state = state.WithSelectedCard(
                    resolvedPageId,
                    OblivionWorkbenchCatalog.ResolveCardSelectionId(
                        resolvedPageId,
                        navigationOptions.SelectedCardId,
                        proofOptions));
            }
        }
        else
        {
            string? resolvedSectionId = ResolveSectionId(navigationOptions.SelectedSectionId, model);
            if (resolvedSectionId is not null)
            {
                state = state.WithSelectedSection(resolvedSectionId);

                string? resolvedTabId = ResolveTabId(resolvedSectionId, navigationOptions.SelectedTabId, model);
                if (resolvedTabId is not null)
                {
                    state = state.WithSelectedTab(resolvedSectionId, resolvedTabId);
                }
            }
        }

        string currentPageId = model.FindTab(
            state.SelectedSectionId,
            state.GetSelectedTabId(state.SelectedSectionId, model))?.PageId ?? model.Sections[0].Tabs[0].PageId;
        if (!string.IsNullOrWhiteSpace(navigationOptions.SelectedCardId) &&
            IsOblivionPage(currentPageId))
        {
            state = state.WithSelectedCard(
                currentPageId,
                OblivionWorkbenchCatalog.ResolveCardSelectionId(
                    currentPageId,
                    navigationOptions.SelectedCardId,
                    proofOptions));
        }

        if (!string.IsNullOrWhiteSpace(navigationOptions.ExpandedCardId) &&
            IsOblivionPage(currentPageId))
        {
            string expandedCardId = OblivionWorkbenchCatalog.ResolveCardSelectionId(
                currentPageId,
                navigationOptions.ExpandedCardId,
                proofOptions);
            state = state
                .WithSelectedCard(currentPageId, expandedCardId)
                .WithCardViewState(
                    currentPageId,
                    expandedCardId,
                    new OblivionCardViewState(
                        IsExpanded: true,
                        BodyScrollOffset: navigationOptions.ExpandedCardBodyScroll ?? 0));
        }

        if (navigationOptions.InspectorScroll is not null &&
            IsOblivionPage(currentPageId))
        {
            state = state.WithInspectorScrollOffset(currentPageId, navigationOptions.InspectorScroll.Value);
        }

        if (navigationOptions.InspectorRawSourceScroll is not null &&
            IsOblivionPage(currentPageId) &&
            !string.IsNullOrWhiteSpace(state.GetSelectedCardId(
                currentPageId,
                OblivionWorkbenchCatalog.GetPageCardsForSelection(currentPageId, proofOptions))))
        {
            string selectedCardId = state.GetSelectedCardId(
                currentPageId,
                OblivionWorkbenchCatalog.GetPageCardsForSelection(currentPageId, proofOptions))!;
            state = state.WithRawMarkdownSourceScrollOffset(selectedCardId, navigationOptions.InspectorRawSourceScroll.Value);
        }

        if (navigationOptions.ScrollOffsetByPageId is not null)
        {
            foreach ((string pageId, double offset) in navigationOptions.ScrollOffsetByPageId)
            {
                string? resolvedScrollPageId = ResolvePageId(pageId, model);
                if (resolvedScrollPageId is null)
                {
                    continue;
                }

                double clamped = ClampPageScrollOffset(
                    resolvedScrollPageId,
                    offset,
                    proofOptions,
                    state,
                    layout);
                state = state.WithScrollOffset(resolvedScrollPageId, clamped);
            }
        }

        if (navigationOptions.CompactPane is not null)
        {
            state = state.WithCompactPane(navigationOptions.CompactPane.Value);
        }

        return state;
    }

    public static string? ResolveSectionId(string? sectionId, PresenterNavigationModel model)
    {
        if (string.IsNullOrWhiteSpace(sectionId))
        {
            return null;
        }

        return model.FindSection(sectionId) is not null
            ? sectionId
            : null;
    }

    public static string? ResolveTabId(string sectionId, string? tabId, PresenterNavigationModel model)
    {
        ArgumentNullException.ThrowIfNull(sectionId);
        ArgumentNullException.ThrowIfNull(model);

        if (string.IsNullOrWhiteSpace(tabId))
        {
            return null;
        }

        if (model.FindTab(sectionId, tabId) is not null)
        {
            return tabId;
        }

        if (TabAliases.TryGetValue(sectionId, out IReadOnlyDictionary<string, string>? aliases) &&
            aliases.TryGetValue(tabId, out string? aliasTarget) &&
            model.FindTab(sectionId, aliasTarget) is not null)
        {
            return aliasTarget;
        }

        return null;
    }

    public static double ClampPageScrollOffset(
        string pageId,
        double requestedOffset,
        PresenterProofOptions proofOptions,
        PresenterNavigationState navigationState,
        PresenterNavigationLayout layout)
    {
        ArgumentNullException.ThrowIfNull(pageId);
        ArgumentNullException.ThrowIfNull(proofOptions);
        ArgumentNullException.ThrowIfNull(navigationState);
        ArgumentNullException.ThrowIfNull(layout);

        if (layout.ShellMode == PresenterShellMode.Wide &&
            IsOblivionPage(pageId))
        {
            return OblivionWorkbenchCatalog.ClampMainCardStackScrollOffset(
                pageId,
                requestedOffset,
                proofOptions,
                navigationState,
                layout);
        }

        double contentHeight = GetPageContentHeight(pageId, proofOptions, navigationState, layout.ViewportHeight, layout.ShellMode);
        return PresenterScrollRegion.ClampScrollOffset(contentHeight, layout.ViewportHeight, requestedOffset);
    }

    public static string? ResolvePageId(string? pageId, PresenterNavigationModel model)
    {
        if (string.IsNullOrWhiteSpace(pageId))
        {
            return null;
        }

        if (model.ContainsPage(pageId))
        {
            return pageId;
        }

        if (PageAliases.TryGetValue(pageId, out string? aliasTarget) &&
            model.ContainsPage(aliasTarget))
        {
            return aliasTarget;
        }

        return null;
    }

    public static string GetPageTitle(string pageId)
    {
        return pageId switch
        {
            "overview.home" => "Presenter home",
            "overview.status" => "Presenter status",
            "components.controls" => "Component controls",
            "components.cards" => "Component cards",
            "text.current" => "Current text path",
            "text.direct-outline" => "DirectOutlineStatic proof",
            "text.proofs" => "Proof organization",
            "diagnostics.layout" => "Layout diagnostics",
            "diagnostics.export" => "Export diagnostics",
            OblivionWorkbenchCatalog.CardsPageId => "Oblivion cards",
            OblivionWorkbenchCatalog.DocsPageId => "Oblivion docs",
            OblivionWorkbenchCatalog.ExecutionRoadmapPageId => "Oblivion execution roadmap",
            OblivionWorkbenchCatalog.ArtifactsPageId => "Oblivion artifacts",
            "legacy.m1e-card" => "Legacy M1e Card",
            _ => throw new InvalidOperationException($"Unknown presenter page id '{pageId}'."),
        };
    }

    public static string GetPageDescription(string pageId, PresenterProofOptions proofOptions)
    {
        return pageId switch
        {
            "overview.home" => "The navigation shell is now the canonical presenter sample surface and starts here deterministically.",
            "overview.status" => "Current presenter state and navigation defaults without falling back to the old single-screen root.",
            "components.controls" => "A scrollable page that keeps control proofs together without growing one giant screen.",
            "components.cards" => "Card-focused organization notes using the same local presenter primitives.",
            "text.current" => "Production UI text defaults remain on the current bitmap path.",
            "text.direct-outline" => proofOptions.IncludeDirectOutlineRenderBridgeProof
                ? "DirectOutlineStatic remains a localized proof path under the Text section."
                : "DirectOutlineStatic remains available as an opt-in proof path and is not resumed by default in M10c.",
            "text.proofs" => "Existing proof-only text notes stay organized here without reopening font work.",
            "diagnostics.layout" => "Layout and scroll structure notes for the presenter navigation shell.",
            "diagnostics.export" => "Export and artifact notes for the canonical M10c presenter shell.",
            OblivionWorkbenchCatalog.CardsPageId => "Oblivion now closes out the static persisted-card substrate while keeping the existing presenter shell unchanged.",
            OblivionWorkbenchCatalog.DocsPageId => "Curated existing repo docs now dogfood the Markdown body path as typed Oblivion cards while editing stays external.",
            OblivionWorkbenchCatalog.ExecutionRoadmapPageId => "Markdown cards come next, while Roslyn and xUnit execution remain explicitly deferred to M13+ or later.",
            OblivionWorkbenchCatalog.ArtifactsPageId => "Artifact-facing placeholders stay visible as static cards before any capture/runtime work exists.",
            "legacy.m1e-card" => "Preserved sample content from the old single-card presenter root.",
            _ => throw new InvalidOperationException($"Unknown presenter page id '{pageId}'."),
        };
    }

    public static double GetPageContentHeight(
        string pageId,
        PresenterProofOptions proofOptions,
        PresenterNavigationState? navigationState = null,
        int viewportHeight = 596,
        PresenterShellMode shellMode = PresenterShellMode.Wide)
    {
        return pageId switch
        {
            "overview.home" => 504,
            "overview.status" => 376,
            "components.controls" => 860,
            "components.cards" => 560,
            "text.current" => 448,
            "text.direct-outline" => proofOptions.IncludeDirectOutlineRenderBridgeProof ? 896 : 320,
            "text.proofs" => 376,
            "diagnostics.layout" => 360,
            "diagnostics.export" => 432,
            OblivionWorkbenchCatalog.CardsPageId => OblivionWorkbenchCatalog.GetPageContentHeight(pageId, proofOptions, navigationState, viewportHeight, shellMode),
            OblivionWorkbenchCatalog.DocsPageId => OblivionWorkbenchCatalog.GetPageContentHeight(pageId, proofOptions, navigationState, viewportHeight, shellMode),
            OblivionWorkbenchCatalog.ExecutionRoadmapPageId => OblivionWorkbenchCatalog.GetPageContentHeight(pageId, proofOptions, navigationState, viewportHeight, shellMode),
            OblivionWorkbenchCatalog.ArtifactsPageId => OblivionWorkbenchCatalog.GetPageContentHeight(pageId, proofOptions, navigationState, viewportHeight, shellMode),
            "legacy.m1e-card" => proofOptions.IncludeDirectOutlineRenderBridgeProof ? 1152 : 420,
            _ => throw new InvalidOperationException($"Unknown presenter page id '{pageId}'."),
        };
    }

    public static PresenterPageRenderResult RenderPage(
        string pageId,
        DemoState demoState,
        StandardTheme theme,
        PresenterProofOptions proofOptions,
        int contentWidth,
        int viewportHeight,
        PresenterNavigationState? navigationState = null,
        PresenterShellMode shellMode = PresenterShellMode.Wide)
    {
        double contentHeight = GetPageContentHeight(pageId, proofOptions, navigationState, viewportHeight, shellMode);
        UiDocument document = BuildPageDocument(pageId, demoState, theme, proofOptions, contentWidth, viewportHeight, navigationState, shellMode);
        var frame = new Machina.Pipeline.MachinaRasterPipeline().Render(document, contentWidth, (int)Math.Ceiling(contentHeight));

        if (pageId == "text.direct-outline" && proofOptions.IncludeDirectOutlineRenderBridgeProof)
        {
            PresenterDirectOutlineRenderBridgeProofRenderer.BlitProof(frame.RasterFrame, frame.Resolved);
        }

        PresenterPageRenderResult result = new(
            PageId: pageId,
            Document: document,
            Frame: frame,
            ContentHeight: contentHeight);
        if (IsOblivionPage(pageId))
        {
            result = result with
            {
                OblivionInteraction = OblivionWorkbenchCatalog.BuildInteractionMap(pageId, proofOptions, frame.Resolved, navigationState, shellMode),
            };
        }

        return result;
    }

    public static PresenterPageRenderResult RenderPage(
        string pageId,
        DemoState demoState,
        StandardTheme theme,
        PresenterProofOptions proofOptions,
        int contentWidth,
        PresenterNavigationState? navigationState = null,
        PresenterShellMode shellMode = PresenterShellMode.Wide)
    {
        int viewportHeight = shellMode == PresenterShellMode.Compact ? 396 : 596;
        return RenderPage(
            pageId,
            demoState,
            theme,
            proofOptions,
            contentWidth,
            viewportHeight,
            navigationState,
            shellMode);
    }

    private static UiDocument BuildPageDocument(
        string pageId,
        DemoState demoState,
        StandardTheme theme,
        PresenterProofOptions proofOptions,
        int contentWidth,
        int viewportHeight,
        PresenterNavigationState? navigationState,
        PresenterShellMode shellMode)
    {
        List<UiRow> rows =
        [
            Row.Root(
                id: "root",
                view: View.Rect(background: ColorToken.Hex(0x00000000))),
        ];

        switch (pageId)
        {
            case "overview.home":
                rows.Add(Row.Anchor("overview-home-intro", "root", left: 0, top: 0, width: contentWidth, height: 156, component: BuildInfoCard(
                    "overview-home-intro-card",
                    "Canonical presenter sample surface",
                    [
                        "The presenter now opens inside the navigation shell by default.",
                        "Sections and local tabs organize the existing sample/proof content instead of one awkward root screen.",
                        "The old single-card sample is preserved under Legacy rather than acting as the application root.",
                    ],
                    ["M10c", "default shell"],
                    theme,
                    contentWidth,
                    156)));
                rows.Add(Row.Anchor("overview-home-state", "root", left: 0, top: 180, width: contentWidth, height: 132, component: BuildStatusCard(demoState, theme, contentWidth, 132)));
                rows.Add(Row.Anchor("overview-summary", "root", left: 0, top: 336, width: contentWidth, height: 144, component: BuildInfoCard(
                    "overview-summary-card",
                    "Navigation defaults",
                    [
                        "Default section: Overview / Home.",
                        "Scroll offsets begin at zero and stay scoped per page id.",
                        "Scroll state is explicit and tracked per page id.",
                    ],
                    ["immutable state"],
                    theme,
                    contentWidth,
                    144)));
                break;

            case "overview.status":
                rows.Add(Row.Anchor("overview-status", "root", left: 0, top: 0, width: contentWidth, height: 148, component: BuildStatusCard(demoState, theme, contentWidth, 148)));
                rows.Add(Row.Anchor("overview-status-notes", "root", left: 0, top: 172, width: contentWidth, height: 144, component: BuildInfoCard(
                    "overview-status-notes-card",
                    "Presenter sample state",
                    [
                        $"Count is currently {demoState.Count}.",
                        $"Email updates are {(demoState.EmailUpdates ? "on" : "off")}.",
                        $"Notifications are {(demoState.Notifications ? "on" : "off")}.",
                    ],
                    ["plain C#", "immutable state"],
                    theme,
                    contentWidth,
                    144)));
                break;

            case "components.controls":
                rows.Add(Row.Anchor("settings-card", "root", left: 0, top: 0, width: 500, height: 292, component: SettingsCard.Build(demoState, theme)));
                rows.Add(Row.Anchor("components-controls-shell", "root", left: 0, top: 316, width: contentWidth, height: 180, component: BuildInfoCard(
                    "components-controls-shell-card",
                    "Local tabs keep controls together",
                    [
                        "This page deliberately scrolls so the shell can prove viewport + scrollbar structure.",
                        "Navigation state does not live inside the control components.",
                        "The presenter still uses the same settings card subtree rather than inventing new widget families.",
                    ],
                    ["sidebar", "tabs", "scroll"],
                    theme,
                    contentWidth,
                    180)));
                rows.Add(Row.Anchor("components-controls-badges", "root", left: 0, top: 520, width: contentWidth, height: 156, component: BuildInfoCard(
                    "components-controls-badges-card",
                    "Current sample surfaces",
                    [
                        "Buttons",
                        "Checkbox",
                        "Switch",
                        "TextBlock",
                    ],
                    ["controls"],
                    theme,
                    contentWidth,
                    156)));
                rows.Add(Row.Anchor("components-controls-notes", "root", left: 0, top: 700, width: contentWidth, height: 120, component: BuildInfoCard(
                    "components-controls-notes-card",
                    "Scope boundary",
                    [
                        "M10b wires interaction onto the presenter shell.",
                        "No production renderer default changed.",
                    ],
                    ["no animations", "no router framework"],
                    theme,
                    contentWidth,
                    120)));
                break;

            case "components.cards":
                rows.Add(Row.Anchor("components-cards-one", "root", left: 0, top: 0, width: contentWidth, height: 148, component: BuildInfoCard(
                    "components-cards-one-card",
                    "Cards are still localized",
                    [
                        "Hosted components continue to own their own node subtrees.",
                        "The shell only arranges sections, tabs, viewport, and scrollbar chrome.",
                    ],
                    ["component subtree"],
                    theme,
                    contentWidth,
                    148)));
                rows.Add(Row.Anchor("components-cards-two", "root", left: 0, top: 172, width: contentWidth, height: 148, component: BuildInfoCard(
                    "components-cards-two-card",
                    "No nested navigation sprawl",
                    [
                        "Sidebar picks the primary section.",
                        "Tabs stay local to that selected section.",
                    ],
                    ["single hierarchy"],
                    theme,
                    contentWidth,
                    148)));
                rows.Add(Row.Anchor("components-cards-three", "root", left: 0, top: 344, width: contentWidth, height: 180, component: BuildInfoCard(
                    "components-cards-three-card",
                    "Deterministic page model",
                    [
                        "Each section/tab resolves to one stable page id.",
                        "Each page owns its own scroll offset.",
                        "No component secretly owns global navigation state.",
                    ],
                    ["stable ids", "per-page scroll"],
                    theme,
                    contentWidth,
                    180)));
                break;

            case "text.current":
                rows.Add(Row.Anchor("text-bitmap-current", "root", left: 0, top: 0, width: contentWidth, height: 164, component: BuildInfoCard(
                    "text-bitmap-current-card",
                    "Bitmap/current remains default",
                    [
                        "M10b does not resume font work.",
                        "Production UI text defaults remain unchanged.",
                        "The presenter shell only reorganizes sample navigation.",
                    ],
                    ["M9 closed", "no default change"],
                    theme,
                    contentWidth,
                    164)));
                rows.Add(Row.Anchor("text-bitmap-current-status", "root", left: 0, top: 188, width: contentWidth, height: 220, component: BuildInfoCard(
                    "text-bitmap-current-status-card",
                    "Font phase note",
                    [
                        "DirectOutlineStatic remains the static/reference path.",
                        "MSDF remains explicit experimental/scalable.",
                    ],
                    [],
                    theme,
                    contentWidth,
                    220)));
                break;

            case "text.direct-outline":
                rows.Add(Row.Anchor("text-direct-outline-intro", "root", left: 0, top: 0, width: contentWidth, height: 148, component: BuildInfoCard(
                    "text-direct-outline-intro-card",
                    "DirectOutlineStatic is still proof-only here",
                    [
                        "M10c keeps the existing presenter proof path under Text.",
                        "No new font/rendering milestone is started.",
                    ],
                    ["proof-only"],
                    theme,
                    contentWidth,
                    140)));

                if (proofOptions.IncludeDirectOutlineRenderBridgeProof)
                {
                    rows.Add(Row.Anchor(
                        PresenterDirectOutlineRenderBridgeProofLayout.SectionId,
                        "root",
                        left: 0,
                        top: 164,
                        width: contentWidth,
                        height: 708,
                        component: PresenterDirectOutlineRenderBridgeProofCard.Build(theme, contentWidth)));
                }
                else
                {
                    rows.Add(Row.Anchor("text-direct-outline-disabled", "root", left: 0, top: 164, width: contentWidth, height: 132, component: BuildInfoCard(
                        "text-direct-outline-disabled-card",
                        "Proof flag not enabled",
                        [
                            "Use the existing opt-in proof flag when you want the presenter to render the direct-outline bridge proof under Text.",
                        ],
                        ["opt-in only"],
                        theme,
                        contentWidth,
                        116)));
                }

                break;

            case "text.proofs":
                rows.Add(Row.Anchor("text-proofs-overview", "root", left: 0, top: 0, width: contentWidth, height: 180, component: BuildInfoCard(
                    "text-proofs-overview-card",
                    "Proof-only text surfaces remain organized",
                    [
                        "DirectOutlineStatic stays localized to explicit proof pages.",
                        "MSDF remains explicit experimental/scalable after the M9 closeout.",
                        "M10c reorganizes sample surfaces but does not resume font work.",
                    ],
                    ["M9 closed", "proof-only"],
                    theme,
                    contentWidth,
                    180)));
                rows.Add(Row.Anchor("text-proofs-status", "root", left: 0, top: 204, width: contentWidth, height: 136, component: BuildInfoCard(
                    "text-proofs-status-card",
                    "Proof routing",
                    [
                        "Use the direct-outline proof flag together with the Text / DirectOutlineStatic page when you want the bridge proof rendered in the shell.",
                        "Legacy single-card mode still exists if you need the old root export path.",
                    ],
                    ["organization only"],
                    theme,
                    contentWidth,
                    112)));
                break;

            case "diagnostics.layout":
                rows.Add(Row.Anchor("diagnostics-layout", "root", left: 0, top: 0, width: contentWidth, height: 164, component: BuildInfoCard(
                    "diagnostics-layout-card",
                    "Scroll region contract",
                    [
                        "Viewport height and content height are explicit.",
                        "Scroll offset clamps to max(0, contentHeight - viewportHeight).",
                        "Scrollbar thumb size and position derive from deterministic geometry.",
                    ],
                    ["geometry", "clamp"],
                    theme,
                    contentWidth,
                    164)));
                rows.Add(Row.Anchor("diagnostics-layout-state", "root", left: 0, top: 188, width: contentWidth, height: 148, component: BuildInfoCard(
                    "diagnostics-layout-state-card",
                    "State ownership",
                    [
                        "Presenter app owns selected section.",
                        "Selected section owns selected tab.",
                        "Selected page owns scroll offset.",
                    ],
                    ["explicit ownership"],
                    theme,
                    contentWidth,
                    148)));
                break;

            case "diagnostics.export":
                rows.Add(Row.Anchor("diagnostics-export", "root", left: 0, top: 0, width: contentWidth, height: 176, component: BuildInfoCard(
                    "diagnostics-export-card",
                    "Export workflow",
                    [
                        "The presenter export script now writes the navigation shell by default.",
                        "Representative M10c artifacts live under artifacts/m10c.",
                        "A deterministic manifest is written alongside shell exports.",
                    ],
                    ["artifacts/m10c", "manifest"],
                    theme,
                    contentWidth,
                    176)));
                rows.Add(Row.Anchor("diagnostics-export-policy", "root", left: 0, top: 200, width: contentWidth, height: 208, component: BuildInfoCard(
                    "diagnostics-export-policy-card",
                    "Artifact policy",
                    [
                        "These images are local proof artifacts, not a committed pixel golden gate.",
                        "The old non-shell presenter export path stays available behind the legacy flag.",
                        "Selected section, selected tab, and per-page scroll exports still resolve deterministically.",
                    ],
                    ["local proof", "legacy opt-out"],
                    theme,
                    contentWidth,
                    208)));
                break;

            case OblivionWorkbenchCatalog.CardsPageId:
            case OblivionWorkbenchCatalog.DocsPageId:
            case OblivionWorkbenchCatalog.ExecutionRoadmapPageId:
            case OblivionWorkbenchCatalog.ArtifactsPageId:
                rows.AddRange(OblivionWorkbenchCatalog.BuildPageRows(pageId, theme, contentWidth, viewportHeight, proofOptions, navigationState, shellMode));
                break;

            case "legacy.m1e-card":
                rows.Add(Row.Anchor(
                    "legacy-settings-card-wrapper",
                    "root",
                    left: 0,
                    top: 24,
                    width: contentWidth,
                    height: 352,
                    component: BuildLegacyCardWrapper(demoState, theme, contentWidth)));

                if (proofOptions.IncludeDirectOutlineRenderBridgeProof)
                {
                    rows.Add(
                        Row.Anchor(
                            PresenterDirectOutlineRenderBridgeProofLayout.SectionId,
                            "root",
                            left: 0,
                            top: 400,
                            width: contentWidth,
                            height: 708,
                            component: PresenterDirectOutlineRenderBridgeProofCard.Build(theme, contentWidth)));
                }

                break;

            default:
                throw new InvalidOperationException($"Unknown presenter page id '{pageId}'.");
        }

        return UiDocument.Create(rows);
    }

    private static UiNode BuildStatusCard(DemoState state, StandardTheme theme, int width, double height)
    {
        return PresenterCard.BuildTextCard(
            id: "overview-status-card-content",
            title: "Presenter status",
            badges:
            [
                $"Count {state.Count}",
                state.EmailUpdates ? "Email on" : "Email off",
                state.Notifications ? "Notify on" : "Notify off",
            ],
            lines:
            [
                "The presenter shell keeps the legacy control demo reachable.",
                "App-level organization now lives in explicit section and tab state.",
                "Per-page scroll offset remains separate from component-local state.",
            ],
            theme: theme,
            options: new PresenterCardOptions(
                Width: width,
                Height: height));
    }

    private static UiNode BuildInfoCard(
        string id,
        string title,
        IReadOnlyList<string> lines,
        IReadOnlyList<string> badges,
        StandardTheme theme,
        int width,
        double height)
    {
        return PresenterCard.BuildTextCard(
            id: id,
            title: title,
            badges: badges,
            lines: lines,
            theme: theme,
            options: new PresenterCardOptions(
                Width: width,
                Height: height));
    }

    private static UiNode BuildLegacyCardWrapper(DemoState state, StandardTheme theme, int width)
    {
        return PresenterCard.BuildHostedCard(
            id: "legacy-settings-wrapper-card",
            title: "Legacy M1e Card",
            badges:
            [
                "preserved",
                "legacy root",
            ],
            body: UI.Anchor(
                SettingsCard.Build(state, theme),
                id: "legacy-settings-card-slot",
                left: 0,
                top: 0,
                width: 500,
                height: 292),
            theme: theme,
            options: new PresenterCardOptions(
                Width: width,
                Height: 352));
    }

    public static bool IsOblivionPage(string pageId)
    {
        return string.Equals(pageId, OblivionWorkbenchCatalog.CardsPageId, StringComparison.Ordinal) ||
               string.Equals(pageId, OblivionWorkbenchCatalog.DocsPageId, StringComparison.Ordinal) ||
               string.Equals(pageId, OblivionWorkbenchCatalog.ExecutionRoadmapPageId, StringComparison.Ordinal) ||
               string.Equals(pageId, OblivionWorkbenchCatalog.ArtifactsPageId, StringComparison.Ordinal);
    }
}

public sealed record PresenterPageRenderResult(
    string PageId,
    UiDocument Document,
    Machina.Pipeline.MachinaFrame Frame,
    double ContentHeight)
{
    public OblivionPageInteractionMap? OblivionInteraction { get; init; }
}
