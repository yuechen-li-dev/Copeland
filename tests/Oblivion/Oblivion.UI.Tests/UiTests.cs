using Oblivion.Model;
using Oblivion.Product;
using Machina.Core.Actions;
using Machina.Layout.Geometry;
using Machina.Runtime.Input;
using Machina.Standard.Components;
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

    [Fact]
    public void Pointer_press_maps_to_typed_product_interaction()
    {
        OblivionPageInteractionMap map = new(
            "page",
            [
                new OblivionCardHitTarget(
                    "page",
                    "card",
                    new Rect(0, 0, 100, 80),
                    OblivionUiActions.ToggleCardExpansion("page", "card"),
                    new Rect(0, 0, 100, 20)),
            ],
            [],
            []);

        OblivionPageInteractionRoutingResult result = map.RouteInput(
            new UiPointerButtonChanged(
                new PointerPoint(10, 10),
                UiPointerButton.Primary,
                IsPressed: true,
                UiModifiers.None),
            pageScrollOffset: 0);

        Assert.True(result.Consumed);
        Assert.NotNull(result.Action);
        Assert.True(OblivionUiActions.TryDecode(result.Action!.Id, out OblivionInteraction? interaction));
        Assert.Equal(
            new OblivionInteraction.ToggleCardExpansion("page", "card"),
            interaction);
    }

    [Fact]
    public void Nested_scroll_region_takes_precedence_over_main_stack()
    {
        OblivionScrollRegionTarget main = CreateScrollRegion(
            OblivionScrollTargetKind.MainCardStack,
            cardId: null,
            bounds: new Rect(0, 0, 400, 400));
        OblivionScrollRegionTarget body = CreateScrollRegion(
            OblivionScrollTargetKind.ExpandedMarkdownBody,
            cardId: "card",
            bounds: new Rect(20, 20, 200, 200));
        OblivionPageInteractionMap map = new("page", [], [], [main, body]);

        OblivionPageInteractionRoutingResult result = map.RouteInput(
            new UiPointerWheel(
                new PointerPoint(40, 40),
                DeltaX: 0,
                DeltaY: -1,
                UiModifiers.None),
            pageScrollOffset: 0);

        Assert.True(OblivionUiActions.TryDecode(result.Action!.Id, out OblivionInteraction? interaction));
        OblivionInteraction.SetScrollOffset scroll = Assert.IsType<OblivionInteraction.SetScrollOffset>(interaction);
        Assert.Equal(OblivionScrollTargetKind.ExpandedMarkdownBody, scroll.Target.Kind);
        Assert.Equal("card", scroll.Target.CardId);
    }

    [Fact]
    public void Workspace_reconciliation_removes_stale_selection_and_card_state()
    {
        OblivionCard current = CreateCard("current");
        OblivionSessionState state = OblivionSessionState.Empty
            .WithSelectedCard("page", "removed")
            .WithCardViewState(
                "page",
                "removed",
                new OblivionCardViewState(IsExpanded: true, BodyScrollOffset: 50));

        OblivionSessionState reconciled = state.ReconcilePage("page", [current]);

        Assert.Equal("current", reconciled.GetSelectedCardId("page", [current]));
        Assert.Empty(reconciled.CardViewStateByPageId["page"]);
    }

    private static OblivionScrollRegionTarget CreateScrollRegion(
        OblivionScrollTargetKind kind,
        string? cardId,
        Rect bounds)
    {
        return new OblivionScrollRegionTarget(
            new OblivionScrollTarget(kind, "page", cardId),
            bounds,
            new ScrollbarGeometry(
                new Rect(bounds.X + bounds.Width - 8, bounds.Y, 8, bounds.Height),
                new Rect(bounds.X + bounds.Width - 8, bounds.Y, 8, 40),
                IsVisible: true,
                ScrollOffset: 0,
                MaxScrollOffset: 500),
            ContentHeight: bounds.Height + 500);
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
