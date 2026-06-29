using System.Text.Json;

namespace Machina.Presenter.Sample;

public static class PresenterNavigationManifestWriter
{
    public const string JsonFileName = "presenter-navigation-shell-manifest.json";
    public const string TextFileName = "presenter-navigation-shell-manifest.txt";

    public static (string jsonPath, string textPath) Write(
        string outputDirectory,
        PresenterNavigationModel model,
        PresenterProofOptions proofOptions)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(proofOptions);

        Directory.CreateDirectory(outputDirectory);

        string jsonPath = Path.Combine(outputDirectory, JsonFileName);
        string textPath = Path.Combine(outputDirectory, TextFileName);

        var manifest = new
        {
            milestone = "M10a",
            kind = "presenter-navigation-shell",
            purpose = "Presenter organization shell with sidebar, local tabs, and scrollable pages.",
            m9FontPhase = new
            {
                status = "closed-unless-concrete-integration-needs-arise",
                directOutlineStatic = "static-reference-path",
                msdf = "explicit-experimental-scalable",
            },
            navigationHierarchy = new[] { "app", "sidebar", "tabs", "pages" },
            sections = model.Sections.Select(section => new
            {
                id = section.Id,
                label = section.Label,
                tabs = section.Tabs.Select(tab => new { id = tab.Id, label = tab.Label, pageId = tab.PageId }).ToArray(),
            }).ToArray(),
            shell = new
            {
                navigationShellOptIn = true,
                deterministicScrollbars = true,
                directOutlineProofOptIn = proofOptions.IncludeDirectOutlineRenderBridgeProof,
            },
        };

        string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        string[] textLines =
        [
            "milestone=M10a",
            "kind=presenter-navigation-shell",
            "m9FontPhase.status=closed-unless-concrete-integration-needs-arise",
            "m9FontPhase.directOutlineStatic=static-reference-path",
            "m9FontPhase.msdf=explicit-experimental-scalable",
            "navigationHierarchy=app,sidebar,tabs,pages",
            $"shell.navigationShellOptIn={true.ToString().ToLowerInvariant()}",
            $"shell.deterministicScrollbars={true.ToString().ToLowerInvariant()}",
            $"shell.directOutlineProofOptIn={proofOptions.IncludeDirectOutlineRenderBridgeProof.ToString().ToLowerInvariant()}",
            "sections:",
            .. model.Sections.Select(section => $"  {section.Id}:{section.Label}:{string.Join(",", section.Tabs.Select(tab => $"{tab.Id}->{tab.PageId}"))}"),
        ];

        File.WriteAllText(jsonPath, json);
        File.WriteAllLines(textPath, textLines);
        return (jsonPath, textPath);
    }
}
