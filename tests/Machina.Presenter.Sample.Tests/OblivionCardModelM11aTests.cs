using Machina.Dominatus.Rendering.Commands;
using Machina.Layout.Geometry;
using Machina.Pipeline;
using Machina.Presenter.Sample;
using Machina.Standard.Theme;
using Xunit;
using Machina.Core.Flat;

namespace Machina.Presenter.Sample.Tests;

public sealed class OblivionCardModelM11aTests
{
    [Fact]
    public void OblivionCard_HasStableId()
    {
        OblivionCard card = OblivionWorkbenchCatalog.CreateCardsPageCards()[0];

        Assert.Equal("oblivion-intro-note-card", card.Id.Value);
    }

    [Fact]
    public void OblivionCard_SupportsRequiredKinds()
    {
        Assert.Equal(
            [
                OblivionCardKind.Note,
                OblivionCardKind.Status,
                OblivionCardKind.UiPreview,
                OblivionCardKind.Artifact,
                OblivionCardKind.CodeFact,
                OblivionCardKind.CodeTheory,
            ],
            Enum.GetValues<OblivionCardKind>());
    }

    [Fact]
    public void OblivionCard_SupportsRequiredStatuses()
    {
        Assert.Equal(
            [
                OblivionCardStatus.Idle,
                OblivionCardStatus.Passing,
                OblivionCardStatus.Failing,
                OblivionCardStatus.Warning,
                OblivionCardStatus.Deferred,
                OblivionCardStatus.Placeholder,
            ],
            Enum.GetValues<OblivionCardStatus>());
    }

    [Fact]
    public void OblivionCard_ActionsAreMetadataOnlyInM11a()
    {
        OblivionCard card = Assert.Single(
            OblivionWorkbenchCatalog.CreateCardsPageCards(),
            candidate => candidate.Kind == OblivionCardKind.UiPreview);

        OblivionCardAction action = Assert.Single(card.Actions);
        Assert.False(action.Enabled);
        Assert.Equal("Open preview", action.Label);
    }

    [Fact]
    public void OblivionCard_ArtifactsAreMetadataOnlyInM11a()
    {
        OblivionCard card = Assert.Single(
            OblivionWorkbenchCatalog.CreateCardsPageCards(),
            candidate => candidate.Kind == OblivionCardKind.Artifact);

        Assert.All(card.Artifacts, artifact => Assert.Null(artifact.Path));
    }

    [Fact]
    public void OblivionCardRenderer_RendersTitleKindStatusTags()
    {
        MachinaFrame frame = RenderCard(
            new OblivionCard(
                new OblivionCardId("renderer-smoke-card"),
                OblivionCardKind.Note,
                OblivionCardStatus.Idle,
                "Renderer smoke card",
                "Subtitle",
                ["alpha", "beta"],
                ["One body line."],
                [],
                []),
            560,
            180);

        List<string> text = frame.RenderCommands.OfType<DrawTextCommand>().Select(command => command.Text).ToList();

        Assert.Contains("Renderer smoke card", text);
        Assert.Contains("Note", text);
        Assert.Contains("Idle", text);
        Assert.Contains("alpha", text);
        Assert.Contains("beta", text);
    }

    [Fact]
    public void OblivionCardRenderer_UsesFiniteBounds()
    {
        OblivionCard card = OblivionWorkbenchCatalog.CreateCardsPageCards()[0];
        MachinaFrame frame = RenderCard(card, 560, OblivionWorkbenchCatalog.GetCardHeight(card));
        PresenterCardFrame cardFrame = OblivionCardRenderer.DescribeFrame(frame.Resolved, card.Id.Value);

        AssertFinite(cardFrame.Bounds);
        AssertFinite(cardFrame.ContentBounds);
    }

    [Fact]
    public void OblivionCardRenderer_ClipsOrTruncatesOverflow()
    {
        OblivionCard card = new(
            new OblivionCardId("overflow-card"),
            OblivionCardKind.Note,
            OblivionCardStatus.Warning,
            "Overflow",
            null,
            ["overflow"],
            Enumerable.Range(0, 12)
                .Select(index => $"This is a deliberately long overflow line {index} that should be truncated to stay inside the card body bounds.")
                .ToArray(),
            [],
            []);

        MachinaFrame frame = RenderCard(card, 320, 188);
        List<string> visibleBodyLines = frame.RenderCommands
            .OfType<DrawTextCommand>()
            .Where(command => command.Id.Contains("overflow-card.body-line-", StringComparison.Ordinal))
            .Select(command => command.Text)
            .ToList();

        Assert.NotEmpty(visibleBodyLines);
        Assert.True(visibleBodyLines.Count < card.BodyLines.Count);
        Assert.Contains(visibleBodyLines, line => line.EndsWith("...", StringComparison.Ordinal));
    }

    [Fact]
    public void OblivionCardRenderer_DoesNotBleedOutsideCard()
    {
        OblivionCard card = Assert.Single(
            OblivionWorkbenchCatalog.CreateCardsPageCards(),
            candidate => candidate.Kind == OblivionCardKind.CodeTheory);
        MachinaFrame frame = RenderCard(card, 560, OblivionWorkbenchCatalog.GetCardHeight(card));
        PresenterCardFrame cardFrame = OblivionCardRenderer.DescribeFrame(frame.Resolved, card.Id.Value);

        IReadOnlyList<DrawTextCommand> bodyCommands = frame.RenderCommands
            .OfType<DrawTextCommand>()
            .Where(command => command.Id.Contains(card.Id.Value, StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(bodyCommands);
        Assert.All(bodyCommands, command => AssertRectInside(command.Rect, cardFrame.Bounds, command.Id));
    }

    [Fact]
    public void OblivionCardRenderer_RendersCodeFactPlaceholderWithoutExecuting()
    {
        PresenterPageRenderResult page = RenderPage(OblivionWorkbenchCatalog.CardsPageId);
        List<string> text = page.Frame.RenderCommands.OfType<DrawTextCommand>().Select(command => command.Text).ToList();

        Assert.Contains("[Fact]", text);
        Assert.Contains(text, line => line.Contains("SettingsCard_Renders", StringComparison.Ordinal));
        Assert.Contains("not executed in M11a", text);
    }

    [Fact]
    public void OblivionCardRenderer_RendersCodeTheoryPlaceholderWithoutExecuting()
    {
        PresenterPageRenderResult page = RenderPage(OblivionWorkbenchCatalog.CardsPageId);
        List<string> text = page.Frame.RenderCommands.OfType<DrawTextCommand>().Select(command => command.Text).ToList();

        Assert.Contains("[Theory]", text);
        Assert.Contains("[InlineData(16)]", text);
        Assert.Contains(text, line => line.Contains("TextProof_RendersAtSize", StringComparison.Ordinal));
        Assert.Contains("not executed in M11a", text);
    }

    [Fact]
    public void PresenterShell_ContainsOblivionSection()
    {
        Assert.Contains(PresenterNavigationCatalog.CreateModel().Sections, section => section.Id == "oblivion");
    }

    [Fact]
    public void PresenterShell_OblivionSectionContainsCardsTab()
    {
        PresenterNavigationSection section = Assert.Single(
            PresenterNavigationCatalog.CreateModel().Sections,
            candidate => candidate.Id == "oblivion");

        Assert.Contains(section.Tabs, tab => tab.Id == "cards" && tab.PageId == "oblivion.cards");
    }

    [Fact]
    public void PresenterShell_OblivionSectionContainsExecutionRoadmapTab()
    {
        PresenterNavigationSection section = Assert.Single(
            PresenterNavigationCatalog.CreateModel().Sections,
            candidate => candidate.Id == "oblivion");

        Assert.Contains(section.Tabs, tab => tab.Id == "execution-roadmap" && tab.PageId == "oblivion.execution-roadmap");
    }

    [Fact]
    public void PresenterShell_OblivionSectionContainsArtifactsTab()
    {
        PresenterNavigationSection section = Assert.Single(
            PresenterNavigationCatalog.CreateModel().Sections,
            candidate => candidate.Id == "oblivion");

        Assert.Contains(section.Tabs, tab => tab.Id == "artifacts" && tab.PageId == "oblivion.artifacts");
    }

    [Fact]
    public void PresenterShell_OblivionCardsPageContainsRequiredCards()
    {
        IReadOnlyList<string> cardIds = OblivionWorkbenchCatalog.CreateCardsPageCards()
            .Select(card => card.Id.Value)
            .ToArray();

        Assert.Equal(
            [
                "oblivion-intro-note-card",
                "oblivion-static-status-card",
                "oblivion-ui-preview-card",
                "oblivion-artifact-placeholder-card",
                "oblivion-code-fact-card",
                "oblivion-code-theory-card",
            ],
            cardIds);
    }

    [Fact]
    public void PresenterShell_DefaultStillStartsAtOverviewHome()
    {
        PresenterNavigationShellRenderResult render = PresenterNavigationShellRenderer.Render(
            DemoState.Default,
            PresenterNavigationState.CreateDefault(PresenterNavigationCatalog.CreateModel()),
            StandardTheme.Default,
            new PresenterProofOptions());

        Assert.Equal("overview", render.SelectedSection.Id);
        Assert.Equal("home", render.SelectedTab.Id);
    }

    [Fact]
    public void ExportPresenter_OblivionCardsWritesArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-oblivion-cards.png"),
                new PresenterProofOptions(),
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "oblivion",
                    SelectedTabId: "cards"),
                StandardTheme.Default);

            Assert.True(File.Exists(result.OutputPath));
            Assert.Equal("oblivion.cards", result.NavigationPageId);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void ExportPresenter_OblivionExecutionRoadmapWritesArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-oblivion-execution-roadmap.png"),
                new PresenterProofOptions(),
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "oblivion",
                    SelectedTabId: "execution-roadmap"),
                StandardTheme.Default);

            Assert.True(File.Exists(result.OutputPath));
            Assert.Equal("oblivion.execution-roadmap", result.NavigationPageId);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void ExportPresenter_OblivionArtifactsWritesArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-oblivion-artifacts.png"),
                new PresenterProofOptions(),
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "oblivion",
                    SelectedTabId: "artifacts"),
                StandardTheme.Default);

            Assert.True(File.Exists(result.OutputPath));
            Assert.Equal("oblivion.artifacts", result.NavigationPageId);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void ExportPresenter_OblivionManifestWritesJsonAndText()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-oblivion-cards.png"),
                new PresenterProofOptions(),
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "oblivion",
                    SelectedTabId: "cards"),
                StandardTheme.Default);

            Assert.True(File.Exists(result.OblivionManifestJsonPath!));
            Assert.True(File.Exists(result.OblivionManifestTextPath!));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void M11a_DoesNotReferenceRoslyn()
    {
        string projectText = File.ReadAllText(GetSampleProjectPath());

        Assert.DoesNotContain("Microsoft.CodeAnalysis", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("Roslyn", projectText, StringComparison.Ordinal);
    }

    [Fact]
    public void M11a_DoesNotReferenceXunitExecutionRuntime()
    {
        string sampleDirectory = Path.Combine(GetRepositoryRoot(), "samples", "Machina.Presenter.Sample");
        string[] sourceFiles = Directory.GetFiles(sampleDirectory, "*.cs", SearchOption.AllDirectories);
        string combinedText = string.Join(
            Environment.NewLine,
            sourceFiles.Select(File.ReadAllText));

        Assert.DoesNotContain("using Xunit", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("Xunit.Sdk", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("ITestOutputHelper", combinedText, StringComparison.Ordinal);
    }

    [Fact]
    public void M11a_DoesNotImplementVisionaryEditor()
    {
        PresenterNavigationModel model = PresenterNavigationCatalog.CreateModel();

        Assert.DoesNotContain(model.Sections, section => section.Id == "visionary");
        Assert.DoesNotContain(
            model.Sections.SelectMany(section => section.Tabs),
            tab => tab.PageId.Contains("visionary", StringComparison.OrdinalIgnoreCase));
    }

    private static PresenterPageRenderResult RenderPage(string pageId)
    {
        return PresenterNavigationCatalog.RenderPage(
            pageId,
            DemoState.Default,
            StandardTheme.Default,
            new PresenterProofOptions(),
            PresenterNavigationLayout.Default.ContentVisibleWidth);
    }

    private static MachinaFrame RenderCard(OblivionCard card, int width, double height)
    {
        UiDocument document = UiDocument.Create(
            [
                Row.Root("root"),
                Row.Anchor(
                    "card-anchor",
                    "root",
                    left: 0,
                    top: 0,
                    width: width,
                    height: height,
                    component: OblivionCardRenderer.BuildCard(
                        card,
                        StandardTheme.Default,
                        new OblivionCardRenderOptions(width, height))),
            ]);

        return new MachinaRasterPipeline().Render(document, width, (int)Math.Ceiling(height));
    }

    private static void AssertFinite(Rect rect)
    {
        Assert.True(double.IsFinite(rect.X));
        Assert.True(double.IsFinite(rect.Y));
        Assert.True(double.IsFinite(rect.Width));
        Assert.True(double.IsFinite(rect.Height));
    }

    private static void AssertRectInside(Rect inner, Rect outer, string id)
    {
        Assert.True(inner.X >= outer.X, $"{id} left outside");
        Assert.True(inner.Y >= outer.Y, $"{id} top outside");
        Assert.True(inner.X + inner.Width <= outer.X + outer.Width, $"{id} right outside");
        Assert.True(inner.Y + inner.Height <= outer.Y + outer.Height, $"{id} bottom outside");
    }

    private static string CreateOutputDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "machina-presenter-m11a-tests", Guid.NewGuid().ToString("N"));
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static string GetSampleProjectPath()
    {
        return Path.Combine(GetRepositoryRoot(), "samples", "Machina.Presenter.Sample", "Machina.Presenter.Sample.csproj");
    }
}
