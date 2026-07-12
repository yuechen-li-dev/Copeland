using System.Text.Json;
using Machina.Presenter.Sample.Playback;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class PlaybackParityM16bTests
{
    private const string DocsPageId = "oblivion.docs";
    private const string MarkdownDocCardId = "doc-aurelian-build-topology-m13b";

    [Fact]
    public void PlaybackParity_MainStackWheel_ResolvesOblivionMainStackRegion()
    {
        PresenterNavigationShellRenderResult render = PlaybackTestEnvironment.RenderDocsShell(
            selectedCardId: MarkdownDocCardId);

        PresenterPlaybackResolvedTarget target = PresenterPlaybackTargetResolver.Resolve(render, "main-stack");

        Assert.Equal("oblivion-main-card-stack", target.ResolvedRegionKind);
        Assert.Equal($"{DocsPageId}.main-stack", target.ResolvedRegionId);
        Assert.True(target.Bounds.Width > 0);
        Assert.True(target.Bounds.Height > 0);
    }

    [Fact]
    public void PlaybackParity_MainStackWheel_DispatchesMainStackScrollAction()
    {
        PresenterPlaybackRunResult result = RunStarterScenario("oblivion-main-stack-scroll.machina-playback.toml");

        PresenterPlaybackTraceStep step = Assert.Single(result.Trace.Steps, value => value.Type == "wheel");
        Assert.Equal("set-oblivion-main-card-stack-scroll-offset", step.DispatchedAction!.ActionType);
    }

    [Fact]
    public void PlaybackParity_MainStackWheel_UpdatesMainStackOffset()
    {
        PresenterPlaybackRunResult result = RunStarterScenario("oblivion-main-stack-scroll.machina-playback.toml");

        Assert.True(result.FinalState.GetScrollOffset(DocsPageId) > 0);
    }

    [Fact]
    public void PlaybackParity_MainStackWheel_DoesNotUpdateInspectorOffset()
    {
        PresenterPlaybackRunResult result = RunStarterScenario("oblivion-main-stack-scroll.machina-playback.toml");

        Assert.Equal(0, result.FinalState.GetInspectorScrollOffset(DocsPageId));
    }

    [Fact]
    public void PlaybackParity_MainStackWheel_DoesNotUseGenericPageScrollClamp()
    {
        PresenterPlaybackRunResult result = RunStarterScenario("oblivion-main-stack-scroll.machina-playback.toml");

        PresenterPlaybackTraceStep step = Assert.Single(result.Trace.Steps, value => value.Type == "wheel");
        Assert.NotEqual("set-scroll-offset", step.DispatchedAction!.ActionType);
    }

    [Fact]
    public void PlaybackParity_MainStackWheel_RenderSessionDoesNotOverwriteDedicatedMainStackScrollState()
    {
        PresenterNavigationShellRenderResult render = PlaybackTestEnvironment.RenderDocsShell(
            selectedCardId: MarkdownDocCardId,
            mainScrollOffset: 48);

        Assert.Equal(48, render.NavigationState.GetScrollOffset(DocsPageId));
    }

    [Fact]
    public void PlaybackParity_RawSourceWheel_ResolvesRawSourceRegion()
    {
        PresenterNavigationShellRenderResult render = PlaybackTestEnvironment.RenderDocsShell(
            selectedCardId: MarkdownDocCardId,
            expandedCardId: MarkdownDocCardId,
            inspectorScrollOffset: 240,
            width: 1280,
            height: 360);

        PresenterPlaybackResolvedTarget target = PresenterPlaybackTargetResolver.Resolve(render, "raw-source", MarkdownDocCardId);

        Assert.Equal("oblivion-inspector-raw-markdown-source", target.ResolvedRegionKind);
        Assert.Equal($"{DocsPageId}.{MarkdownDocCardId}.raw-source", target.ResolvedRegionId);
        Assert.True(target.Bounds.Width > 0);
        Assert.True(target.Bounds.Height > 0);
    }

    [Fact]
    public void PlaybackParity_RawSourceWheel_DispatchesRawSourceScrollAction()
    {
        PresenterPlaybackRunResult result = RunStarterScenario("oblivion-raw-source-scroll.machina-playback.toml");

        PresenterPlaybackTraceStep step = Assert.Single(result.Trace.Steps, value => value.Type == "wheel");
        Assert.Equal("set-oblivion-raw-markdown-source-scroll-offset", step.DispatchedAction!.ActionType);
    }

    [Fact]
    public void PlaybackParity_RawSourceWheel_UpdatesRawSourceOffset()
    {
        PresenterPlaybackRunResult result = RunStarterScenario("oblivion-raw-source-scroll.machina-playback.toml");

        Assert.True(result.FinalState.GetRawMarkdownSourceScrollOffset(MarkdownDocCardId) > 0);
    }

    [Fact]
    public void PlaybackParity_RawSourceWheel_DoesNotUpdateMainStackOffset()
    {
        PresenterPlaybackRunResult result = RunStarterScenario("oblivion-raw-source-scroll.machina-playback.toml");

        Assert.Equal(0, result.FinalState.GetScrollOffset(DocsPageId));
    }

    [Fact]
    public void PlaybackParity_RawSourceWheel_DoesNotFallBackToInspectorPane()
    {
        PresenterPlaybackRunResult result = RunStarterScenario("oblivion-raw-source-scroll.machina-playback.toml");

        PresenterPlaybackTraceStep step = Assert.Single(result.Trace.Steps, value => value.Type == "wheel");
        Assert.Equal("oblivion-inspector-raw-markdown-source", step.HitTestResult!.RegionKind);
    }

    [Fact]
    public void PlaybackParity_RawSourceWheel_RejectsOffscreenRawSourceRegionUntilInspectorViewportRevealsIt()
    {
        PresenterNavigationShellRenderResult hiddenRender = PlaybackTestEnvironment.RenderDocsShell(
            selectedCardId: MarkdownDocCardId,
            expandedCardId: MarkdownDocCardId,
            width: 1280,
            height: 360);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => PresenterPlaybackTargetResolver.Resolve(hiddenRender, "raw-source", MarkdownDocCardId));
        Assert.Contains("outside the visible presenter content viewport", ex.Message, StringComparison.Ordinal);

        PresenterNavigationShellRenderResult visibleRender = PlaybackTestEnvironment.RenderDocsShell(
            selectedCardId: MarkdownDocCardId,
            expandedCardId: MarkdownDocCardId,
            inspectorScrollOffset: 240,
            width: 1280,
            height: 360);
        PresenterPlaybackResolvedTarget visibleTarget = PresenterPlaybackTargetResolver.Resolve(
            visibleRender,
            "raw-source",
            MarkdownDocCardId);

        Assert.True(visibleTarget.Bounds.Height > 0);
    }

    [Fact]
    public void PlaybackTrace_IncludesTargetResolution()
    {
        PresenterPlaybackRunResult result = RunStarterScenario("oblivion-main-stack-scroll.machina-playback.toml");

        PresenterPlaybackTraceStep step = Assert.Single(result.Trace.Steps);
        Assert.NotNull(step.TargetResolution);
        Assert.Equal("main-stack", step.TargetResolution!.SemanticTargetKind);
        Assert.Equal("oblivion-main-card-stack", step.TargetResolution.ResolvedRegionKind);
    }

    [Fact]
    public void PlaybackTrace_IncludesHitTestResult()
    {
        PresenterPlaybackRunResult result = RunStarterScenario("oblivion-raw-source-scroll.machina-playback.toml");

        PresenterPlaybackTraceStep step = Assert.Single(result.Trace.Steps);
        Assert.NotNull(step.HitTestResult);
        Assert.Equal("oblivion-inspector-raw-markdown-source", step.HitTestResult!.RegionKind);
    }

    [Fact]
    public void PlaybackTrace_IncludesDispatchedAction()
    {
        PresenterPlaybackRunResult result = RunStarterScenario("oblivion-main-stack-scroll.machina-playback.toml");

        PresenterPlaybackTraceStep step = Assert.Single(result.Trace.Steps);
        Assert.NotNull(step.DispatchedAction);
        Assert.True(step.DispatchedAction!.ActionHandled);
    }

    [Fact]
    public void PlaybackTrace_IncludesStateDeltas()
    {
        PresenterPlaybackRunResult result = RunStarterScenario("oblivion-main-stack-scroll.machina-playback.toml");

        PresenterPlaybackTraceStep step = Assert.Single(result.Trace.Steps);
        Assert.NotNull(step.StateDelta);
        Assert.True(step.StateDelta!.MainStackScrollDelta > 0);
        Assert.Equal(0, step.StateDelta.InspectorScrollDelta);
    }

    [Fact]
    public void PlaybackTrace_RemainsDeterministic()
    {
        PresenterPlaybackRunResult left = RunStarterScenario("oblivion-main-stack-scroll.machina-playback.toml");
        PresenterPlaybackRunResult right = RunStarterScenario("oblivion-main-stack-scroll.machina-playback.toml");

        string leftJson = JsonSerializer.Serialize(left.Trace);
        string rightJson = JsonSerializer.Serialize(right.Trace);
        Assert.Equal(leftJson, rightJson);
    }

    [Fact]
    public void PlaybackScenario_OblivionExpandCollapse_Passes()
    {
        AssertStarterScenarioPasses("oblivion-expand-collapse.machina-playback.toml");
    }

    [Fact]
    public void PlaybackScenario_OblivionExpandedBodyScroll_Passes()
    {
        AssertStarterScenarioPasses("oblivion-expanded-body-scroll.machina-playback.toml");
    }

    [Fact]
    public void PlaybackScenario_OblivionMainStackScroll_Passes()
    {
        AssertStarterScenarioPasses("oblivion-main-stack-scroll.machina-playback.toml");
    }

    [Fact]
    public void PlaybackScenario_OblivionInspectorScroll_Passes()
    {
        AssertStarterScenarioPasses("oblivion-inspector-scroll.machina-playback.toml");
    }

    [Fact]
    public void PlaybackScenario_OblivionRawSourceScroll_Passes()
    {
        AssertStarterScenarioPasses("oblivion-raw-source-scroll.machina-playback.toml");
    }

    [Fact]
    public void M16b_DoesNotImplementNativeOsAutomation()
    {
        using JsonDocument manifest = LoadMilestoneManifest();
        Assert.False(manifest.RootElement.GetProperty("nativeOsAutomationImplemented").GetBoolean());
    }

    [Fact]
    public void M16b_DoesNotImplementPixelGoldenDiffing()
    {
        using JsonDocument manifest = LoadMilestoneManifest();
        Assert.False(manifest.RootElement.GetProperty("pixelGoldenDiffingImplemented").GetBoolean());
    }

    [Fact]
    public void M16b_DoesNotImplementMarkdownEditing()
    {
        using JsonDocument manifest = LoadMilestoneManifest();
        Assert.False(manifest.RootElement.GetProperty("markdownEditingImplemented").GetBoolean());
    }

    [Fact]
    public void M16b_DoesNotImplementNotebookExecution()
    {
        using JsonDocument manifest = LoadMilestoneManifest();
        Assert.False(manifest.RootElement.GetProperty("notebookExecutionImplemented").GetBoolean());
    }

    [Fact]
    public void M16b_DoesNotPerformAurelianWork()
    {
        using JsonDocument manifest = LoadMilestoneManifest();
        Assert.False(manifest.RootElement.GetProperty("aurelianWorkPerformed").GetBoolean());
    }

    [Fact]
    public void M16b_DoesNotPerformVdMirWork()
    {
        using JsonDocument manifest = LoadMilestoneManifest();
        Assert.False(manifest.RootElement.GetProperty("vdMirWorkPerformed").GetBoolean());
    }

    private static PresenterPlaybackRunResult RunStarterScenario(string fileName)
    {
        string outputFilePath = PlaybackTestEnvironment.CreateOutputFilePath(Path.GetFileNameWithoutExtension(fileName));
        return PlaybackTestEnvironment.CreateRunner().RunScenarioFile(
            PlaybackTestEnvironment.GetScenarioPath(fileName),
            outputFilePath);
    }

    private static void AssertStarterScenarioPasses(string fileName)
    {
        PresenterPlaybackRunResult result = RunStarterScenario(fileName);
        Assert.All(result.Trace.Assertions, assertion => Assert.True(assertion.Passed, assertion.FailureMessage));
    }

    private static JsonDocument LoadMilestoneManifest()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "machina-playback-m16b-manifest", Guid.NewGuid().ToString("N"));
        try
        {
            (string jsonPath, _) = PresenterPlaybackOutputWriter.WriteMilestoneManifest(outputDirectory);
            return JsonDocument.Parse(File.ReadAllText(jsonPath));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }
}
