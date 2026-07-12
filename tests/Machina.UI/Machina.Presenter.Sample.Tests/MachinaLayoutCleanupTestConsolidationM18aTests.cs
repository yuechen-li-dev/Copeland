using System.Text.Json;
using Machina.Core.Styling;
using Machina.Dominatus.Rendering.Commands;
using Machina.Layout.Documents;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;
using Machina.Presenter.Sample;
using Machina.Presenter.Sample.Playback;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class MachinaLayoutCleanupTestConsolidationM18aTests
{
    [Fact]
    public void OblivionInspector_TitleDoesNotClipInWideMode()
    {
        PresenterPageRenderResult page = PresenterSampleTestHarness.RenderDocsPage();
        Rect titleSlot = FindRectBySuffix(page.Frame.Resolved, "oblivion.docs.wide-inspector.title.slot");
        DrawTextCommand[] titleCommands = FindWideInspectorTitleCommands(page);

        Assert.NotEmpty(titleCommands);
        Assert.All(titleCommands, command => PresenterRegionAssert.RectInside(command.Rect, titleSlot, command.Id));
    }

    [Fact]
    public void OblivionInspector_TitleUsesReadableOverflowPolicy()
    {
        PresenterPageRenderResult page = PresenterSampleTestHarness.RenderDocsPage();
        Rect titleSlot = FindRectBySuffix(page.Frame.Resolved, "oblivion.docs.wide-inspector.title.slot");
        DrawTextCommand title = Assert.Single(FindWideInspectorTitleCommands(page));
        PushClipCommand clip = Assert.Single(
            page.Frame.RenderCommands.OfType<PushClipCommand>(),
            command => command.Rect == titleSlot);

        Assert.Equal(TextSize.Md, title.Style.Size);
        Assert.Equal(titleSlot, clip.Rect);
    }

    [Fact]
    public void OblivionInspector_TitleDoesNotOverlapFirstSection()
    {
        PresenterPageRenderResult page = PresenterSampleTestHarness.RenderDocsPage();
        Rect titleSlot = FindRectBySuffix(page.Frame.Resolved, "oblivion.docs.wide-inspector.title.slot");
        Rect firstSection = FindRectBySuffix(page.Frame.Resolved, "oblivion.docs.wide-inspector-section-0");

        PresenterRegionAssert.DoesNotOverlapVertically(
            titleSlot,
            firstSection,
            "wide inspector title and first section");
    }

    [Fact]
    public void M18a_SharedTestHelpersExist()
    {
        Assert.Equal(1280, PresenterSampleTestHarness.WideWidth);
        Assert.Equal(720, PresenterSampleTestHarness.WideHeight);
        Assert.False(string.IsNullOrWhiteSpace(ManifestTestHelper.RepoRoot));
        Assert.NotNull(typeof(PresenterRegionAssert).GetMethod(nameof(PresenterRegionAssert.SingleScrollRegion)));
    }

    [Fact]
    public void M18a_ManifestHelpersPreserveMilestoneAssertions()
    {
        using JsonDocument manifest = LoadM18aManifest();
        JsonElement root = manifest.RootElement;

        ManifestTestHelper.AssertMilestoneAndKind(
            root,
            "M18a",
            "machina-layout-cleanup-test-consolidation");
        ManifestTestHelper.AssertBoolean(root, "testHelperDuplicationAudited", true);
    }

    [Fact]
    public void M18a_PlaybackScenarioHelpersResolveCanonicalPaths()
    {
        string starterPath = PlaybackTestEnvironment.GetScenarioPath("oblivion-inspector-scroll.machina-playback.toml");
        string suitePath = PlaybackTestEnvironment.GetSuiteManifestPath("m16c-oblivion-playback-suite.machina-playback-suite.toml");
        IReadOnlyList<PlaybackScenarioFile> canonicalScenarios = PlaybackScenarioDiscovery.AllCanonicalScenarios();

        Assert.True(File.Exists(starterPath));
        Assert.True(File.Exists(suitePath));
        Assert.NotEmpty(canonicalScenarios);
        Assert.All(canonicalScenarios, scenario => Assert.True(File.Exists(scenario.ScenarioPath)));
    }

    [Fact]
    public void M18a_RegionAssertHelpersPreserveExplicitAssertions()
    {
        PresenterNavigationShellRenderResult render = PresenterSampleTestHarness.RenderShell(
            PresenterSampleTestHarness.CreateDocsState());

        OblivionScrollRegionTarget inspector = PresenterRegionAssert.SingleScrollRegion(
            render,
            PresenterScrollbarTargetKind.OblivionInspectorPane);

        Assert.Equal(PresenterScrollbarTargetKind.OblivionInspectorPane, inspector.Target.Kind);
        Assert.True(inspector.Bounds.Width > 0);
        Assert.True(inspector.Bounds.Height > 0);
    }

    [Fact]
    public void M18a_DoesNotImplementNewLayoutPrimitive()
    {
        using JsonDocument manifest = LoadM18aManifest();
        JsonElement root = manifest.RootElement;

        Assert.False(root.GetProperty("newLayoutPrimitiveImplemented").GetBoolean());
        Assert.False(root.GetProperty("proportionalUiLengthImplemented").GetBoolean());
        Assert.False(root.GetProperty("rowVariantsImplemented").GetBoolean());
        Assert.False(root.GetProperty("guideFrameImplemented").GetBoolean());
        Assert.False(root.GetProperty("deusMachineImplemented").GetBoolean());
    }

    [Fact]
    public void M18a_DoesNotDeleteCoverage()
    {
        using JsonDocument manifest = LoadM18aManifest();
        JsonElement root = manifest.RootElement;

        Assert.Equal(0, root.GetProperty("testsDeleted").GetInt32());
        Assert.False(root.GetProperty("coverageIntentionallyReduced").GetBoolean());
    }

    [Fact]
    public void M18a_DoesNotImplementMarkdownEditing()
    {
        using JsonDocument manifest = LoadM18aManifest();
        Assert.False(manifest.RootElement.GetProperty("markdownEditingImplemented").GetBoolean());
    }

    [Fact]
    public void M18a_DoesNotImplementNotebookExecution()
    {
        using JsonDocument manifest = LoadM18aManifest();
        Assert.False(manifest.RootElement.GetProperty("notebookExecutionImplemented").GetBoolean());
    }

    [Fact]
    public void M18a_DoesNotPerformAurelianWork()
    {
        using JsonDocument manifest = LoadM18aManifest();
        Assert.False(manifest.RootElement.GetProperty("aurelianWorkPerformed").GetBoolean());
    }

    [Fact]
    public void M18a_DoesNotPerformVdMirWork()
    {
        using JsonDocument manifest = LoadM18aManifest();
        Assert.False(manifest.RootElement.GetProperty("vdMirWorkPerformed").GetBoolean());
    }

    private static DrawTextCommand[] FindWideInspectorTitleCommands(PresenterPageRenderResult page)
    {
        return page.Frame.RenderCommands
            .OfType<DrawTextCommand>()
            .Where(command => command.Text == "Selected card inspector")
            .ToArray();
    }

    private static Rect FindRectBySuffix(ResolvedLayoutDocument resolved, string suffix)
    {
        KeyValuePair<NodeId, ResolvedLayoutNode> match = Assert.Single(
            resolved.Nodes,
            pair => pair.Key.Value.EndsWith(suffix, StringComparison.Ordinal));
        return match.Value.Rect;
    }

    private static JsonDocument LoadM18aManifest()
    {
        return ManifestTestHelper.LoadArtifactManifest(
            "m18a",
            "machina-layout-cleanup-test-consolidation-manifest.json");
    }
}
