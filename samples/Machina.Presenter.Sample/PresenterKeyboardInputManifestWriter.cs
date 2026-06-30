using System.Text.Json;

namespace Machina.Presenter.Sample;

public static class PresenterKeyboardInputManifestWriter
{
    public const string JsonFileName = "presenter-keyboard-input-manifest.json";
    public const string TextFileName = "presenter-keyboard-input-manifest.txt";

    public static (string jsonPath, string textPath) Write(
        string outputDirectory,
        PresenterNavigationShellRenderResult render,
        string? interactionBackendName)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(render);

        Directory.CreateDirectory(outputDirectory);

        string jsonPath = Path.Combine(outputDirectory, JsonFileName);
        string textPath = Path.Combine(outputDirectory, TextFileName);

        string[] supportedKeys = PresenterKeyboardInputSupport.SupportedKeys.ToArray();
        string[] supportedShortcuts = PresenterKeyboardInputSupport.SupportedShortcuts.ToArray();
        string[] deferredWork = PresenterKeyboardInputSupport.DeferredWork.ToArray();

        var manifest = new
        {
            milestone = "M12g",
            kind = "presenter-keyboard-input-backend",
            keyboardBackendEnabled = true,
            avaloniaAdapter = true,
            textInputTranslated = true,
            textEditingImplemented = false,
            markdownEditorImplemented = false,
            roslynEnabled = false,
            xunitEnabled = false,
            visionaryImplemented = false,
            interactionBackend = interactionBackendName ?? "none",
            backendBoundary = "sample-scoped-adapter",
            selectedSection = render.SelectedSection.Id,
            selectedTab = render.SelectedTab.Id,
            selectedPage = render.SelectedTab.PageId,
            supportedKeys,
            supportedShortcuts,
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
            "milestone=M12g",
            "kind=presenter-keyboard-input-backend",
            "keyboardBackendEnabled=true",
            "avaloniaAdapter=true",
            "textInputTranslated=true",
            "textEditingImplemented=false",
            "markdownEditorImplemented=false",
            "roslynEnabled=false",
            "xunitEnabled=false",
            "visionaryImplemented=false",
            $"interactionBackend={interactionBackendName ?? "none"}",
            "backendBoundary=sample-scoped-adapter",
            $"selectedSection={render.SelectedSection.Id}",
            $"selectedTab={render.SelectedTab.Id}",
            $"selectedPage={render.SelectedTab.PageId}",
            $"supportedKeys={string.Join(",", supportedKeys)}",
            $"supportedShortcuts={string.Join(" | ", supportedShortcuts)}",
            $"deferredWork={string.Join(" | ", deferredWork)}",
        ];

        File.WriteAllText(jsonPath, json);
        File.WriteAllLines(textPath, textLines);
        return (jsonPath, textPath);
    }
}
