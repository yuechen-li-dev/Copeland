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
                    new PresenterNavigationTab("bitmap-current", "Bitmap/current", "text.bitmap-current"),
                    new PresenterNavigationTab("direct-outline-static", "DirectOutlineStatic", "text.direct-outline-static"),
                    new PresenterNavigationTab("msdf-experimental", "MSDF experimental", "text.msdf-experimental"),
                ]),
            new PresenterNavigationSection(
                "diagnostics",
                "Diagnostics",
                [
                    new PresenterNavigationTab("layout", "Layout", "diagnostics.layout"),
                    new PresenterNavigationTab("export", "Export", "diagnostics.export"),
                ]),
        ]);
    }

    public static string GetPageTitle(string pageId)
    {
        return pageId switch
        {
            "overview.home" => "Presenter home",
            "overview.status" => "Presenter status",
            "components.controls" => "Component controls",
            "components.cards" => "Component cards",
            "text.bitmap-current" => "Bitmap/current text",
            "text.direct-outline-static" => "DirectOutlineStatic proof",
            "text.msdf-experimental" => "MSDF experimental note",
            "diagnostics.layout" => "Layout diagnostics",
            "diagnostics.export" => "Export diagnostics",
            _ => throw new InvalidOperationException($"Unknown presenter page id '{pageId}'."),
        };
    }

    public static string GetPageDescription(string pageId, PresenterProofOptions proofOptions)
    {
        return pageId switch
        {
            "overview.home" => "The original presenter settings card stays as the first/default page inside the presenter navigation shell.",
            "overview.status" => "A compact status page for sample state and navigation structure.",
            "components.controls" => "A scrollable page that keeps control proofs together without growing one giant screen.",
            "components.cards" => "Card-focused organization notes using the same local presenter primitives.",
            "text.bitmap-current" => "Production UI text defaults remain on the current bitmap path.",
            "text.direct-outline-static" => proofOptions.IncludeDirectOutlineRenderBridgeProof
                ? "DirectOutlineStatic remains a localized proof path under the Text section."
                : "DirectOutlineStatic remains available as an opt-in proof path and is not resumed by default in M10b.",
            "text.msdf-experimental" => "MSDF stays explicit experimental/scalable after the M9 closeout.",
            "diagnostics.layout" => "Layout and scroll structure notes for the presenter navigation shell.",
            "diagnostics.export" => "Export and artifact notes for M10b navigation interaction validation.",
            _ => throw new InvalidOperationException($"Unknown presenter page id '{pageId}'."),
        };
    }

    public static double GetPageContentHeight(string pageId, PresenterProofOptions proofOptions)
    {
        return pageId switch
        {
            "overview.home" => 516,
            "overview.status" => 340,
            "components.controls" => 860,
            "components.cards" => 560,
            "text.bitmap-current" => 356,
            "text.direct-outline-static" => proofOptions.IncludeDirectOutlineRenderBridgeProof ? 700 : 260,
            "text.msdf-experimental" => 320,
            "diagnostics.layout" => 360,
            "diagnostics.export" => 380,
            _ => throw new InvalidOperationException($"Unknown presenter page id '{pageId}'."),
        };
    }

    public static PresenterPageRenderResult RenderPage(
        string pageId,
        DemoState demoState,
        StandardTheme theme,
        PresenterProofOptions proofOptions,
        int contentWidth)
    {
        double contentHeight = GetPageContentHeight(pageId, proofOptions);
        UiDocument document = BuildPageDocument(pageId, demoState, theme, proofOptions, contentWidth);
        var frame = new Machina.Pipeline.MachinaRasterPipeline().Render(document, contentWidth, (int)Math.Ceiling(contentHeight));

        if (pageId == "text.direct-outline-static" && proofOptions.IncludeDirectOutlineRenderBridgeProof)
        {
            PresenterDirectOutlineRenderBridgeProofRenderer.BlitProof(frame.RasterFrame, frame.Resolved);
        }

        return new PresenterPageRenderResult(
            PageId: pageId,
            Document: document,
            Frame: frame,
            ContentHeight: contentHeight);
    }

    private static UiDocument BuildPageDocument(
        string pageId,
        DemoState demoState,
        StandardTheme theme,
        PresenterProofOptions proofOptions,
        int contentWidth)
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
                rows.Add(Row.Anchor("settings-card", "root", left: 0, top: 0, width: 500, height: 292, component: SettingsCard.Build(demoState, theme)));
                rows.Add(Row.Anchor("overview-summary", "root", left: 0, top: 316, width: contentWidth, height: 176, component: BuildInfoCard(
                    "overview-summary-card",
                    "Presenter navigation shell",
                    [
                        "App -> sidebar -> tabs -> pages keeps the presenter sample organized as it grows.",
                        "The original settings content remains the first/default page.",
                        "Scroll state is explicit and tracked per page id.",
                    ],
                    ["M10a", "input", "scroll state"],
                    theme,
                    contentWidth,
                    176)));
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

            case "text.bitmap-current":
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
                rows.Add(Row.Anchor("text-bitmap-current-status", "root", left: 0, top: 188, width: contentWidth, height: 144, component: BuildInfoCard(
                    "text-bitmap-current-status-card",
                    "Font phase note",
                    [
                        "DirectOutlineStatic remains the static/reference path.",
                        "MSDF remains explicit experimental/scalable.",
                    ],
                    ["DirectOutlineStatic", "MSDF experimental"],
                    theme,
                    contentWidth,
                    144)));
                break;

            case "text.direct-outline-static":
                rows.Add(Row.Anchor("text-direct-outline-intro", "root", left: 0, top: 0, width: contentWidth, height: 140, component: BuildInfoCard(
                    "text-direct-outline-intro-card",
                    "DirectOutlineStatic is still proof-only here",
                    [
                        "M10b keeps the existing presenter proof path under Text.",
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
                        width: SettingsScreen.ProofSectionWidth,
                        height: SettingsScreen.ProofSectionHeight,
                        component: PresenterDirectOutlineRenderBridgeProofCard.Build(theme)));
                }
                else
                {
                    rows.Add(Row.Anchor("text-direct-outline-disabled", "root", left: 0, top: 164, width: contentWidth, height: 72, component: BuildInfoCard(
                        "text-direct-outline-disabled-card",
                        "Proof flag not enabled",
                        [
                            "Use the existing opt-in proof flag when you want the presenter to render the direct-outline bridge proof under Text.",
                        ],
                        ["opt-in only"],
                        theme,
                        contentWidth,
                        72)));
                }

                break;

            case "text.msdf-experimental":
                rows.Add(Row.Anchor("text-msdf-experimental", "root", left: 0, top: 0, width: contentWidth, height: 180, component: BuildInfoCard(
                    "text-msdf-experimental-card",
                    "MSDF remains explicit experimental/scalable",
                    [
                        "M9f repaired the experimental path structurally.",
                        "M10b does not extend or integrate that work.",
                        "This page is organizational only.",
                    ],
                    ["M9f", "experimental"],
                    theme,
                    contentWidth,
                    180)));
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
                        "The presenter export script supports the navigation shell as an opt-in sample mode.",
                        "Representative M10b artifacts live under artifacts/m10b.",
                        "A deterministic manifest is written alongside shell exports.",
                    ],
                    ["artifacts/m10b", "manifest"],
                    theme,
                    contentWidth,
                    176)));
                rows.Add(Row.Anchor("diagnostics-export-policy", "root", left: 0, top: 200, width: contentWidth, height: 156, component: BuildInfoCard(
                    "diagnostics-export-policy-card",
                    "Artifact policy",
                    [
                        "These images are local proof artifacts, not a committed pixel golden gate.",
                        "The old non-shell presenter export path stays available and unchanged by default.",
                    ],
                    ["local proof", "default-safe"],
                    theme,
                    contentWidth,
                    156)));
                break;

            default:
                throw new InvalidOperationException($"Unknown presenter page id '{pageId}'.");
        }

        return UiDocument.Create(rows);
    }

    private static UiNode BuildStatusCard(DemoState state, StandardTheme theme, int width, double height)
    {
        return StandardUI.Card(
            id: "overview-status-card-content",
            theme: theme,
            width: width,
            height: height,
            gap: 10,
            children:
            [
                UI.Text("Presenter status", id: "status-title", size: TextSize.Md, color: theme.Colors.Foreground),
                UI.Row(
                    id: "status-badges",
                    gap: 8,
                    children:
                    [
                        StandardUI.Badge($"Count {state.Count}", id: "status-count", theme: theme),
                        StandardUI.Badge(state.EmailUpdates ? "Email on" : "Email off", id: "status-email", theme: theme),
                        StandardUI.Badge(state.Notifications ? "Notify on" : "Notify off", id: "status-notifications", theme: theme),
                    ]),
                StandardUI.TextBlock(
                    id: "status-copy",
                    text: Text.Markup(
                        """
                        The presenter shell keeps the legacy control demo reachable,
                        while moving app-level organization into explicit section/tab state.
                        """,
                        variant: MachinaTextVariant.Caption),
                    theme: theme,
                    foreground: theme.Colors.MutedForeground),
            ]);
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
        List<UiNode> children =
        [
            UI.Text(title, id: "title", size: TextSize.Md, color: theme.Colors.Foreground),
        ];

        if (badges.Count > 0)
        {
            children.Add(
                UI.Row(
                    id: "badges",
                    gap: 8,
                    children: badges.Select((badge, index) => (UiNode)StandardUI.Badge(badge, id: $"badge-{index}", theme: theme, variant: BadgeVariant.Secondary)).ToArray()));
        }

        string markdown = string.Join(
            Environment.NewLine + Environment.NewLine,
            lines.Select(line => $"- {line}"));

        children.Add(
            StandardUI.TextBlock(
                id: "copy",
                text: Text.Markup(markdown, variant: MachinaTextVariant.Caption),
                theme: theme,
                foreground: theme.Colors.MutedForeground));

        return StandardUI.Card(
            id: id,
            theme: theme,
            width: width,
            height: height,
            gap: 10,
            children: children);
    }
}

public sealed record PresenterPageRenderResult(
    string PageId,
    UiDocument Document,
    Machina.Pipeline.MachinaFrame Frame,
    double ContentHeight);
