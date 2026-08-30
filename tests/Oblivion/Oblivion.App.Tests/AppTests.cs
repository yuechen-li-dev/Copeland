using Oblivion.App;
using Oblivion.Model;
using Oblivion.Product;
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
}
