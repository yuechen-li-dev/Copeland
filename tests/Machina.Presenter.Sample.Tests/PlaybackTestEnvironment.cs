using Machina.Presenter.Sample.Playback;
using Machina.Standard.Theme;

namespace Machina.Presenter.Sample.Tests;

internal static class PlaybackTestEnvironment
{
    public static PresenterPlaybackRunner CreateRunner()
    {
        return new PresenterPlaybackRunner(
            DemoState.Default,
            StandardTheme.Default,
            new PresenterProofOptions());
    }

    public static string CreateOutputFilePath(string scenarioId)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "machina-presenter-playback-m16a-tests",
            scenarioId,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "final.png");
    }

    public static string GetScenarioPath(string fileName)
    {
        return Path.Combine(GetRepoRoot(), "samples", "Machina.Presenter.Sample", "PlaybackScenarios", fileName);
    }

    public static string GetRepoRoot()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Copeland.slnx")))
        {
            string? parent = Directory.GetParent(root)?.FullName;
            if (parent is null)
            {
                throw new InvalidOperationException("Could not find repo root.");
            }

            root = parent;
        }

        return root;
    }

    public static void DeleteOutputPath(string outputFilePath)
    {
        string? directory = Path.GetDirectoryName(outputFilePath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static PresenterNavigationShellRenderResult RenderOblivionShell(
        string tabId,
        string pageId,
        string selectedCardId,
        string? expandedCardId = null,
        double mainScrollOffset = 0,
        double inspectorScrollOffset = 0,
        double rawSourceScrollOffset = 0,
        double bodyScrollOffset = 0,
        int width = 1280,
        int height = 720)
    {
        PresenterNavigationModel model = PresenterNavigationCatalog.CreateModel();
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(model)
            .WithSelectedSection("oblivion")
            .WithSelectedTab("oblivion", tabId)
            .WithSelectedCard(pageId, selectedCardId)
            .WithScrollOffset(pageId, mainScrollOffset)
            .WithInspectorScrollOffset(pageId, inspectorScrollOffset)
            .WithRawMarkdownSourceScrollOffset(selectedCardId, rawSourceScrollOffset);

        if (!string.IsNullOrWhiteSpace(expandedCardId))
        {
            state = state.WithCardViewState(
                pageId,
                expandedCardId,
                new OblivionCardViewState(true, bodyScrollOffset));
        }

        PresenterShellMode shellMode = PresenterShellModeResolver.Resolve(width);
        PresenterNavigationLayout layout = PresenterNavigationLayout.Create(width, height, shellMode);
        return PresenterNavigationShellRenderer.Render(
            DemoState.Default,
            state,
            StandardTheme.Default,
            new PresenterProofOptions(),
            layout);
    }

    public static PresenterNavigationShellRenderResult RenderDocsShell(
        string selectedCardId = "doc-machina-oblivion-phase-closeout-m11g",
        string? expandedCardId = null,
        double mainScrollOffset = 0,
        double inspectorScrollOffset = 0,
        double rawSourceScrollOffset = 0,
        double bodyScrollOffset = 0,
        int width = 1280,
        int height = 720)
    {
        return RenderOblivionShell(
            "docs",
            OblivionWorkbenchCatalog.DocsPageId,
            selectedCardId,
            expandedCardId,
            mainScrollOffset,
            inspectorScrollOffset,
            rawSourceScrollOffset,
            bodyScrollOffset,
            width,
            height);
    }

    public static PresenterNavigationShellRenderResult RenderExecutionRoadmapShell(
        string selectedCardId = "markdown-first-roadmap",
        string? expandedCardId = null,
        double mainScrollOffset = 0,
        double inspectorScrollOffset = 0,
        double rawSourceScrollOffset = 0,
        double bodyScrollOffset = 0,
        int width = 1280,
        int height = 720)
    {
        return RenderOblivionShell(
            "execution-roadmap",
            OblivionWorkbenchCatalog.ExecutionRoadmapPageId,
            selectedCardId,
            expandedCardId,
            mainScrollOffset,
            inspectorScrollOffset,
            rawSourceScrollOffset,
            bodyScrollOffset,
            width,
            height);
    }

    public static PresenterPlaybackScenario LoadScenario(string fileName)
    {
        return PresenterPlaybackTomlParser.LoadFile(GetScenarioPath(fileName));
    }
}
