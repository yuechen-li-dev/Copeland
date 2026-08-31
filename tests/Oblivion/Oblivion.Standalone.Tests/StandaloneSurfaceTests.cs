using System.Reflection;
using Oblivion.Model;
using Oblivion.Persistence;
using Oblivion.Presentation;
using Oblivion.Product;
using Xunit;

namespace Oblivion.Standalone.Tests;

public sealed class StandaloneSurfaceTests
{
    [Fact]
    public void Standalone_page_materializes_one_card_in_the_normal_stream_stack()
    {
        MaterializedPresentation presentation = M19gSingleCardReading.Materialize();

        Assert.Single(presentation.Page.Cards);
        PresentationMaterializedBand band = Assert.Single(presentation.Bands);
        Assert.Equal(PresentationMaterializedBandKind.Stream, band.Kind);
        Assert.Single(band.CardIds);
    }

    [Fact]
    public void Collapsed_state_does_not_mount_or_allocate_a_body_surface()
    {
        OblivionStandaloneSurface surface = new();

        OblivionStandaloneSurfaceSnapshot snapshot = surface.CreateSnapshot(2560, 1440);

        Assert.False(snapshot.CardView.IsExpanded);
        Assert.Equal(OblivionReadingState.Collapsed, snapshot.ContentPlan.ReadingState);
        Assert.False(snapshot.MatureContentMounted);
        Assert.Null(snapshot.MatureContentBounds);
    }

    [Fact]
    public void Expanded_state_selects_the_mature_Avalonia_document_presenter()
    {
        OblivionStandaloneSurface surface = new();
        surface.ToggleExpansion();

        OblivionStandaloneSurfaceSnapshot snapshot = surface.CreateSnapshot(2560, 1440);

        Assert.True(snapshot.MatureContentMounted);
        Assert.Equal(OblivionReadingState.Expanded, snapshot.ContentPlan.ReadingState);
        OblivionContentPresentationItem item = Assert.Single(snapshot.ContentPlan.Items);
        Assert.Equal(OblivionContentPresenterKind.AvaloniaReadOnlyDocument, item.PresenterKind);
        Assert.Equal(OblivionContentFocusContract.PresenterOwnsSelectionAndCopy, item.FocusContract);
    }

    [Fact]
    public void Expand_action_changes_session_state_and_collapse_restores_it()
    {
        OblivionStandaloneSurface surface = new();

        surface.ToggleExpansion();

        Assert.True(surface.Session.GetCardViewState(surface.PageId, surface.Card.Id.Value).IsExpanded);

        surface.Collapse();

        Assert.False(surface.Session.GetCardViewState(surface.PageId, surface.Card.Id.Value).IsExpanded);
    }

    [Fact]
    public void Square_affordance_keeps_its_location_across_reading_states()
    {
        OblivionStandaloneSurface surface = new();
        OblivionStandaloneSurfaceSnapshot collapsed = surface.CreateSnapshot(2560, 1440);
        surface.ToggleExpansion();
        OblivionStandaloneSurfaceSnapshot expanded = surface.CreateSnapshot(2560, 1440);

        Assert.Equal(collapsed.ExpansionAffordanceBounds, expanded.ExpansionAffordanceBounds);
        Assert.True(collapsed.ExpansionAffordanceBounds.Width >= 36);
        Assert.True(collapsed.ExpansionAffordanceBounds.Height >= 36);
    }

    [Fact]
    public void Full_viewport_card_uses_wide_outer_margins_without_narrowing_the_application()
    {
        OblivionStandaloneSurface surface = new();

        OblivionStandaloneSurfaceSnapshot snapshot = surface.CreateSnapshot(2560, 1440);

        Assert.Equal(OblivionStandaloneRenderer.OuterHorizontalMargin, snapshot.CardBounds.X);
        Assert.Equal(
            2560 - (OblivionStandaloneRenderer.OuterHorizontalMargin * 2),
            snapshot.CardBounds.Width);
        Assert.True(snapshot.CardBounds.Width > 2200);

        OblivionStandaloneSurfaceSnapshot resized = surface.CreateSnapshot(1920, 1080);
        Assert.Equal(
            1920 - (OblivionStandaloneRenderer.OuterHorizontalMargin * 2),
            resized.CardBounds.Width);
    }

    [Fact]
    public void Product_semantic_assemblies_remain_Avalonia_free()
    {
        AssertAvaloniaFree(typeof(OblivionCard).Assembly);
        AssertAvaloniaFree(typeof(OblivionWorkspaceLoader).Assembly);
        AssertAvaloniaFree(typeof(PresentationMaterializer).Assembly);
    }

    private static void AssertAvaloniaFree(Assembly assembly)
    {
        Assert.DoesNotContain(
            assembly.GetReferencedAssemblies(),
            reference => reference.Name?.StartsWith("Avalonia", StringComparison.Ordinal) == true);
    }
}
