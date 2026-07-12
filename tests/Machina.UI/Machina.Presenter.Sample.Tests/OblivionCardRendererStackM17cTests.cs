using System.Text.Json;
using Machina.Core.Lowering;
using Machina.Layout.Compilation;
using Machina.Layout.Documents;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Layout.Rows;
using Machina.Presenter.Sample;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class OblivionCardRendererStackM17cTests
{
    private static readonly StandardTheme Theme = StandardTheme.Default;

    [Fact]
    public void OblivionCardRenderer_UsesStackForMainCardComposition()
    {
        OblivionCardRenderOptions options = new(Width: 420, Height: 240);
        UiLoweringResult lowered = LowerCard(CreatePlainView(), options);

        Assert.Contains(lowered.Rows, row => row.Id == new NodeId("m17c-plain.layout"));
        Assert.Contains(lowered.Rows, row => row.Id == new NodeId("m17c-plain.layout.item-0"));
        Assert.Contains(lowered.Rows, row => row.Id == new NodeId("m17c-plain.layout.item-1"));
    }

    [Fact]
    public void OblivionCardRenderer_DoesNotRequireManualSlotIdsForStackSections()
    {
        OblivionCardRenderOptions options = new(Width: 420, Height: 240);
        UiLoweringResult lowered = LowerCard(CreatePlainView(), options);

        Assert.DoesNotContain(
            lowered.Rows,
            row => row.Id.Value.Contains("m17c-plain", StringComparison.Ordinal) &&
                row.Id.Value.EndsWith(".slot", StringComparison.Ordinal));
    }

    [Fact]
    public void OblivionCardRenderer_PreservesCardSectionOrder()
    {
        OblivionCardRenderOptions options = new(Width: 420, Height: 240);
        ResolvedLayoutDocument resolved = ResolveCard(CreatePlainView(), options);

        Rect title = FindRectBySuffix(resolved, "m17c-plain.title");
        Rect meta = FindRectBySuffix(resolved, "m17c-plain.meta-row");
        Rect tags = FindRectBySuffix(resolved, "m17c-plain.tags-row");
        Rect body = FindRectBySuffix(resolved, "m17c-plain.body-frame");

        Assert.True(title.Y < meta.Y);
        Assert.True(meta.Y < tags.Y);
        Assert.True(tags.Y < body.Y);
    }

    [Fact]
    public void OblivionCardRenderer_BodyAndFooterDoNotOverlap()
    {
        OblivionCardRenderOptions options = new(Width: 420, Height: 260);
        ResolvedLayoutDocument resolved = ResolveCard(CreatePlainView(actionCount: 4, artifactCount: 4), options);

        Rect body = FindRectBySuffix(resolved, "m17c-plain.body-layout.item-0");
        Rect footer = FindRectBySuffix(resolved, "m17c-plain.body-layout.item-1");

        Assert.True(body.Y + body.Height <= footer.Y);
    }

    [Fact]
    public void OblivionCardRenderer_MarkdownBodyDoesNotPaintUnderFooter()
    {
        OblivionCardRenderOptions options = new(Width: 420, Height: 260);
        ResolvedLayoutDocument resolved = ResolveCard(CreateMarkdownView(actionCount: 4, artifactCount: 2), options);

        Rect firstLine = FindRectBySuffix(resolved, "m17c-markdown.body-line-0");
        Rect footer = FindRectBySuffix(resolved, "m17c-markdown.body-layout.item-1");

        Assert.True(firstLine.Y + firstLine.Height <= footer.Y);
    }

    [Fact]
    public void OblivionCardRenderer_PlainBodyDoesNotPaintUnderFooter()
    {
        OblivionCardRenderOptions options = new(Width: 420, Height: 260);
        ResolvedLayoutDocument resolved = ResolveCard(CreatePlainView(actionCount: 4, artifactCount: 2), options);

        Rect lastVisibleLine = FindRectBySuffix(resolved, "m17c-plain.body-line-1");
        Rect footer = FindRectBySuffix(resolved, "m17c-plain.body-layout.item-1");

        Assert.True(lastVisibleLine.Y + lastVisibleLine.Height <= footer.Y);
    }

    [Fact]
    public void OblivionCardRenderer_OverflowBadgeIsIncludedInFooterMeasurement()
    {
        OblivionCompactCardView view = CreatePlainView(actionCount: 5, artifactCount: 5);
        OblivionCardRenderOptions options = new(Width: 420, Height: 260);
        PresenterCardLayout layout = OblivionCardRenderer.ComputeLayout(
            view,
            options,
            Theme.Card.Default,
            OblivionCardRenderer.ComputeBodyTop(view, options));

        Rect footer = Assert.IsType<Rect>(layout.FooterRectInContent);
        Assert.Equal((options.RowHeight * 2) + options.SmallGap, footer.Height, 6);
    }

    [Fact]
    public void OblivionCardRenderer_RenderedBadgeRowsMatchMeasuredRows()
    {
        OblivionCardRenderOptions options = new(Width: 420, Height: 260);
        ResolvedLayoutDocument resolved = ResolveCard(CreatePlainView(actionCount: 5, artifactCount: 5), options);

        IReadOnlyList<NodeId> footerChildren = resolved.Children[new NodeId("m17c-plain.footer-stack")];

        Assert.Equal(2, footerChildren.Count);
        Assert.Equal(4, resolved.Children[new NodeId("m17c-plain.actions-row")].Count);
        Assert.Equal(4, resolved.Children[new NodeId("m17c-plain.artifacts-row")].Count);
    }

    [Fact]
    public void M17cDocsAndArtifacts_Exist()
    {
        Assert.True(File.Exists(Path.Combine(RepoRoot, "docs", "Oblivion", "oblivion-card-renderer-stack-refactor-m17c.md")));
        Assert.True(File.Exists(Path.Combine(RepoRoot, "artifacts", "m17c", "oblivion-card-renderer-stack-refactor-manifest.json")));
        Assert.True(File.Exists(Path.Combine(RepoRoot, "artifacts", "m17c", "oblivion-card-renderer-stack-refactor-manifest.txt")));
    }

    [Fact]
    public void M17cManifest_RecordsStackRefactor()
    {
        using JsonDocument manifest = LoadManifest();
        JsonElement root = manifest.RootElement;

        Assert.Equal("M17c", root.GetProperty("milestone").GetString());
        Assert.True(root.GetProperty("cardRendererUsesUiStack").GetBoolean());
        Assert.True(root.GetProperty("bodyFooterOverlapRiskFixed").GetBoolean());
        Assert.True(root.GetProperty("measurementRenderBadgeModelUnified").GetBoolean());
        Assert.True(root.GetProperty("overflowBadgeMeasured").GetBoolean());
        Assert.False(root.GetProperty("pageLayoutRefactored").GetBoolean());
        Assert.False(root.GetProperty("gridImplemented").GetBoolean());
        Assert.False(root.GetProperty("editorImplemented").GetBoolean());
        Assert.False(root.GetProperty("notebookExecutionImplemented").GetBoolean());
        Assert.False(root.GetProperty("aurelianWorkPerformed").GetBoolean());
        Assert.False(root.GetProperty("vdMirWorkPerformed").GetBoolean());
    }

    [Fact]
    public void M17c_PreservesExpandableMarkdownCards()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(PresenterNavigationCatalog.CreateModel())
            .WithSelectedSection("oblivion")
            .WithSelectedTab("oblivion", "docs")
            .WithSelectedCard(OblivionWorkbenchCatalog.DocsPageId, ExpandedDocCardId)
            .WithCardViewState(OblivionWorkbenchCatalog.DocsPageId, ExpandedDocCardId, new OblivionCardViewState(true, 0));
        PresenterPageRenderResult page = RenderDocsPage(state);

        Assert.Contains(
            page.Frame.Resolved.Nodes.Keys,
            id => id.Value.Contains($"{ExpandedDocCardId}.expanded-body-viewport", StringComparison.Ordinal));
    }

    [Fact]
    public void M17c_PreservesIndependentScrollPanes()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(PresenterNavigationCatalog.CreateModel())
            .WithSelectedSection("oblivion")
            .WithSelectedTab("oblivion", "docs")
            .WithSelectedCard(OblivionWorkbenchCatalog.DocsPageId, ExpandedDocCardId)
            .WithCardViewState(OblivionWorkbenchCatalog.DocsPageId, ExpandedDocCardId, new OblivionCardViewState(true, 0))
            .WithScrollOffset(OblivionWorkbenchCatalog.DocsPageId, 120)
            .WithInspectorScrollOffset(OblivionWorkbenchCatalog.DocsPageId, 180);

        Assert.Equal(120, state.GetScrollOffset(OblivionWorkbenchCatalog.DocsPageId));
        Assert.Equal(180, state.GetInspectorScrollOffset(OblivionWorkbenchCatalog.DocsPageId));
    }

    [Fact]
    public void M17c_DoesNotRefactorPageLayout()
    {
        using JsonDocument manifest = LoadManifest();
        Assert.False(manifest.RootElement.GetProperty("pageLayoutRefactored").GetBoolean());
    }

    [Fact]
    public void M17c_DoesNotImplementGrid()
    {
        using JsonDocument manifest = LoadManifest();
        Assert.False(manifest.RootElement.GetProperty("gridImplemented").GetBoolean());
    }

    [Fact]
    public void M17c_DoesNotImplementMarkdownEditing()
    {
        using JsonDocument manifest = LoadManifest();
        Assert.False(manifest.RootElement.GetProperty("editorImplemented").GetBoolean());
    }

    [Fact]
    public void M17c_DoesNotImplementNotebookExecution()
    {
        using JsonDocument manifest = LoadManifest();
        Assert.False(manifest.RootElement.GetProperty("notebookExecutionImplemented").GetBoolean());
    }

    [Fact]
    public void M17c_DoesNotPerformAurelianWork()
    {
        using JsonDocument manifest = LoadManifest();
        Assert.False(manifest.RootElement.GetProperty("aurelianWorkPerformed").GetBoolean());
    }

    [Fact]
    public void M17c_DoesNotPerformVdMirWork()
    {
        using JsonDocument manifest = LoadManifest();
        Assert.False(manifest.RootElement.GetProperty("vdMirWorkPerformed").GetBoolean());
    }

    private static UiLoweringResult LowerCard(OblivionCompactCardView view, OblivionCardRenderOptions options)
    {
        return UiLowerer.Lower(OblivionCardRenderer.BuildCard(view, Theme, options));
    }

    private static ResolvedLayoutDocument ResolveCard(OblivionCompactCardView view, OblivionCardRenderOptions options)
    {
        UiLoweringResult lowered = LowerCard(view, options);
        LayoutDocument document = LayoutCompiler.CompileLayoutRows(lowered.Rows);
        return LayoutDocumentResolver.ResolveLayoutDocument(document, new Rect(0, 0, options.Width, options.Height));
    }

    private static PresenterPageRenderResult RenderDocsPage(PresenterNavigationState state)
    {
        int width = 1280;
        int height = 720;
        PresenterShellMode shellMode = PresenterShellModeResolver.Resolve(width);
        PresenterNavigationLayout layout = PresenterNavigationLayout.Create(width, height, shellMode);
        return PresenterNavigationCatalog.RenderPage(
            OblivionWorkbenchCatalog.DocsPageId,
            DemoState.Default,
            Theme,
            new PresenterProofOptions(),
            layout.ContentVisibleWidth,
            layout.ViewportHeight,
            state,
            shellMode);
    }

    private static Rect FindRectBySuffix(ResolvedLayoutDocument resolved, string suffix)
    {
        foreach ((NodeId nodeId, ResolvedLayoutNode node) in resolved.Nodes)
        {
            if (nodeId.Value.EndsWith(suffix, StringComparison.Ordinal))
            {
                return node.Rect;
            }
        }

        throw new KeyNotFoundException($"No resolved layout node ended with '{suffix}'.");
    }

    private static OblivionCompactCardView CreatePlainView(int actionCount = 2, int artifactCount = 2)
    {
        return new OblivionCompactCardView(
            "m17c-plain",
            "Plain stack card",
            "Readable subtitle",
            "plain-source.md",
            "Short summary",
            ["Note", "Passing"],
            ["alpha", "beta"],
            new OblivionCompactPlainBodyContent(
            [
                "First body line stays visible.",
                "Second body line reserves its own space.",
                "Third body line is available if height allows.",
            ]),
            Enumerable.Range(1, actionCount).Select(index => $"Action {index}").ToArray(),
            Enumerable.Range(1, artifactCount).Select(index => $"Artifact {index}").ToArray(),
            IsExpanded: false,
            BodyScrollOffset: 0,
            PreferredHeight: 260,
            ExpandedPreferredHeight: 260);
    }

    private static OblivionCompactCardView CreateMarkdownView(int actionCount = 2, int artifactCount = 2)
    {
        return new OblivionCompactCardView(
            "m17c-markdown",
            "Markdown stack card",
            "Readable subtitle",
            "markdown-source.md",
            "Collapsed markdown summary stays above the footer.",
            ["Note", "Markdown body"],
            ["alpha", "beta"],
            new OblivionCompactMarkdownBodyContent(
                new OblivionCardBody(
                    OblivionCardBodyFormat.CopelandMarkdown,
                    RawText: "# Heading",
                    BodySourcePath: "markdown-source.md",
                    PreviewLines: ["Heading"],
                    DocumentMir: null,
                    Diagnostics: [])),
            Enumerable.Range(1, actionCount).Select(index => $"Action {index}").ToArray(),
            Enumerable.Range(1, artifactCount).Select(index => $"Artifact {index}").ToArray(),
            IsExpanded: false,
            BodyScrollOffset: 0,
            PreferredHeight: 260,
            ExpandedPreferredHeight: 420);
    }

    private static JsonDocument LoadManifest()
    {
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepoRoot,
            "artifacts",
            "m17c",
            "oblivion-card-renderer-stack-refactor-manifest.json")));
    }

    private const string ExpandedDocCardId = "doc-aurelian-build-topology-m13b";

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
