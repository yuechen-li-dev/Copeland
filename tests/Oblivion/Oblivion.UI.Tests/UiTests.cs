using Oblivion.Model;
using Oblivion.Product;
using Xunit;

namespace Oblivion.UI.Tests;

public sealed class UiTests
{
    [Fact]
    public void Session_state_keeps_product_panes_independent()
    {
        OblivionSessionState state = OblivionSessionState.Empty
            .WithMainScrollOffset("page", 120)
            .WithInspectorScrollOffset("page", 40)
            .WithRawSourceScrollOffset("card", 12)
            .WithCardBodyScrollOffset("page", "card", 88);

        Assert.Equal(120, state.GetMainScrollOffset("page"));
        Assert.Equal(40, state.GetInspectorScrollOffset("page"));
        Assert.Equal(12, state.GetRawSourceScrollOffset("card"));
        Assert.Equal(88, state.GetCardViewState("page", "card").BodyScrollOffset);
    }

    [Fact]
    public void Selection_and_exclusive_expansion_are_product_state()
    {
        OblivionCard first = CreateCard("first");
        OblivionCard second = CreateCard("second");
        OblivionSessionState state = OblivionSessionState.Empty
            .WithSelectedCard("page", second.Id.Value)
            .ExpandCardExclusively("page", second.Id.Value, [first.Id.Value, second.Id.Value]);

        Assert.Equal("second", state.GetSelectedCardId("page", [first, second]));
        Assert.False(state.GetCardViewState("page", "first").IsExpanded);
        Assert.True(state.GetCardViewState("page", "second").IsExpanded);
    }

    [Fact]
    public void Markdown_projection_is_derived_and_cached()
    {
        OblivionMarkdownBody.ClearProjectionCache();
        OblivionCardBody body = OblivionMarkdownBody.CreateMarkdown("# Heading\n\nBody", "body.md");
        OblivionMarkdownProjection first = OblivionMarkdownBody.Project(body);
        OblivionMarkdownProjection second = OblivionMarkdownBody.Project(body);

        Assert.Same(first, second);
        Assert.NotNull(first.Document);
        Assert.Contains(first.Preview, line => line.Contains("Heading", StringComparison.Ordinal));
        Assert.Equal("body.md", first.SourceReference);
    }

    [Fact]
    public void Ui_assembly_does_not_reference_presenter_or_aurelian()
    {
        string[] references = typeof(OblivionSessionState).Assembly.GetReferencedAssemblies().Select(reference => reference.Name!).ToArray();
        Assert.DoesNotContain("Machina.Presenter.Sample", references);
        Assert.DoesNotContain(references, name => name.StartsWith("Aurelian.", StringComparison.Ordinal));
    }

    private static OblivionCard CreateCard(string id)
    {
        return new OblivionCard(
            new OblivionCardId(id),
            OblivionCardKind.Note,
            OblivionCardStatus.Idle,
            id,
            null,
            [],
            OblivionMarkdownBody.CreatePlain(id),
            [],
            [],
            OblivionProvenance.Unknown);
    }
}
