using System.Text.Json;
using Machina.Core.Styling;
using Machina.Dominatus.Rendering.Commands;
using Machina.Layout.Geometry;
using Machina.Presenter.Sample;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class OblivionExpandableMarkdownCardsM15cTests
{
    private static readonly PresenterNavigationModel Model = PresenterNavigationCatalog.CreateModel();
    private static readonly PresenterProofOptions ProofOptions = new();

    [Fact]
    public void OblivionCardExpansion_DefaultsCollapsed()
    {
        PresenterNavigationState state = CreateDocsState();

        Assert.False(state.GetCardViewState(OblivionWorkbenchCatalog.DocsPageId, ExpandedDocCardId).IsExpanded);
    }

    [Fact]
    public void OblivionCardExpansion_ToggleExpandsCard()
    {
        PresenterNavigationState next = Dispatch(
            CreateDocsState(),
            PresenterNavigationActions.ToggleOblivionCardExpansion(OblivionWorkbenchCatalog.DocsPageId, ExpandedDocCardId));

        Assert.True(next.GetCardViewState(OblivionWorkbenchCatalog.DocsPageId, ExpandedDocCardId).IsExpanded);
    }

    [Fact]
    public void OblivionCardExpansion_ToggleCollapsesCard()
    {
        PresenterNavigationState state = CreateDocsState(expandedCardId: ExpandedDocCardId);

        PresenterNavigationState next = Dispatch(
            state,
            PresenterNavigationActions.ToggleOblivionCardExpansion(OblivionWorkbenchCatalog.DocsPageId, ExpandedDocCardId));

        Assert.False(next.GetCardViewState(OblivionWorkbenchCatalog.DocsPageId, ExpandedDocCardId).IsExpanded);
    }

    [Fact]
    public void OblivionCardExpansion_IsSeparateFromSelection()
    {
        PresenterNavigationState state = CreateDocsState(selectedCardId: "doc-copeland-markdown-frontend-m12a")
            .WithCardViewState(OblivionWorkbenchCatalog.DocsPageId, ExpandedDocCardId, new OblivionCardViewState(true, 0));

        Assert.Equal("doc-copeland-markdown-frontend-m12a", state.GetSelectedCardId(OblivionWorkbenchCatalog.DocsPageId, GetDocsCards()));
        Assert.True(state.GetCardViewState(OblivionWorkbenchCatalog.DocsPageId, ExpandedDocCardId).IsExpanded);
    }

    [Fact]
    public void OblivionCardExpansion_PreservesSelectedCard()
    {
        PresenterNavigationState state = CreateDocsState(selectedCardId: ExpandedDocCardId, expandedCardId: ExpandedDocCardId);

        PresenterNavigationState next = Dispatch(
            state,
            PresenterNavigationActions.CollapseOblivionCard(OblivionWorkbenchCatalog.DocsPageId, ExpandedDocCardId));

        Assert.Equal(ExpandedDocCardId, next.GetSelectedCardId(OblivionWorkbenchCatalog.DocsPageId, GetDocsCards()));
        Assert.False(next.GetCardViewState(OblivionWorkbenchCatalog.DocsPageId, ExpandedDocCardId).IsExpanded);
    }

    [Fact]
    public void OblivionExpandedMarkdownCard_RendersBodyInline()
    {
        PresenterPageRenderResult page = RenderDocsPage(expandedCardId: ExpandedDocCardId);

        Assert.Contains(
            page.Frame.RenderCommands.OfType<DrawTextCommand>(),
            command => command.Id.Contains($"{ExpandedDocCardId}.expanded.block-", StringComparison.Ordinal));
    }

    [Fact]
    public void OblivionExpandedMarkdownCard_RendersHeadingParagraphList()
    {
        PresenterPageRenderResult page = RenderDocsPage(expandedCardId: ExpandedDocCardId);
        DrawTextCommand[] commands = page.Frame.RenderCommands.OfType<DrawTextCommand>().ToArray();

        Assert.Contains(commands, command => command.Id.Contains($"{ExpandedDocCardId}.expanded.block-", StringComparison.Ordinal));
        Assert.True(
            commands.Any(command => command.Id.Contains(".heading", StringComparison.Ordinal)) ||
            commands.Any(command => command.Id.Contains(".paragraph", StringComparison.Ordinal)) ||
            commands.Any(command => command.Id.Contains(".item-0.marker", StringComparison.Ordinal)));
    }

    [Fact]
    public void OblivionExpandedMarkdownCard_WrapsBodyText()
    {
        PresenterPageRenderResult page = RenderDocsPage(expandedCardId: ExpandedDocCardId);

        Assert.True(
            page.Frame.RenderCommands
                .OfType<DrawTextCommand>()
                .Count(command => command.Id.Contains($"{ExpandedDocCardId}.expanded.block-", StringComparison.Ordinal)) > 8);
    }

    [Fact]
    public void OblivionExpandedMarkdownCard_DoesNotOverflowCardBounds()
    {
        PresenterPageRenderResult page = RenderDocsPage(expandedCardId: ExpandedDocCardId);
        OblivionCompactCardView view = GetExpandedBuiltCard().CompactView;
        OblivionExpandedBodyViewport? viewport = OblivionCardRenderer.DescribeExpandedBodyViewport(page.Frame.Resolved, view, ExpandedDocCardId);
        Assert.NotNull(viewport);
        DrawTextCommand[] commands = page.Frame.RenderCommands
            .OfType<DrawTextCommand>()
            .Where(command => command.Id.Contains($"{ExpandedDocCardId}.expanded.block-", StringComparison.Ordinal))
            .ToArray();
        PushClipCommand[] clips = page.Frame.RenderCommands
            .OfType<PushClipCommand>()
            .Where(command => command.Id.Contains($"{ExpandedDocCardId}.expanded-body-viewport", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(commands);
        Assert.NotEmpty(clips);
        Assert.Contains(clips, clip => clip.Rect == viewport!.Bounds);
    }

    [Fact]
    public void OblivionExpandedMarkdownCard_UsesReadableContrast()
    {
        DrawTextCommand[] commands = RenderDocsPage(expandedCardId: ExpandedDocCardId).Frame.RenderCommands
            .OfType<DrawTextCommand>()
            .Where(command => command.Id.Contains($"{ExpandedDocCardId}.expanded.block-", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(commands);
        Assert.DoesNotContain(commands, command => command.Style.Color == ColorToken.Hex(0x09111DFF));
        Assert.DoesNotContain(commands, command => command.Style.Color == ColorToken.Hex(0x000000FF));
    }

    [Fact]
    public void OblivionExpandedMarkdownCard_LongBodyShowsLocalScrollbar()
    {
        PresenterNavigationShellRenderResult render = RenderShell(CreateDocsState(expandedCardId: ExpandedDocCardId), 1280, 720);
        OblivionCardBodyHitTarget bodyTarget = Assert.Single(
            render.PageRender!.OblivionInteraction!.BodyTargets,
            target => string.Equals(target.CardId, ExpandedDocCardId, StringComparison.Ordinal));

        Assert.True(bodyTarget.ScrollbarGeometry.IsVisible);
        Assert.True(bodyTarget.ScrollbarGeometry.MaxScrollOffset > 0);
    }

    [Fact]
    public void OblivionExpandedMarkdownCard_ShortBodyDoesNotShowLocalScrollbar()
    {
        PresenterNavigationShellRenderResult render = RenderExecutionRoadmapShell(expandedCardId: "visionary-future");
        OblivionCardBodyHitTarget bodyTarget = Assert.Single(
            render.PageRender!.OblivionInteraction!.BodyTargets,
            target => string.Equals(target.CardId, "visionary-future", StringComparison.Ordinal));

        Assert.False(bodyTarget.ScrollbarGeometry.IsVisible);
        Assert.Equal(0, bodyTarget.ScrollbarGeometry.MaxScrollOffset);
    }

    [Fact]
    public void OblivionExpandedMarkdownCard_BodyScrollOffsetClamps()
    {
        PresenterNavigationState next = Dispatch(
            CreateDocsState(expandedCardId: ExpandedDocCardId),
            PresenterNavigationActions.SetOblivionCardBodyScrollOffset(OblivionWorkbenchCatalog.DocsPageId, ExpandedDocCardId, 100000));

        Assert.InRange(next.GetCardViewState(OblivionWorkbenchCatalog.DocsPageId, ExpandedDocCardId).BodyScrollOffset, 0, 100000);
        Assert.True(next.GetCardViewState(OblivionWorkbenchCatalog.DocsPageId, ExpandedDocCardId).BodyScrollOffset < 100000);
    }

    [Fact]
    public void OblivionExpandedMarkdownCard_BodyWheelScrollsLocalBody()
    {
        PresenterNavigationState state = CreateDocsState(expandedCardId: ExpandedDocCardId);
        PresenterNavigationShellRenderResult render = RenderShell(state, 1280, 720);
        OblivionCardBodyHitTarget bodyTarget = Assert.Single(
            render.PageRender!.OblivionInteraction!.BodyTargets,
            target => string.Equals(target.CardId, ExpandedDocCardId, StringComparison.Ordinal));

        OblivionPageInteractionRoutingResult routed = render.PageRender.OblivionInteraction!.RouteInput(
            Wheel(
                new PresenterInputPoint(
                    (float)(bodyTarget.Bounds.X + (bodyTarget.Bounds.Width / 2)),
                    (float)(bodyTarget.Bounds.Y + (bodyTarget.Bounds.Height / 2))),
                -1),
            render.ScrollbarGeometry.ScrollOffset);

        Assert.True(routed.Consumed);
        Assert.NotNull(routed.Action);
        Assert.True(PresenterNavigationActions.TryParseSetOblivionCardBodyScrollOffset(
            routed.Action!.Id,
            out string pageId,
            out string cardId,
            out double scrollOffset));
        Assert.Equal(OblivionWorkbenchCatalog.DocsPageId, pageId);
        Assert.Equal(ExpandedDocCardId, cardId);
        Assert.True(scrollOffset > 0);
    }

    [Fact]
    public void OblivionCollapsedMarkdownCard_RendersTitleSourceTags()
    {
        string text = PageText(RenderDocsPage());

        Assert.Contains("Aurelian Build Topology", text, StringComparison.Ordinal);
        Assert.Contains("aurelian-build-topology-m13b.md", text, StringComparison.Ordinal);
        Assert.Contains("Selected card inspector", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionCollapsedMarkdownCard_DoesNotRenderFullBody()
    {
        PresenterPageRenderResult page = RenderDocsPage();

        Assert.DoesNotContain(
            page.Frame.RenderCommands.OfType<DrawTextCommand>(),
            command => command.Id.Contains($"{ExpandedDocCardId}.expanded.block-", StringComparison.Ordinal));
    }

    [Fact]
    public void OblivionCollapsedMarkdownCard_RemainsScannable()
    {
        OblivionBuiltCard collapsed = GetBuiltDocCard(expanded: false);
        OblivionBuiltCard expanded = GetBuiltDocCard(expanded: true);

        Assert.True(collapsed.CompactView.PreferredHeight < expanded.CompactView.ExpandedPreferredHeight);
    }

    [Fact]
    public void OblivionInspector_StillRendersMetadataAfterExpansion()
    {
        string text = PageText(RenderDocsPage(expandedCardId: ExpandedDocCardId));

        Assert.Contains("Metadata", text, StringComparison.Ordinal);
        Assert.Contains("Body source path:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionInspector_StillRendersActionsAfterExpansion()
    {
        string text = PageText(RenderDocsPage(expandedCardId: ExpandedDocCardId));

        Assert.Contains("Available actions", text, StringComparison.Ordinal);
        Assert.Contains("Refresh markdown", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionInspector_NoLongerRequiredAsPrimaryBodySurface()
    {
        PresenterPageRenderResult page = RenderDocsPage(expandedCardId: ExpandedDocCardId);
        string text = PageText(page);

        Assert.Contains("Selected card inspector", text, StringComparison.Ordinal);
        Assert.Contains(
            page.Frame.RenderCommands.OfType<DrawTextCommand>(),
            command => command.Id.Contains($"{ExpandedDocCardId}.expanded.block-", StringComparison.Ordinal));
    }

    [Fact]
    public void M15cManifest_RecordsExpandableCards()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            (string jsonPath, string textPath) = OblivionWorkbenchCatalog.WriteExpandableMarkdownCardsManifest(outputDirectory, CreateDocsState(expandedCardId: ExpandedDocCardId));
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(jsonPath));

            Assert.True(File.Exists(textPath));
            Assert.True(document.RootElement.GetProperty("expandableCardsImplemented").GetBoolean());
            Assert.True(document.RootElement.GetProperty("selectionExpansionSeparated").GetBoolean());
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void M15cManifest_RecordsMarkdownBodyInlineInStack()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            (string jsonPath, _) = OblivionWorkbenchCatalog.WriteExpandableMarkdownCardsManifest(outputDirectory, CreateDocsState(expandedCardId: ExpandedDocCardId));
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(jsonPath));

            Assert.True(document.RootElement.GetProperty("markdownBodyInlineInStack").GetBoolean());
            Assert.False(document.RootElement.GetProperty("inspectorPrimaryBodySurface").GetBoolean());
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void M15cExportArtifacts_AreWritten()
    {
        string[] expectedArtifacts =
        [
            "artifacts/m15c/m15c-oblivion-docs-collapsed-1280x720.png",
            "artifacts/m15c/m15c-oblivion-docs-expanded-1280x720.png",
            "artifacts/m15c/m15c-oblivion-docs-expanded-scrolled-1280x720.png",
            "artifacts/m15c/m15c-oblivion-cards-expanded-1280x720.png",
            "artifacts/m15c/m15c-oblivion-docs-compact-expanded-960x540.png",
            "artifacts/m15c/m15c-oblivion-inspector-after-expand-1280x720.png",
            "artifacts/m15c/oblivion-expandable-markdown-cards-manifest.json",
            "artifacts/m15c/oblivion-expandable-markdown-cards-manifest.txt",
        ];

        Assert.All(
            expectedArtifacts,
            relativePath => Assert.True(File.Exists(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))));
    }

    [Fact]
    public void M15c_DoesNotImplementMarkdownEditing()
    {
        using JsonDocument manifest = LoadManifest();
        Assert.False(manifest.RootElement.GetProperty("markdownEditingImplemented").GetBoolean());
    }

    [Fact]
    public void M15c_DoesNotImplementNotebookExecution()
    {
        using JsonDocument manifest = LoadManifest();
        Assert.False(manifest.RootElement.GetProperty("notebookExecutionImplemented").GetBoolean());
        Assert.False(manifest.RootElement.GetProperty("roslynExecutionImplemented").GetBoolean());
    }

    [Fact]
    public void M15c_DoesNotPerformAurelianWork()
    {
        using JsonDocument manifest = LoadManifest();
        Assert.False(manifest.RootElement.GetProperty("aurelianWorkPerformed").GetBoolean());
    }

    [Fact]
    public void M15c_DoesNotPerformVdMirWork()
    {
        using JsonDocument manifest = LoadManifest();
        Assert.False(manifest.RootElement.GetProperty("vdMirWorkPerformed").GetBoolean());
        Assert.False(manifest.RootElement.GetProperty("arbitrary2DLayoutSolverImplemented").GetBoolean());
    }

    private static IReadOnlyList<OblivionCard> GetDocsCards()
    {
        return OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.DocsPageId);
    }

    private static OblivionBuiltCard GetBuiltDocCard(bool expanded)
    {
        PresenterNavigationState state = CreateDocsState(expandedCardId: expanded ? ExpandedDocCardId : null);
        return Assert.Single(
            OblivionWorkbenchCatalog.GetBuiltPageCardsForSelection(
                OblivionWorkbenchCatalog.DocsPageId,
                ProofOptions,
                state.EffectState,
                state),
            card => string.Equals(card.SourceCard.Id.Value, ExpandedDocCardId, StringComparison.Ordinal));
    }

    private static OblivionBuiltCard GetExpandedBuiltCard()
    {
        return GetBuiltDocCard(expanded: true);
    }

    private static PresenterNavigationState CreateDocsState(
        string? selectedCardId = null,
        string? expandedCardId = null,
        double bodyScrollOffset = 0)
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("oblivion")
            .WithSelectedTab("oblivion", "docs");

        if (!string.IsNullOrWhiteSpace(selectedCardId))
        {
            state = state.WithSelectedCard(OblivionWorkbenchCatalog.DocsPageId, selectedCardId);
        }

        if (!string.IsNullOrWhiteSpace(expandedCardId))
        {
            state = state
                .WithSelectedCard(OblivionWorkbenchCatalog.DocsPageId, expandedCardId)
                .WithCardViewState(
                    OblivionWorkbenchCatalog.DocsPageId,
                    expandedCardId,
                    new OblivionCardViewState(true, bodyScrollOffset));
        }

        return state;
    }

    private static PresenterNavigationState CreateExecutionRoadmapState(string? expandedCardId = null)
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("oblivion")
            .WithSelectedTab("oblivion", "execution-roadmap");

        if (!string.IsNullOrWhiteSpace(expandedCardId))
        {
            state = state
                .WithSelectedCard(OblivionWorkbenchCatalog.ExecutionRoadmapPageId, expandedCardId)
                .WithCardViewState(
                    OblivionWorkbenchCatalog.ExecutionRoadmapPageId,
                    expandedCardId,
                    new OblivionCardViewState(true, 0));
        }

        return state;
    }

    private static PresenterPageRenderResult RenderDocsPage(string? expandedCardId = null, double bodyScrollOffset = 0)
    {
        PresenterNavigationState state = CreateDocsState(
            selectedCardId: expandedCardId ?? ExpandedDocCardId,
            expandedCardId: expandedCardId,
            bodyScrollOffset: bodyScrollOffset);
        int width = 1280;
        PresenterShellMode shellMode = PresenterShellModeResolver.Resolve(width);
        PresenterNavigationLayout layout = PresenterNavigationLayout.Create(width, 720, shellMode);
        return PresenterNavigationCatalog.RenderPage(
            OblivionWorkbenchCatalog.DocsPageId,
            DemoState.Default,
            StandardTheme.Default,
            ProofOptions,
            layout.ContentVisibleWidth,
            state,
            shellMode);
    }

    private static PresenterNavigationShellRenderResult RenderExecutionRoadmapShell(string? expandedCardId = null)
    {
        return RenderShell(CreateExecutionRoadmapState(expandedCardId), 1280, 720);
    }

    private static PresenterNavigationShellRenderResult RenderShell(PresenterNavigationState state, int width, int height)
    {
        PresenterShellMode shellMode = PresenterShellModeResolver.Resolve(width);
        PresenterNavigationLayout layout = PresenterNavigationLayout.Create(width, height, shellMode);
        return PresenterNavigationShellRenderer.Render(
            DemoState.Default,
            state,
            StandardTheme.Default,
            ProofOptions,
            layout);
    }

    private static PresenterNavigationState Dispatch(PresenterNavigationState state, Machina.Core.Actions.UiActionId actionId)
    {
        PresenterNavigationLayout layout = PresenterNavigationLayout.Create(1280, 720, PresenterShellMode.Wide);
        return PresenterNavigationDispatch.Dispatch(state, actionId, Model, ProofOptions, layout);
    }

    private static PresenterInputEvent Wheel(PresenterInputPoint point, float deltaY)
    {
        return new PresenterInputEvent(
            PresenterInputKind.Wheel,
            point,
            PresenterInputButton.None,
            deltaY,
            "Test");
    }

    private static string PageText(PresenterPageRenderResult page)
    {
        return string.Join(
            Environment.NewLine,
            page.Frame.RenderCommands.OfType<DrawTextCommand>().Select(command => command.Text));
    }

    private static void AssertRectInside(Rect inner, Rect outer)
    {
        Assert.True(inner.X >= outer.X);
        Assert.True(inner.Y >= outer.Y);
        Assert.True(inner.X + inner.Width <= outer.X + outer.Width);
        Assert.True(inner.Y + inner.Height <= outer.Y + outer.Height);
    }

    private static JsonDocument LoadManifest()
    {
        return JsonDocument.Parse(
            File.ReadAllText(Path.Combine(
                RepoRoot,
                "artifacts",
                "m15c",
                "oblivion-expandable-markdown-cards-manifest.json")));
    }

    private static string CreateOutputDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "machina-presenter-m15c-tests", Guid.NewGuid().ToString("N"));
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private const string ExpandedDocCardId = "doc-aurelian-build-topology-m13b";

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
