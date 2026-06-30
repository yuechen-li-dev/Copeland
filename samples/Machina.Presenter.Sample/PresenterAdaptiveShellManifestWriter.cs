using System.Text.Json;

namespace Machina.Presenter.Sample;

public static class PresenterAdaptiveShellManifestWriter
{
    public const string JsonFileName = "presenter-adaptive-shell-manifest.json";
    public const string TextFileName = "presenter-adaptive-shell-manifest.txt";

    public static (string jsonPath, string textPath) Write(
        string outputDirectory,
        PresenterNavigationShellRenderResult render)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(render);

        Directory.CreateDirectory(outputDirectory);

        string jsonPath = Path.Combine(outputDirectory, JsonFileName);
        string textPath = Path.Combine(outputDirectory, TextFileName);

        string[] deferredWork =
        [
            "Animated shell transitions",
            "Touch gestures",
            "Continuous scaling or responsive interpolation",
            "Generic responsive layout solver",
            "Markdown editor",
            "Roslyn compilation and execution",
            "xUnit [Fact] and [Theory] runtime",
            "Visionary code editor/source workspace",
        ];

        var manifest = new
        {
            milestone = "M12h",
            kind = "presenter-adaptive-shell-modes",
            shellModes = new[] { "wide", "compact" },
            breakpointWidth = PresenterShellModeResolver.BreakpointWidth,
            resolvedShellMode = render.ShellMode.ToString().ToLowerInvariant(),
            continuousScaling = false,
            layoutNegotiation = false,
            compactSidebarRail = true,
            compactInspectorSwap = true,
            cardsKnowShellMode = false,
            editorImplemented = false,
            roslynEnabled = false,
            xunitEnabled = false,
            visionaryImplemented = false,
            deferredWork,
        };

        string json = JsonSerializer.Serialize(
            manifest,
            new JsonSerializerOptions
            {
                WriteIndented = true,
            });

        string[] textLines =
        [
            "milestone=M12h",
            "kind=presenter-adaptive-shell-modes",
            "shellModes=wide,compact",
            $"breakpointWidth={PresenterShellModeResolver.BreakpointWidth}",
            $"resolvedShellMode={render.ShellMode.ToString().ToLowerInvariant()}",
            "continuousScaling=false",
            "layoutNegotiation=false",
            "compactSidebarRail=true",
            "compactInspectorSwap=true",
            "cardsKnowShellMode=false",
            "editorImplemented=false",
            "roslynEnabled=false",
            "xunitEnabled=false",
            "visionaryImplemented=false",
            $"deferredWork={string.Join(" | ", deferredWork)}",
        ];

        File.WriteAllText(jsonPath, json);
        File.WriteAllLines(textPath, textLines);
        return (jsonPath, textPath);
    }
}
