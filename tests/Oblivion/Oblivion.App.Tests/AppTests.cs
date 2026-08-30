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
                card_kind = "status"
                status = "passing"
                title = "Evidence status"

                [body]
                format = "plain"
                text = "Evidence collected."
                """);
            File.WriteAllText(
                Path.Combine(rootPath, "body", "trial.md"),
                "# Trial\n\nbefore edit\n");
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
