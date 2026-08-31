using Oblivion.App;
using Oblivion.Model;
using Oblivion.Persistence;
using Oblivion.Product;
using System.Text.Json;
using Xunit;

namespace Oblivion.App.Tests;

public sealed class AppTests
{
    [Fact]
    public void Typed_action_routes_deferred_effect_and_updates_runtime_state()
    {
        OblivionCard card = new(
            new OblivionCardId("note"),
            OblivionCardKind.Note,
            OblivionCardStatus.Idle,
            "Note",
            null,
            [],
            OblivionMarkdownBody.CreateMarkdown("# Note", "note.md"),
            [new OblivionCardAction("refresh-markdown", "Refresh", true)],
            [],
            new OblivionProvenance(OblivionProvenanceSourceKind.WorkspaceAsset, "note.card.toml"));

        OblivionActionOutcome? outcome = new OblivionApplication().Invoke(card, "page", "refresh-markdown");

        Assert.NotNull(outcome);
        Assert.Equal(OblivionCardEffectKind.RefreshMarkdown, outcome.Request.Kind);
        Assert.Equal(OblivionCardEffectStatus.Deferred, outcome.Result.Status);
        Assert.Equal(outcome.Result, outcome.State.EffectState.GetLastResult(card.Id));
    }

    [Fact]
    public void Unknown_action_does_not_create_an_effect()
    {
        OblivionCard card = new(
            new OblivionCardId("status"),
            OblivionCardKind.Status,
            OblivionCardStatus.Passing,
            "Status",
            null,
            [],
            OblivionMarkdownBody.CreatePlain("Passing"),
            [],
            [],
            OblivionProvenance.Unknown);

        Assert.Null(new OblivionApplication().Invoke(card, "page", "missing"));
    }

    [Fact]
    public void App_assembly_does_not_reference_presenter()
    {
        string[] references = typeof(OblivionApplication).Assembly.GetReferencedAssemblies().Select(reference => reference.Name!).ToArray();
        Assert.DoesNotContain("Machina.Presenter.Sample", references);
    }

    [Fact]
    public void Product_action_id_survives_ui_adapter_as_typed_invocation()
    {
        OblivionCardActionInvocation invocation = new(
            new OblivionCardId("card"),
            new OblivionProductActionId("refresh-markdown"),
            "page",
            "card.toml");

        Machina.Core.Actions.UiActionId action = OblivionUiActions.InvokeProductAction(invocation);

        Assert.True(OblivionUiActions.TryDecode(action, out OblivionInteraction? interaction));
        OblivionInteraction.InvokeProductAction decoded =
            Assert.IsType<OblivionInteraction.InvokeProductAction>(interaction);
        Assert.Equal(invocation.ActionId, decoded.Invocation.ActionId);
        Assert.Equal(invocation.CardId, decoded.Invocation.CardId);
    }

    [Fact]
    public void Typed_request_uses_explicit_host_capability_and_typed_result()
    {
        OblivionCard card = CreateMarkdownCard();
        OblivionHostCapabilities capabilities = new(
            RefreshContent: request => new CompletedEffectResult(
                request.RequestId,
                request.CardId,
                request.Kind,
                "Refreshed.",
                [],
                []));
        OblivionApplication application = new(
            effects: new OblivionCardEffectRouter(capabilities));

        OblivionActionOutcome outcome = Assert.IsType<OblivionActionOutcome>(
            application.Invoke(
                card,
                "page",
                new OblivionProductActionId("refresh-markdown")));

        Assert.IsType<RefreshContentEffectRequest>(outcome.Request);
        Assert.IsType<CompletedEffectResult>(outcome.Result);
        Assert.Equal(OblivionCardEffectStatus.Completed, outcome.Result.Status);
    }

    [Fact]
    public void Unavailable_host_capability_is_observable()
    {
        OblivionActionOutcome outcome = Assert.IsType<OblivionActionOutcome>(
            new OblivionApplication().Invoke(
                CreateMarkdownCard(),
                "page",
                new OblivionProductActionId("refresh-markdown")));

        Assert.IsType<RefreshContentEffectRequest>(outcome.Request);
        Assert.Contains(
            outcome.Result.Diagnostics,
            diagnostic => diagnostic.Code == "OBLIVION-HOST-CAPABILITY-UNAVAILABLE");
    }

    [Fact]
    public void Effect_state_rejects_result_for_another_request()
    {
        OblivionEffectContext context = new(
            new OblivionProductActionId("refresh-markdown"),
            OblivionCardKind.Note,
            "page",
            WorkspaceId: null,
            SourcePath: null,
            Intent: "refresh");
        RefreshContentEffectRequest request = new("request-a", new OblivionCardId("card"), context);
        DeferredEffectResult result = new(
            "request-b",
            request.CardId,
            request.Kind,
            "Deferred.",
            [],
            []);

        Assert.Throws<InvalidOperationException>(
            () => OblivionEffectState.Empty.WithOutcome(request, result));
    }

    [Fact]
    public void Product_interaction_dispatcher_owns_selection_semantics()
    {
        IReadOnlyList<OblivionCard> cards = OblivionWorkbench.CreateCardsPageCards();
        OblivionCard card = Assert.Single(cards.Take(1));
        OblivionInteractionDispatchResult result = OblivionInteractionDispatcher.Dispatch(
            OblivionHostState.Empty,
            new OblivionInteraction.SelectCard(OblivionWorkbench.CardsPageId, card.Id.Value),
            new OblivionHostOptions(),
            new OblivionHostLayout(OblivionShellMode.Wide, 800, 600));

        Assert.True(result.Applied);
        Assert.Equal(
            card.Id.Value,
            result.State.Session.GetSelectedCardId(OblivionWorkbench.CardsPageId, cards));
    }

    [Fact]
    public void Product_surface_inspects_durable_state_in_declared_order()
    {
        using ProductWorkspaceFixture fixture = ProductWorkspaceFixture.Create();

        OblivionProductSurfaceResult<OblivionProductWorkspaceSnapshot> result =
            new OblivionProductSurface().Inspect(fixture.ManifestPath);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal("trial-workspace", result.Value!.Workspace.Id);
        Assert.Equal(["notes", "evidence"], result.Value.Workspace.Pages.Select(page => page.Id));
        Assert.Equal(["trial-note", "evidence-status"], result.Value.Cards.Select(card => card.Id));
        Assert.Equal("notes", result.Value.Session.SelectedPageId);
        Assert.Null(result.Value.Session.SelectedCardId);
        Assert.Equal("initial-session-defaults", result.Value.Session.Kind);
    }

    [Fact]
    public void Card_inspection_exposes_content_provenance_artifacts_and_runtime_actions()
    {
        using ProductWorkspaceFixture fixture = ProductWorkspaceFixture.Create();

        OblivionProductSurfaceResult<OblivionProductCardSnapshot> result =
            new OblivionProductSurface().ShowCard(fixture.ManifestPath, "trial-note");

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal("body/trial.md", result.Value!.Body.SourceReference);
        Assert.Equal("cards/trial.card.toml", result.Value.Provenance.SourceReference);
        Assert.Equal("markdown-reference", result.Value.Body.ContentKind);
        Assert.Contains(result.Value.AvailableActions, action =>
            action.Id == "refresh-markdown" &&
            action.EffectKind == "refreshMarkdown" &&
            action.SemanticallyInvokable);
        OblivionProductArtifactSnapshot artifact = Assert.Single(result.Value.Artifacts);
        Assert.Equal("trial-output", artifact.Id);
        Assert.Equal("artifacts/trial.txt", artifact.Reference);
        Assert.Equal("trial-workspace", artifact.Address.WorkspaceId);
        Assert.Equal("notes", artifact.Address.PageId);
        Assert.True(artifact.Exists);
        Assert.True(artifact.IsFile);
        Assert.Equal(".txt", artifact.Extension);
        Assert.Equal("text/plain", artifact.MediaType);
        Assert.Equal(new FileInfo(Path.Combine(fixture.RootPath, "artifacts", "trial.txt")).Length, artifact.ByteLength);
    }

    [Fact]
    public void Same_local_artifact_id_on_different_cards_has_distinct_resolved_addresses()
    {
        using ProductWorkspaceFixture fixture = ProductWorkspaceFixture.Create();

        OblivionProductSurfaceResult<IReadOnlyList<OblivionProductArtifactSnapshot>> result =
            new OblivionProductSurface().ListArtifacts(fixture.ManifestPath);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        OblivionProductArtifactSnapshot[] artifacts = result.Value!
            .Where(artifact => artifact.Id == "trial-output")
            .ToArray();
        Assert.Equal(2, artifacts.Length);
        Assert.Equal(2, artifacts.Select(artifact => artifact.Address).Distinct().Count());
        Assert.Equal(["evidence-status", "trial-note"], artifacts.Select(artifact => artifact.CardId).Order().ToArray());
    }

    [Fact]
    public void Artifact_show_rejects_unknown_owner_and_reports_missing_payload()
    {
        using ProductWorkspaceFixture fixture = ProductWorkspaceFixture.Create();
        OblivionProductSurface surface = new();

        OblivionProductSurfaceResult<OblivionProductArtifactSnapshot> missingOwner =
            surface.ShowArtifact(fixture.ManifestPath, "missing-card", "trial-output");
        File.Delete(Path.Combine(fixture.RootPath, "artifacts", "trial.txt"));
        OblivionProductSurfaceResult<OblivionProductArtifactSnapshot> missingPayload =
            surface.ShowArtifact(fixture.ManifestPath, "trial-note", "trial-output");

        Assert.False(missingOwner.Succeeded);
        Assert.Equal("OBLIVION-ARTIFACT-OWNER-NOT-FOUND", Assert.Single(missingOwner.Diagnostics).Code);
        Assert.True(missingPayload.Succeeded);
        Assert.False(missingPayload.Value!.Exists);
        Assert.Contains(missingPayload.Diagnostics, diagnostic => diagnostic.Code == "OBLIVION-ARTIFACT-NOT-FOUND");
    }

    [Fact]
    public void Resolver_rejects_traversal_and_absolute_artifact_references()
    {
        using ProductWorkspaceFixture fixture = ProductWorkspaceFixture.Create();
        OblivionWorkspaceLoadResult load = OblivionWorkspaceApplication.Load(fixture.ManifestPath, useCache: false);
        OblivionWorkspacePage page = load.Workspace!.Pages[0];
        OblivionCard card = page.Cards[0];
        OblivionArtifactResolver resolver = new();

        OblivionArtifactResolutionResult traversal = resolver.Resolve(
            load.Workspace,
            load.Location!,
            page,
            card,
            new OblivionCardArtifact("unsafe", "Unsafe", "text", "../outside.txt"));
        OblivionArtifactResolutionResult absolute = resolver.Resolve(
            load.Workspace,
            load.Location!,
            page,
            card,
            new OblivionCardArtifact("absolute", "Absolute", "text", Path.GetFullPath(fixture.BodyPath)));

        Assert.False(traversal.Succeeded);
        Assert.False(absolute.Succeeded);
        Assert.All(
            traversal.Diagnostics.Concat(absolute.Diagnostics),
            diagnostic => Assert.Equal("OBLIVION-ARTIFACT-PATH-UNSAFE", diagnostic.Code));
    }

    [Fact]
    public void Resolver_preserves_generated_card_provenance_and_identifies_directories()
    {
        using ProductWorkspaceFixture fixture = ProductWorkspaceFixture.Create();
        OblivionWorkspaceLoadResult load = OblivionWorkspaceApplication.Load(fixture.ManifestPath, useCache: false);
        OblivionWorkspacePage page = load.Workspace!.Pages[0];
        OblivionCard sourceCard = page.Cards[0];
        OblivionCard generatedCard = sourceCard with
        {
            Id = new OblivionCardId("generated-card"),
            Provenance = new OblivionProvenance(
                OblivionProvenanceSourceKind.Generated,
                "generation/input.md",
                "generate-report",
                new OblivionArtifactId("parent"),
                new OblivionCardId("parent-card")),
        };

        OblivionArtifactResolutionResult result = new OblivionArtifactResolver().Resolve(
            load.Workspace,
            load.Location!,
            page,
            generatedCard,
            new OblivionCardArtifact("directory", "Artifact directory", "directory", "artifacts", true));

        Assert.True(result.Succeeded);
        Assert.True(result.Artifact!.Exists);
        Assert.True(result.Artifact.IsDirectory);
        Assert.False(result.Artifact.IsFile);
        Assert.Null(result.Artifact.ByteLength);
        Assert.Equal(OblivionProvenanceSourceKind.Generated, result.Artifact.Provenance.SourceKind);
        Assert.Equal("generate-report", result.Artifact.Provenance.ProducerActionId);
        Assert.Equal("parent", result.Artifact.Provenance.ParentArtifactId!.Value);
        Assert.Equal("parent-card", result.Artifact.Provenance.ParentCardId!.Value);
    }

    [Fact]
    public void Typed_local_host_capabilities_receive_resolved_source_and_artifact_requests()
    {
        using ProductWorkspaceFixture fixture = ProductWorkspaceFixture.Create();
        List<OblivionOpenPathCapabilityRequest> opened = [];
        List<OblivionCopyTextCapabilityRequest> copied = [];
        OblivionLocalHostCapabilities host = new(
            OpenPath: request =>
            {
                opened.Add(request);
                return new OblivionHostCapabilityResult(true, "Opened by fake host.");
            },
            CopyText: request =>
            {
                copied.Add(request);
                return new OblivionHostCapabilityResult(true, "Copied by fake host.");
            });
        OblivionProductSurface surface = new(localHost: host);

        OblivionProductInvocationSnapshot openSource = surface
            .Invoke(fixture.ManifestPath, "trial-note", "open-source").Value!;
        OblivionProductInvocationSnapshot copySource = surface
            .Invoke(fixture.ManifestPath, "trial-note", "copy-source-path").Value!;
        OblivionProductInvocationSnapshot openArtifact = surface
            .Invoke(fixture.ManifestPath, "evidence-status", "open-artifact", "trial-output").Value!;

        Assert.Equal("completed", openSource.Status);
        Assert.Equal("completed", copySource.Status);
        Assert.Equal("completed", openArtifact.Status);
        Assert.Equal(2, opened.Count);
        Assert.Equal(OblivionHostPathTargetKind.Source, opened[0].TargetKind);
        Assert.Equal(Path.GetFullPath(fixture.BodyPath), opened[0].ResolvedPath);
        Assert.Equal(OblivionHostPathTargetKind.Artifact, opened[1].TargetKind);
        Assert.Equal("trial-output", opened[1].ArtifactAddress!.ArtifactId.Value);
        Assert.Equal(Path.GetFullPath(fixture.BodyPath), copied.Single().Text);
        Assert.Equal("resolved-source-path", copied.Single().SemanticKind);
    }

    [Fact]
    public void Headless_open_source_returns_correlated_capability_diagnostic()
    {
        using ProductWorkspaceFixture fixture = ProductWorkspaceFixture.Create();

        OblivionProductSurfaceResult<OblivionProductInvocationSnapshot> result =
            new OblivionProductSurface().Invoke(fixture.ManifestPath, "trial-note", "open-source");

        Assert.True(result.Succeeded);
        Assert.Equal("deferred", result.Value!.Status);
        OblivionProductDiagnostic diagnostic = Assert.Single(
            result.Diagnostics,
            candidate => candidate.Code == "OBLIVION-HOST-CAPABILITY-UNAVAILABLE");
        Assert.Equal("trial-workspace", diagnostic.WorkspaceId);
        Assert.Equal("notes", diagnostic.PageId);
        Assert.Equal("trial-note", diagnostic.CardId);
        Assert.Equal("open-source", diagnostic.ActionId);
        Assert.Equal("openSource", diagnostic.EffectKind);
    }

    [Fact]
    public void Open_artifact_rejects_ambiguous_or_missing_target_before_calling_host()
    {
        using ProductWorkspaceFixture fixture = ProductWorkspaceFixture.Create();
        int openCount = 0;
        OblivionLocalHostCapabilities host = new(
            OpenPath: _ =>
            {
                openCount++;
                return new OblivionHostCapabilityResult(true, "Opened.");
            });
        OblivionProductSurface surface = new(localHost: host);

        OblivionProductSurfaceResult<OblivionProductInvocationSnapshot> ambiguous =
            surface.Invoke(fixture.ManifestPath, "evidence-status", "open-artifact");
        OblivionProductSurfaceResult<OblivionProductInvocationSnapshot> missing =
            surface.Invoke(fixture.ManifestPath, "evidence-status", "open-artifact", "missing");

        Assert.False(ambiguous.Succeeded);
        Assert.Equal("rejected", ambiguous.Value!.Status);
        Assert.Contains(ambiguous.Diagnostics, diagnostic => diagnostic.Code == "OBLIVION-ARTIFACT-ID-AMBIGUOUS");
        Assert.False(missing.Succeeded);
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Code == "OBLIVION-ARTIFACT-NOT-FOUND");
        Assert.Equal(0, openCount);
    }

    [Fact]
    public void Artifact_show_command_emits_stable_machine_readable_address_and_metadata()
    {
        using ProductWorkspaceFixture fixture = ProductWorkspaceFixture.Create();
        StringWriter first = new();
        StringWriter second = new();
        string[] arguments =
        [
            "artifact",
            "show",
            "trial-note",
            "trial-output",
            "--workspace",
            fixture.ManifestPath,
            "--json",
        ];

        Assert.Equal(0, new OblivionCommandLine(first, TextWriter.Null).Run(arguments));
        Assert.Equal(0, new OblivionCommandLine(second, TextWriter.Null).Run(arguments));
        Assert.Equal(first.ToString(), second.ToString());
        using JsonDocument json = JsonDocument.Parse(first.ToString());
        Assert.Equal("trial-workspace", json.RootElement.GetProperty("address").GetProperty("workspaceId").GetString());
        Assert.Equal("trial-output", json.RootElement.GetProperty("address").GetProperty("artifactId").GetString());
        Assert.True(json.RootElement.GetProperty("exists").GetBoolean());
        Assert.Equal("text/plain", json.RootElement.GetProperty("mediaType").GetString());
    }

    [Fact]
    public void Semantic_refresh_reloads_a_code_first_source_edit()
    {
        using ProductWorkspaceFixture fixture = ProductWorkspaceFixture.Create();
        OblivionProductSurface surface = new();
        Assert.Contains("before edit", surface.ShowCard(fixture.ManifestPath, "trial-note").Value!.Body.Text);

        File.WriteAllText(fixture.BodyPath, "# Trial\n\nafter edit\n");
        OblivionProductSurfaceResult<OblivionProductInvocationSnapshot> invocation =
            surface.Invoke(fixture.ManifestPath, "trial-note", "refresh-markdown");
        OblivionProductSurfaceResult<OblivionProductCardSnapshot> after =
            surface.ShowCard(fixture.ManifestPath, "trial-note");

        Assert.True(invocation.Succeeded, string.Join(Environment.NewLine, invocation.Diagnostics));
        Assert.Equal("completed", invocation.Value!.Status);
        Assert.Equal("refreshMarkdown", invocation.Value.EffectKind);
        Assert.Contains("after edit", after.Value!.Body.Text);
    }

    [Fact]
    public void Invalid_semantic_action_returns_located_recovery_diagnostic()
    {
        using ProductWorkspaceFixture fixture = ProductWorkspaceFixture.Create();

        OblivionProductSurfaceResult<OblivionProductInvocationSnapshot> result =
            new OblivionProductSurface().Invoke(fixture.ManifestPath, "trial-note", "missing-action");

        Assert.False(result.Succeeded);
        OblivionProductDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("OBLIVION-ACTION-NOT-FOUND", diagnostic.Code);
        Assert.Equal("trial-workspace", diagnostic.WorkspaceId);
        Assert.Equal("notes", diagnostic.PageId);
        Assert.Equal("trial-note", diagnostic.CardId);
        Assert.Equal("missing-action", diagnostic.ActionId);
        Assert.Contains("Use 'actions trial-note'", diagnostic.Message);
    }

    [Fact]
    public void Command_line_json_is_deterministic_and_machine_readable()
    {
        using ProductWorkspaceFixture fixture = ProductWorkspaceFixture.Create();
        StringWriter firstOutput = new();
        StringWriter firstError = new();
        StringWriter secondOutput = new();
        string[] arguments = ["inspect", "--workspace", fixture.ManifestPath, "--json"];

        int firstExit = new OblivionCommandLine(firstOutput, firstError).Run(arguments);
        int secondExit = new OblivionCommandLine(secondOutput, TextWriter.Null).Run(arguments);

        Assert.Equal(0, firstExit);
        Assert.Equal(0, secondExit);
        Assert.Equal(firstOutput.ToString(), secondOutput.ToString());
        Assert.Empty(firstError.ToString());
        using JsonDocument json = JsonDocument.Parse(firstOutput.ToString());
        Assert.Equal(
            "oblivion.product.v1",
            json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(
            "trial-workspace",
            json.RootElement.GetProperty("workspace").GetProperty("id").GetString());
    }

    [Fact]
    public void Derived_docs_cards_report_projection_provenance()
    {
        string manifestPath = OblivionWorkspacePaths.ResolveWorkspaceManifestPath();

        OblivionProductSurfaceResult<OblivionProductCardSnapshot> result =
            new OblivionProductSurface().ShowCard(
                manifestPath,
                "doc-copeland-markdown-frontend-m12a");

        Assert.NotNull(result.Value);
        Assert.Equal("generated", result.Value.Provenance.SourceKind);
        Assert.Equal(
            OblivionDocsDogfoodCatalog.ProjectionActionId,
            result.Value.Provenance.ProducerActionId);
        Assert.Equal(
            "docs/Copeland/history/copeland-markdown-frontend-m12a.md",
            result.Value.Provenance.SourceReference);
    }

    [Fact]
    public void Mermaid_realization_uses_injected_renderer_and_retains_source_provenance()
    {
        OblivionCard card = CreateMarkdownCard() with
        {
            Body = OblivionMarkdownBody.CreateMarkdown(
                "```mermaid\ngraph TD\n  Source --> Derived\n```",
                "body/architecture.md"),
        };
        OblivionContentPresentationPlan plan = OblivionContentPresenterSelector.Select(
            card,
            new OblivionCardViewState(true, 0));
        RecordingDiagramRenderer renderer = new();

        IReadOnlyList<OblivionDiagramRenderResult> results = OblivionContentRealization.RenderMermaid(
            plan,
            renderer,
            Path.GetTempPath());

        Assert.True(Assert.Single(results).Succeeded);
        Assert.Equal("body/architecture.md", Assert.Single(renderer.Requests).SourceReference);
        Assert.Contains("Source --> Derived", Assert.Single(renderer.Requests).Source);
    }

    [Fact]
    public void Headless_card_inspection_exposes_mermaid_source_hash_renderer_and_cache_status()
    {
        string manifestPath = Path.GetFullPath(
            Path.Combine(
                FindRepositoryRoot(),
                "artifacts",
                "m19c",
                "trial-workspace",
                "workspace.oblivion.json"));

        OblivionProductSurfaceResult<OblivionProductCardSnapshot> result =
            new OblivionProductSurface().ShowCard(manifestPath, "m19c-architecture");

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        OblivionProductDiagramSnapshot diagram = Assert.Single(result.Value!.Diagrams);
        Assert.Contains("Agent-authored semantic content", diagram.Source);
        Assert.Equal(64, diagram.SourceHash.Length);
        Assert.Equal("mermaid-cli", diagram.RendererId);
        Assert.Equal("11.16.0", diagram.RendererVersion);
        Assert.NotEmpty(diagram.RendererStatus);
        Assert.Equal(64, diagram.CacheKey.Length);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Copeland.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Copeland repository root was not found.");
    }

    [Fact]
    public void Unavailable_mermaid_renderer_is_a_bounded_diagnostic_not_a_load_failure()
    {
        OblivionDiagramRenderResult result = new OblivionExternalMermaidRenderer(
            executablePath: null).Render(new OblivionDiagramRenderRequest(
                "card.diagram",
                "graph TD\n  A --> B",
                "body/diagram.md",
                Path.GetTempPath()));

        Assert.False(result.Succeeded);
        Assert.Null(result.RenderedPath);
        Assert.Equal("mermaid-cli", result.Renderer);
        Assert.Equal(
            "OBLIVION-MERMAID-RENDERER-UNAVAILABLE",
            Assert.Single(result.Diagnostics).Code);
        Assert.NotEmpty(result.SourceHash);
    }

    [Fact]
    public void Mermaid_source_hash_is_sha256_over_utf8_with_canonical_newlines()
    {
        string windowsHash = OblivionMermaidHashing.ComputeSourceHash("graph TD\r\n  A --> B\r\n");
        string unixHash = OblivionMermaidHashing.ComputeSourceHash("graph TD\n  A --> B\n");
        string changedHash = OblivionMermaidHashing.ComputeSourceHash("graph TD\n  A --> C\n");

        Assert.Equal(unixHash, windowsHash);
        Assert.Equal(64, unixHash.Length);
        Assert.NotEqual(unixHash, changedHash);
    }

    [Fact]
    public void Mermaid_cache_key_changes_only_for_contract_inputs()
    {
        MermaidDerivedArtifactKey baseline = new(
            "source",
            "mermaid-cli",
            "11.16.0",
            "png",
            "strict");

        Assert.Equal(baseline.Value, (baseline with { }).Value);
        Assert.NotEqual(baseline.Value, (baseline with { SourceHash = "changed" }).Value);
        Assert.NotEqual(baseline.Value, (baseline with { RendererVersion = "11.17.0" }).Value);
        Assert.NotEqual(baseline.Value, (baseline with { OutputFormat = "svg" }).Value);
        Assert.NotEqual(baseline.Value, (baseline with { RenderingOptions = "loose" }).Value);
    }

    [Fact]
    public void Qualified_renderer_cold_renders_then_reuses_valid_cache()
    {
        using MermaidRendererFixture fixture = MermaidRendererFixture.Create();

        OblivionDiagramRenderResult first = fixture.Renderer.Render(fixture.Request);
        OblivionDiagramRenderResult second = fixture.Renderer.Render(fixture.Request);

        Assert.True(
            first.Succeeded,
            string.Join(Environment.NewLine, first.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.False(first.CacheHit);
        Assert.True(second.Succeeded);
        Assert.True(second.CacheHit);
        Assert.Equal(first.CacheKey, second.CacheKey);
        Assert.Equal(first.RenderedPath, second.RenderedPath);
        Assert.Equal(1, fixture.ProcessRunner.RenderInvocations);
        Assert.Equal(1, fixture.ProcessRunner.VersionInvocations);
        Assert.True(File.Exists(first.RenderedPath));
    }

    [Fact]
    public void Source_and_renderer_version_changes_invalidate_mermaid_cache()
    {
        using MermaidRendererFixture fixture = MermaidRendererFixture.Create();
        OblivionDiagramRenderResult first = fixture.Renderer.Render(fixture.Request);
        OblivionDiagramRenderResult sourceChanged = fixture.Renderer.Render(
            fixture.Request with { Source = "graph TD\n  Durable --> Different" });
        FakeProcessRunner upgradedRunner = new("11.17.0");
        OblivionExternalMermaidRenderer upgradedRenderer = fixture.CreateRenderer(
            upgradedRunner,
            expectedVersion: "11.17.0");
        OblivionDiagramRenderResult versionChanged = upgradedRenderer.Render(fixture.Request);

        Assert.NotEqual(first.CacheKey, sourceChanged.CacheKey);
        Assert.NotEqual(first.CacheKey, versionChanged.CacheKey);
        Assert.Equal(2, fixture.ProcessRunner.RenderInvocations);
        Assert.Equal(1, upgradedRunner.RenderInvocations);
    }

    [Fact]
    public void Invalid_mermaid_cache_metadata_is_diagnosed_and_rebuilt()
    {
        using MermaidRendererFixture fixture = MermaidRendererFixture.Create();
        OblivionDiagramRenderResult first = fixture.Renderer.Render(fixture.Request);
        string metadataPath = Path.ChangeExtension(first.RenderedPath!, ".json");
        File.WriteAllText(metadataPath, "{ invalid json");

        OblivionDiagramRenderResult rebuilt = fixture.Renderer.Render(fixture.Request);

        Assert.True(rebuilt.Succeeded);
        Assert.False(rebuilt.CacheHit);
        Assert.Contains(
            rebuilt.Diagnostics,
            diagnostic => diagnostic.Code == "OBLIVION-MERMAID-CACHE-INVALID");
        Assert.Equal(2, fixture.ProcessRunner.RenderInvocations);
    }

    [Fact]
    public void Missing_cached_mermaid_artifact_is_diagnosed_and_rebuilt()
    {
        using MermaidRendererFixture fixture = MermaidRendererFixture.Create();
        OblivionDiagramRenderResult first = fixture.Renderer.Render(fixture.Request);
        File.Delete(first.RenderedPath!);

        OblivionDiagramRenderResult rebuilt = fixture.Renderer.Render(fixture.Request);

        Assert.True(rebuilt.Succeeded);
        Assert.False(rebuilt.CacheHit);
        Assert.Contains(
            rebuilt.Diagnostics,
            diagnostic => diagnostic.Code == "OBLIVION-MERMAID-CACHE-INVALID");
        Assert.Equal(2, fixture.ProcessRunner.RenderInvocations);
    }

    [Fact]
    public void Mermaid_provenance_retains_source_renderer_owner_and_derived_status()
    {
        using MermaidRendererFixture fixture = MermaidRendererFixture.Create();

        OblivionDiagramRenderResult result = fixture.Renderer.Render(fixture.Request);

        Assert.Equal(result.SourceHash, result.Provenance!.SourceHash);
        Assert.Equal("Mermaid", result.Provenance.SourceKind);
        Assert.Equal("mermaid-cli", result.Provenance.RendererId);
        Assert.Equal("11.16.0", result.Provenance.RendererVersion);
        Assert.Equal("workspace", result.Provenance.WorkspaceId);
        Assert.Equal("page", result.Provenance.PageId);
        Assert.Equal("card", result.Provenance.CardId);
        Assert.True(result.Provenance.Derived);
    }

    [Theory]
    [InlineData(FakeProcessBehavior.WrongVersion, "OBLIVION-MERMAID-RENDERER-VERSION-MISMATCH")]
    [InlineData(FakeProcessBehavior.Timeout, "OBLIVION-MERMAID-RENDER-TIMEOUT")]
    [InlineData(FakeProcessBehavior.NonzeroExit, "OBLIVION-MERMAID-RENDER-FAILED")]
    [InlineData(FakeProcessBehavior.MissingOutput, "OBLIVION-MERMAID-OUTPUT-MISSING")]
    [InlineData(FakeProcessBehavior.MalformedOutput, "OBLIVION-MERMAID-OUTPUT-INVALID")]
    public void Mermaid_renderer_failures_are_specific_and_retain_source(
        FakeProcessBehavior behavior,
        string expectedCode)
    {
        using MermaidRendererFixture fixture = MermaidRendererFixture.Create(behavior);

        OblivionDiagramRenderResult result = fixture.Renderer.Render(fixture.Request);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, Assert.Single(result.Diagnostics).Code);
        Assert.Equal(OblivionMermaidHashing.ComputeSourceHash(fixture.Request.Source), result.SourceHash);
        Assert.NotNull(result.Provenance);
    }

    private static OblivionCard CreateMarkdownCard()
    {
        return new OblivionCard(
            new OblivionCardId("note"),
            OblivionCardKind.Note,
            OblivionCardStatus.Idle,
            "Note",
            null,
            [],
            OblivionMarkdownBody.CreateMarkdown("# Note", "note.md"),
            [new OblivionCardAction("refresh-markdown", "Refresh", true)],
            [],
            new OblivionProvenance(
                OblivionProvenanceSourceKind.WorkspaceAsset,
                "note.card.toml"));
    }

    private sealed class RecordingDiagramRenderer : IOblivionDiagramRenderer
    {
        public List<OblivionDiagramRenderRequest> Requests { get; } = [];

        public OblivionDiagramRenderResult Render(OblivionDiagramRenderRequest request)
        {
            Requests.Add(request);
            return new OblivionDiagramRenderResult(
                Succeeded: true,
                Renderer: "fake-mermaid",
                RendererVersion: "test",
                SourceHash: "stable-test-hash",
                RenderedPath: Path.Combine(request.OutputDirectory, request.ContentId + ".svg"),
                MediaType: "image/svg+xml",
                Diagnostics: []);
        }
    }

    public enum FakeProcessBehavior
    {
        Success,
        WrongVersion,
        Timeout,
        NonzeroExit,
        MissingOutput,
        MalformedOutput,
    }

    private sealed class FakeProcessRunner : IOblivionProcessRunner
    {
        private static readonly byte[] MinimalPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

        private readonly string _version;
        private readonly FakeProcessBehavior _behavior;

        public FakeProcessRunner(
            string version = OblivionMermaidRendererOptions.PinnedVersion,
            FakeProcessBehavior behavior = FakeProcessBehavior.Success)
        {
            _version = version;
            _behavior = behavior;
        }

        public int VersionInvocations { get; private set; }
        public int RenderInvocations { get; private set; }

        public OblivionProcessResult Run(OblivionProcessRequest request)
        {
            if (request.Arguments.Contains("--version"))
            {
                VersionInvocations++;
                string version = _behavior == FakeProcessBehavior.WrongVersion ? "0.0.0" : _version;
                return new OblivionProcessResult(true, false, 0, version, string.Empty);
            }

            RenderInvocations++;
            if (_behavior == FakeProcessBehavior.Timeout)
            {
                return new OblivionProcessResult(true, true, null, string.Empty, string.Empty);
            }

            if (_behavior == FakeProcessBehavior.NonzeroExit)
            {
                return new OblivionProcessResult(true, false, 7, string.Empty, "bounded fake failure");
            }

            int outputArgumentIndex = request.Arguments.ToList().IndexOf("--output");
            string outputPath = request.Arguments[outputArgumentIndex + 1];
            if (_behavior == FakeProcessBehavior.MissingOutput)
            {
                return new OblivionProcessResult(true, false, 0, string.Empty, string.Empty);
            }

            File.WriteAllBytes(
                outputPath,
                _behavior == FakeProcessBehavior.MalformedOutput ? [1, 2, 3, 4] : MinimalPng);
            return new OblivionProcessResult(true, false, 0, string.Empty, string.Empty);
        }
    }

    private sealed class MermaidRendererFixture : IDisposable
    {
        private MermaidRendererFixture(
            string rootPath,
            FakeProcessRunner processRunner,
            OblivionExternalMermaidRenderer renderer)
        {
            RootPath = rootPath;
            ProcessRunner = processRunner;
            Renderer = renderer;
        }

        public string RootPath { get; }
        public FakeProcessRunner ProcessRunner { get; }
        public OblivionExternalMermaidRenderer Renderer { get; }
        public OblivionDiagramRenderRequest Request => new(
            "card.diagram",
            "graph TD\r\n  Durable --> Derived\r\n",
            "body/architecture.md",
            RootPath,
            "workspace",
            "page",
            "card");

        public static MermaidRendererFixture Create(
            FakeProcessBehavior behavior = FakeProcessBehavior.Success)
        {
            string rootPath = Path.Combine(
                Path.GetTempPath(),
                "oblivion-m19e-mermaid-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            FakeProcessRunner processRunner = new(behavior: behavior);
            OblivionExternalMermaidRenderer renderer = CreateRendererCore(
                processRunner,
                OblivionMermaidRendererOptions.PinnedVersion);
            return new MermaidRendererFixture(rootPath, processRunner, renderer);
        }

        public OblivionExternalMermaidRenderer CreateRenderer(
            FakeProcessRunner runner,
            string expectedVersion)
        {
            return CreateRendererCore(runner, expectedVersion);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }

        private static OblivionExternalMermaidRenderer CreateRendererCore(
            FakeProcessRunner runner,
            string expectedVersion)
        {
            return new OblivionExternalMermaidRenderer(
                new OblivionMermaidRendererOptions(
                    typeof(AppTests).Assembly.Location,
                    null,
                    expectedVersion,
                    TimeSpan.FromMilliseconds(100),
                    "test fake"),
                runner);
        }
    }

    private sealed class ProductWorkspaceFixture : IDisposable
    {
        private ProductWorkspaceFixture(string rootPath)
        {
            RootPath = rootPath;
        }

        public string RootPath { get; }
        public string ManifestPath => Path.Combine(RootPath, "workspace.oblivion.json");
        public string BodyPath => Path.Combine(RootPath, "body", "trial.md");

        public static ProductWorkspaceFixture Create()
        {
            string rootPath = Path.Combine(
                Path.GetTempPath(),
                "oblivion-m19a-product-surface-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(rootPath, "cards"));
            Directory.CreateDirectory(Path.Combine(rootPath, "body"));
            Directory.CreateDirectory(Path.Combine(rootPath, "artifacts"));

            File.WriteAllText(
                Path.Combine(rootPath, "workspace.oblivion.json"),
                """
                {
                  "format": 1,
                  "kind": "oblivion-workspace",
                  "workspaceId": "trial-workspace",
                  "title": "M19a Trial Workspace",
                  "defaultPageId": "notes",
                  "sections": [
                    {
                      "id": "trial",
                      "title": "Trial",
                      "pages": [
                        {
                          "id": "notes",
                          "title": "Notes",
                          "cards": ["cards/trial.card.toml"]
                        },
                        {
                          "id": "evidence",
                          "title": "Evidence",
                          "cards": ["cards/evidence.card.toml"]
                        }
                      ]
                    }
                  ]
                }
                """);
            File.WriteAllText(
                Path.Combine(rootPath, "cards", "trial.card.toml"),
                """
                format = 1
                kind = "card"
                id = "trial-note"
                card_kind = "note"
                status = "passing"
                title = "Trial note"
                tags = ["m19a", "code-first"]

                [body]
                format = "copeland-markdown"
                path = "body/trial.md"

                [[artifacts]]
                id = "trial-output"
                label = "Trial output"
                kind = "text"
                path = "artifacts/trial.txt"
                generated = false
                """);
            File.WriteAllText(
                Path.Combine(rootPath, "cards", "evidence.card.toml"),
                """
                format = 1
                kind = "card"
                id = "evidence-status"
                card_kind = "artifact"
                status = "passing"
                title = "Evidence status"

                [body]
                format = "plain"
                text = "Evidence collected."

                [[artifacts]]
                id = "trial-output"
                label = "Evidence output"
                kind = "text"
                path = "artifacts/evidence.txt"
                generated = true

                [[artifacts]]
                id = "second-output"
                label = "Second output"
                kind = "text"
                path = "artifacts/second.txt"
                generated = false
                """);
            File.WriteAllText(
                Path.Combine(rootPath, "body", "trial.md"),
                "# Trial\n\nbefore edit\n");
            File.WriteAllText(Path.Combine(rootPath, "artifacts", "trial.txt"), "trial artifact\n");
            File.WriteAllText(Path.Combine(rootPath, "artifacts", "evidence.txt"), "generated evidence\n");
            File.WriteAllText(Path.Combine(rootPath, "artifacts", "second.txt"), "second artifact\n");
            return new ProductWorkspaceFixture(rootPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
