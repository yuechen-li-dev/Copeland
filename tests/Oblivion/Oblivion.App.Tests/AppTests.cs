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
}
