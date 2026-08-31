using Copeland.TS.Compiler;
using Copeland.TS.Templates;
using Oblivion.Model;
using Oblivion.Persistence;
using Oblivion.Product;
using Xunit;

namespace Oblivion.App.Tests;

public sealed class DiagramCardTests
{
    [Fact]
    public void Structured_vault_loads_first_class_diagram_card()
    {
        OblivionWorkspaceLoadResult load = OblivionApplication.LoadVault(FixtureRoot);

        Assert.True(load.Succeeded, string.Join(Environment.NewLine, load.Diagnostics));
        OblivionCard diagram = Assert.Single(load.Workspace!.Pages.Single().Cards, card =>
            card.Kind == OblivionCardKind.Diagram);
        Assert.Equal("source/VehicleFlow.ts", diagram.Diagram!.Reference);
        Assert.Equal("VehicleFlow", diagram.Diagram.Symbol);
        Assert.Equal(OblivionDiagramProjectionKind.State, diagram.Diagram.Projection);
        Assert.Empty(diagram.Body.RawText);
    }

    [Fact]
    public void Compiler_semantics_project_guards_without_authored_mermaid()
    {
        OblivionCard card = LoadDiagramCard();

        OblivionDiagramProjectionResult result = new OblivionDiagramCardRealizer().Project(
            card,
            FixtureRoot);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.StartsWith("stateDiagram-v2", result.MermaidSource);
        Assert.Contains("Start [speed > 0]", result.MermaidSource);
        Assert.Contains("Impact [detected == true]", result.MermaidSource);
        Assert.NotNull(result.SemanticFingerprint);
    }

    [Fact]
    public void Missing_symbol_is_inspectable_projection_failure()
    {
        OblivionCard card = LoadDiagramCard() with
        {
            Diagram = LoadDiagramCard().Diagram! with { Symbol = "MissingFlow" },
        };

        OblivionDiagramProjectionResult result = new OblivionDiagramCardRealizer().Project(
            card,
            FixtureRoot);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "COPE-STATE-DIAGRAM-0001");
        Assert.Equal("MissingFlow", result.Source.Symbol);
    }

    [Fact]
    public void Missing_source_is_inspectable_projection_failure()
    {
        OblivionCard card = LoadDiagramCard() with
        {
            Diagram = LoadDiagramCard().Diagram! with { Reference = "source/Missing.ts" },
        };

        OblivionDiagramProjectionResult result = new OblivionDiagramCardRealizer().Project(
            card,
            FixtureRoot);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "OBLIVION-DIAGRAM-SOURCE-NOT-FOUND");
        Assert.Equal("source/Missing.ts", result.Source.Reference);
    }

    [Fact]
    public void Collapsed_has_no_body_and_expanded_selects_diagram_presenter()
    {
        OblivionCard card = LoadDiagramCard();
        OblivionDiagramProjectionResult projection = new OblivionDiagramCardRealizer().Project(card, FixtureRoot);
        OblivionDiagramPresentationSource presentation = new(
            projection.MermaidSource!,
            projection.Source.Reference,
            "state");

        OblivionContentPresentationPlan collapsed = OblivionContentPresenterSelector.Select(
            card,
            OblivionCardViewState.Collapsed,
            diagram: presentation);
        OblivionContentPresentationPlan expanded = OblivionContentPresenterSelector.Select(
            card,
            new OblivionCardViewState(true, 0),
            diagram: presentation);

        Assert.Empty(collapsed.Items);
        Assert.Equal("Diagram", collapsed.ContentTypeLabel);
        Assert.Equal(OblivionContentPresenterKind.ExternalMermaidRenderer, Assert.Single(expanded.Items).PresenterKind);
        Assert.True(expanded.AllowsInternalScroll);
    }

    [Fact]
    public void Realization_retains_semantic_and_renderer_provenance()
    {
        OblivionCard card = LoadDiagramCard();
        RecordingRenderer renderer = new();

        OblivionDiagramCardRealizationResult result = new OblivionDiagramCardRealizer().Realize(
            card,
            FixtureRoot,
            renderer,
            Path.GetTempPath());

        Assert.True(result.Projection.Succeeded);
        Assert.True(result.Render!.Succeeded);
        Assert.Equal("source/VehicleFlow.ts", result.Projection.Source.Reference);
        Assert.Equal("VehicleFlow", result.Projection.Source.Symbol);
        Assert.Equal("vehicle-flow-state.diagram", renderer.Request!.ContentId);
        Assert.Equal("source/VehicleFlow.ts", renderer.Request.SourceReference);
        Assert.Equal(OblivionResolvedAppearance.Light, renderer.Request.Appearance);
        Assert.Equal("vehicle-flow-state", result.Render.Provenance!.CardId);
    }

    [Fact]
    public void Card_show_exposes_source_projection_artifact_status_and_renderer()
    {
        OblivionControlResult<OblivionCardDetail> result = new OblivionWorkspaceControl().ShowCard(
            FixtureRoot,
            "vehicle-flow-state");

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal("diagram", result.Value!.Kind);
        Assert.Equal("CopelandFlow", result.Value.DiagramSourceKind);
        Assert.Equal("source/VehicleFlow.ts", result.Value.DiagramSourceReference);
        Assert.Equal("VehicleFlow", result.Value.DiagramSymbol);
        Assert.Equal("State", result.Value.DiagramProjection);
        Assert.NotNull(result.Value.DiagramSemanticFingerprint);
        Assert.NotNull(result.Value.DiagramDerivedArtifactStatus);
        Assert.Equal("mermaid-cli@11.16.0", result.Value.DiagramRenderer);
        Assert.NotNull(result.Value.DiagramCachedAppearances);
        Assert.NotNull(result.Value.DiagramRequestedAppearance);
    }

    [Fact]
    public void Diagram_card_content_is_explicitly_not_text()
    {
        OblivionControlResult<OblivionCardContentResult> result = new OblivionWorkspaceControl().GetCardContent(
            FixtureRoot,
            "vehicle-flow-state");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "OBLIVION-CARD-CONTENT-NOT-TEXT");
    }

    [Fact]
    public void Unsupported_projection_is_rejected_at_vault_boundary()
    {
        string toml = File.ReadAllText(Path.Combine(FixtureRoot, "cards", "vehicle-flow-state.toml"))
            .Replace("projection = \"state\"", "projection = \"sequence\"", StringComparison.Ordinal);

        OblivionCardTomlReadResult result = OblivionCardTomlReader.Read(toml, "diagram.card.toml");

        Assert.Null(result.Document);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "unsupported-diagram-projection");
    }

    [Fact]
    public void Render_failure_keeps_projection_and_source_inspectable()
    {
        OblivionCard card = LoadDiagramCard();
        OblivionDiagramCardRealizationResult result = new OblivionDiagramCardRealizer().Realize(
            card,
            FixtureRoot,
            new FailingRenderer(),
            Path.GetTempPath());

        Assert.True(result.Projection.Succeeded);
        Assert.Equal("source/VehicleFlow.ts", result.Projection.Source.Reference);
        Assert.False(result.Render!.Succeeded);
        Assert.Contains(result.Render.Diagnostics, diagnostic => diagnostic.Code == "TEST-RENDER-FAILURE");
    }

    [Fact]
    public void M20a_dense_realization_flow_is_deterministic_bounded_and_source_correlated()
    {
        OblivionWorkspaceLoadResult load = OblivionApplication.LoadVault(M20aFixtureRoot);
        Assert.True(load.Succeeded, string.Join(Environment.NewLine, load.Diagnostics));
        OblivionCard card = Assert.Single(load.Workspace!.Pages.Single().Cards);

        OblivionDiagramCardRealizer realizer = new();
        OblivionDiagramProjectionResult first = realizer.Project(card, M20aFixtureRoot);
        OblivionDiagramProjectionResult second = realizer.Project(card, M20aFixtureRoot);

        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics));
        Assert.Equal(first.MermaidSource, second.MermaidSource);
        Assert.Equal(first.SemanticFingerprint, second.SemanticFingerprint);

        string sourcePath = Path.Combine(
            M20aFixtureRoot,
            "source",
            "DiagramCardRealizationFlow.ts");
        CopelandCompilation compilation = CopelandCompiler.CompileTemplates(
            File.ReadAllText(sourcePath),
            new CopelandCompilationOptions
            {
                SourcePath = sourcePath,
                ProjectRoot = M20aFixtureRoot,
            });

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.True(StateMachineDiagramProjection.TryProject(
            compilation.BoundCompilation!.Program,
            "DiagramCardRealizationFlow",
            out StateMachineSemanticView? semanticView,
            out Diagram? diagram,
            out var diagnostics),
            string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(16, semanticView!.States.Count);
        Assert.Equal(31, semanticView.Transitions.Count);
        Assert.Equal(16, diagram!.Nodes.Count);
        Assert.Equal(31, diagram.Edges.Count);
        Assert.Equal(24, diagram.Edges.Count(edge => edge.Label!.Contains('[', StringComparison.Ordinal)));
        Assert.All(semanticView.States, state =>
        {
            Assert.Equal(sourcePath, state.Source.Path);
            Assert.True(state.Source.StartLine > 0);
        });
        Assert.All(semanticView.Transitions, transition =>
        {
            Assert.Equal(sourcePath, transition.Source.Path);
            Assert.True(transition.Source.StartLine > 0);
        });
    }

    private static string FixtureRoot => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "M19oDiagramCards.oblivion");

    private static string M20aFixtureRoot => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "M20aDenseDiagram.oblivion");

    private static OblivionCard LoadDiagramCard()
    {
        OblivionWorkspaceLoadResult load = OblivionApplication.LoadVault(FixtureRoot);
        return Assert.Single(load.Workspace!.Pages.Single().Cards, card => card.Kind == OblivionCardKind.Diagram);
    }

    private sealed class RecordingRenderer : IOblivionDiagramRenderer
    {
        public OblivionDiagramRenderRequest? Request { get; private set; }

        public OblivionDiagramRenderResult Render(OblivionDiagramRenderRequest request)
        {
            Request = request;
            string hash = OblivionMermaidHashing.ComputeSourceHash(request.Source);
            return new OblivionDiagramRenderResult(
                true,
                "fake-mermaid",
                "1.0",
                hash,
                Path.Combine(request.OutputDirectory, hash + ".png"),
                "image/png",
                [],
                Provenance: new OblivionDiagramProvenance(
                    "Mermaid",
                    hash,
                    "fake-mermaid",
                    "1.0",
                    "render",
                    "png",
                    request.Appearance,
                    OblivionMermaidRendererOptions.RenderingOptionsFor(request.Appearance),
                    "test",
                    request.WorkspaceId,
                    request.PageId,
                    request.CardId,
                    request.ContentId,
                    request.SourceReference,
                    true));
        }
    }

    private sealed class FailingRenderer : IOblivionDiagramRenderer
    {
        public OblivionDiagramRenderResult Render(OblivionDiagramRenderRequest request)
        {
            return new OblivionDiagramRenderResult(
                false,
                "fake-mermaid",
                "1.0",
                OblivionMermaidHashing.ComputeSourceHash(request.Source),
                null,
                null,
                [new OblivionCardDiagnostic(
                    "TEST-RENDER-FAILURE",
                    OblivionDiagnosticSeverity.Warning,
                    "Renderer failed without erasing semantic source.",
                    request.SourceReference)]);
        }
    }
}
