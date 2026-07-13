using System.Text.Json;

namespace Machina.Presenter.Sample;

public static class PresenterNavigationManifestWriter
{
    public const string JsonFileName = "presenter-scrollbar-state-machine-manifest.json";
    public const string TextFileName = "presenter-scrollbar-state-machine-manifest.txt";

    public static (string jsonPath, string textPath) Write(
        string outputDirectory,
        PresenterNavigationShellRenderResult render,
        PresenterProofOptions proofOptions,
        string? interactionBackendName)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(proofOptions);

        Directory.CreateDirectory(outputDirectory);

        string jsonPath = Path.Combine(outputDirectory, JsonFileName);
        string textPath = Path.Combine(outputDirectory, TextFileName);

        var manifest = new
        {
            milestone = "M11c",
            kind = "presenter-scrollbar-state-machine",
            purpose = "Presenter scrollbar refactor with explicit interaction states and cached composition layers.",
            m9FontPhase = new
            {
                status = "closed-unless-concrete-integration-needs-arise",
                directOutlineStatic = "static-reference-path",
                msdf = "explicit-experimental-scalable",
            },
            navigationHierarchy = new[] { "app", "sidebar", "tabs", "pages" },
            sections = render.Model.Sections.Select(section => new
            {
                id = section.Id,
                label = section.Label,
                tabs = section.Tabs.Select(tab => new { id = tab.Id, label = tab.Label, pageId = tab.PageId }).ToArray(),
            }).ToArray(),
            shell = new
            {
                navigationShellDefault = true,
                legacySingleCardAvailable = true,
                deterministicScrollbars = true,
                explicitInteractionStateMachine = true,
                cachedComposition = true,
                directOutlineProofOptIn = proofOptions.IncludeDirectOutlineRenderBridgeProof,
                interactionBackend = interactionBackendName ?? "none",
                backendBoundary = "sample-scoped-adapter",
                dominatusIntegrationDecision = "local-dominatus-style-state-machine-with-render-bridge-boundary",
                pageRenderCount = render.Diagnostics.PageRenderCount,
                shellRenderCount = render.Diagnostics.ShellRenderCount,
                compositionCount = render.Diagnostics.CompositionCount,
                scrollOffset = render.ScrollbarGeometry.ScrollOffset,
                selectedSection = render.SelectedSection.Id,
                selectedTab = render.SelectedTab.Id,
                selectedPage = render.SelectedTab.PageId,
            },
        };

        string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        string[] textLines =
        [
            "milestone=M11c",
            "kind=presenter-scrollbar-state-machine",
            "m9FontPhase.status=closed-unless-concrete-integration-needs-arise",
            "m9FontPhase.directOutlineStatic=static-reference-path",
            "m9FontPhase.msdf=explicit-experimental-scalable",
            "navigationHierarchy=app,sidebar,tabs,pages",
            $"shell.navigationShellDefault={true.ToString().ToLowerInvariant()}",
            $"shell.legacySingleCardAvailable={true.ToString().ToLowerInvariant()}",
            $"shell.deterministicScrollbars={true.ToString().ToLowerInvariant()}",
            $"shell.explicitInteractionStateMachine={true.ToString().ToLowerInvariant()}",
            $"shell.cachedComposition={true.ToString().ToLowerInvariant()}",
            $"shell.directOutlineProofOptIn={proofOptions.IncludeDirectOutlineRenderBridgeProof.ToString().ToLowerInvariant()}",
            $"shell.interactionBackend={interactionBackendName ?? "none"}",
            "shell.backendBoundary=sample-scoped-adapter",
            "shell.dominatusIntegrationDecision=local-dominatus-style-state-machine-with-render-bridge-boundary",
            $"shell.pageRenderCount={render.Diagnostics.PageRenderCount}",
            $"shell.shellRenderCount={render.Diagnostics.ShellRenderCount}",
            $"shell.compositionCount={render.Diagnostics.CompositionCount}",
            $"shell.scrollOffset={render.ScrollbarGeometry.ScrollOffset}",
            $"shell.selectedSection={render.SelectedSection.Id}",
            $"shell.selectedTab={render.SelectedTab.Id}",
            $"shell.selectedPage={render.SelectedTab.PageId}",
            "sections:",
            .. render.Model.Sections.Select(section => $"  {section.Id}:{section.Label}:{string.Join(",", section.Tabs.Select(tab => $"{tab.Id}->{tab.PageId}"))}"),
        ];

        File.WriteAllText(jsonPath, json);
        File.WriteAllLines(textPath, textLines);
        return (jsonPath, textPath);
    }
}
