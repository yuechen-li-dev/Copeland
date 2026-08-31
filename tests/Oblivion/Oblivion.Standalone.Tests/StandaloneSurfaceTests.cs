using System.Reflection;
using Oblivion.Model;
using Oblivion.Persistence;
using Oblivion.Presentation;
using Oblivion.Product;
using Xunit;

namespace Oblivion.Standalone.Tests;

public sealed class StandaloneSurfaceTests
{
    private static readonly OblivionStandaloneStyle Style = OblivionStandaloneStyles.M19h;

    [Fact]
    public void Standalone_enters_the_real_structured_workspace_session()
    {
        OblivionStandaloneSurface surface = new();

        Assert.Equal(M19iStructuredVault.WorkspaceId, surface.Workspace.Id.Value);
        Assert.Equal(M19iStructuredVault.PageId, surface.PageId);
        Assert.Equal(surface.Cards[0].Id.Value, surface.SelectedCardId);
        Assert.Collection(
            surface.Cards,
            card => Assert.Equal(M19iStructuredVault.FirstCardId, card.Id.Value),
            card => Assert.Equal(M19iStructuredVault.SecondCardId, card.Id.Value));
        Assert.All(surface.Cards, card => Assert.False(surface.IsExpanded(card)));
    }

    [Fact]
    public void Explicit_session_reload_observes_content_and_metadata_edits()
    {
        string vaultRoot = Path.Combine(
            Path.GetTempPath(),
            "oblivion-m19i-session-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            foreach (string sourcePath in Directory.GetFiles(
                M19iStructuredVault.DefaultRoot,
                "*",
                SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(M19iStructuredVault.DefaultRoot, sourcePath);
                string destinationPath = Path.Combine(vaultRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath);
            }

            OblivionStandaloneSurface before = new(vaultRoot);
            string markdownPath = Path.Combine(vaultRoot, "content", "notebook-stack.md");
            string cardPath = Path.Combine(vaultRoot, "cards", "notebook-stack.toml");
            File.AppendAllText(markdownPath, Environment.NewLine + "Session reload marker." + Environment.NewLine);
            File.WriteAllText(
                cardPath,
                File.ReadAllText(cardPath).Replace(
                    "From one card to a notebook stack",
                    "Reloaded session title",
                    StringComparison.Ordinal));

            OblivionStandaloneSurface after = new(vaultRoot);

            Assert.DoesNotContain("Session reload marker.", before.Cards[1].Body.RawText, StringComparison.Ordinal);
            Assert.Contains("Session reload marker.", after.Cards[1].Body.RawText, StringComparison.Ordinal);
            Assert.Equal("From one card to a notebook stack", before.Cards[1].Title);
            Assert.Equal("Reloaded session title", after.Cards[1].Title);
            Assert.Equal(after.Cards[0].Id.Value, after.SelectedCardId);
            Assert.All(after.Cards, card => Assert.False(after.IsExpanded(card)));
        }
        finally
        {
            if (Directory.Exists(vaultRoot))
            {
                Directory.Delete(vaultRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Standalone_page_loads_exactly_two_markdown_cards_in_stable_order()
    {
        OblivionStandaloneSurface surface = new();

        Assert.Equal(2, surface.Cards.Count);
        Assert.Collection(
            surface.Cards,
            card => Assert.Equal(M19iStructuredVault.FirstCardId, card.Id.Value),
            card => Assert.Equal(M19iStructuredVault.SecondCardId, card.Id.Value));
        Assert.All(
            surface.Cards,
            card => Assert.Equal(OblivionCardBodyFormat.CopelandMarkdown, card.Body.Format));
    }

    [Fact]
    public void M19g_single_card_materialization_remains_available_as_a_regression_contract()
    {
        MaterializedPresentation presentation = M19gSingleCardReading.Materialize();

        Assert.Single(presentation.Page.Cards);
    }

    [Fact]
    public void Fresh_launch_is_deterministically_collapsed_and_mounts_no_bodies()
    {
        OblivionStandaloneSurface surface = new();

        OblivionStandaloneSurfaceSnapshot snapshot = surface.CreateSnapshot(2560, 1440);

        Assert.Equal(surface.Cards[0].Id.Value, surface.SelectedCardId);
        Assert.All(snapshot.Cards, card =>
        {
            Assert.False(card.CardView.IsExpanded);
            Assert.Equal(OblivionReadingState.Collapsed, card.ContentPlan.ReadingState);
            Assert.False(card.MatureContentMounted);
            Assert.Null(card.MatureContentBounds);
        });
    }

    [Fact]
    public void Multi_expand_keeps_each_card_state_independent_and_uses_the_mature_presenter()
    {
        OblivionStandaloneSurface surface = new();
        string firstId = surface.Cards[0].Id.Value;
        string secondId = surface.Cards[1].Id.Value;

        surface.ToggleExpansion(firstId);
        Assert.True(surface.IsExpanded(surface.Cards[0]));
        Assert.False(surface.IsExpanded(surface.Cards[1]));

        surface.ToggleExpansion(secondId);
        OblivionStandaloneSurfaceSnapshot bothExpanded = surface.CreateSnapshot(2560, 1440);

        Assert.All(bothExpanded.Cards, card =>
        {
            Assert.True(card.CardView.IsExpanded);
            Assert.True(card.MatureContentMounted);
            Assert.Equal(OblivionReadingState.Expanded, card.ContentPlan.ReadingState);
            OblivionContentPresentationItem item = Assert.Single(card.ContentPlan.Items);
            Assert.Equal(OblivionContentPresenterKind.AvaloniaReadOnlyDocument, item.PresenterKind);
            Assert.Equal(
                OblivionContentFocusContract.PresenterOwnsSelectionAndCopy,
                item.FocusContract);
        });

        surface.Collapse(firstId);
        Assert.False(surface.IsExpanded(surface.Cards[0]));
        Assert.True(surface.IsExpanded(surface.Cards[1]));
    }

    [Fact]
    public void Expanding_first_card_pushes_second_card_and_collapse_restores_its_position()
    {
        OblivionStandaloneSurface surface = new();
        OblivionStandaloneSurfaceSnapshot collapsed = surface.CreateSnapshot(2560, 1440);

        surface.ToggleExpansion(surface.Cards[0].Id.Value);
        OblivionStandaloneSurfaceSnapshot expanded = surface.CreateSnapshot(2560, 1440);

        Assert.Equal(
            Style.ExpandedCardHeight - Style.CollapsedCardHeight,
            expanded.Cards[1].CardBounds.Y - collapsed.Cards[1].CardBounds.Y);
        AssertStackGeometry(expanded);

        surface.Collapse(surface.Cards[0].Id.Value);
        OblivionStandaloneSurfaceSnapshot restored = surface.CreateSnapshot(2560, 1440);

        Assert.Equal(collapsed.Cards[1].CardBounds.Y, restored.Cards[1].CardBounds.Y);
        AssertStackGeometry(restored);
    }

    [Fact]
    public void Both_expanded_cards_create_page_overflow_without_overlapping_frames()
    {
        OblivionStandaloneSurface surface = new();
        foreach (OblivionCard card in surface.Cards)
        {
            surface.ToggleExpansion(card.Id.Value);
        }

        OblivionStandaloneSurfaceSnapshot snapshot = surface.CreateSnapshot(2560, 1440);

        Assert.True(snapshot.PageContentHeight > snapshot.ViewportHeight);
        AssertStackGeometry(snapshot);
        Assert.Equal(
            Style.OuterVerticalMargin,
            snapshot.PageContentHeight -
                (snapshot.Cards[1].CardBounds.Y + snapshot.Cards[1].CardBounds.Height));
    }

    [Fact]
    public void Selection_and_independent_expansion_survive_resize_recomposition()
    {
        OblivionStandaloneSurface surface = new();
        string secondId = surface.Cards[1].Id.Value;
        surface.Select(secondId);
        surface.ToggleExpansion(secondId);

        OblivionStandaloneSurfaceSnapshot resized = surface.CreateSnapshot(1920, 1080);

        Assert.Equal(secondId, surface.SelectedCardId);
        Assert.False(surface.IsExpanded(surface.Cards[0]));
        Assert.True(surface.IsExpanded(surface.Cards[1]));
        Assert.False(resized.Cards[0].IsSelected);
        Assert.True(resized.Cards[1].IsSelected);
        Assert.Equal(surface.Cards.Select(card => card.Id), resized.Cards.Select(card => card.Card.Id));
        AssertStackGeometry(resized);
    }

    [Fact]
    public void Responsive_width_applies_identically_to_both_cards()
    {
        OblivionStandaloneSurface surface = new();

        OblivionStandaloneSurfaceSnapshot wide = surface.CreateSnapshot(2560, 1440);
        OblivionStandaloneSurfaceSnapshot resized = surface.CreateSnapshot(1920, 1080);

        Assert.All(wide.Cards, card =>
        {
            Assert.Equal(Style.OuterHorizontalMargin, card.CardBounds.X);
            Assert.Equal(
                2560 - (Style.OuterHorizontalMargin * 2),
                card.CardBounds.Width);
        });
        Assert.All(resized.Cards, card => Assert.Equal(
            1920 - (Style.OuterHorizontalMargin * 2),
            card.CardBounds.Width));
    }

    [Fact]
    public void Square_affordances_are_identical_and_keep_position_within_each_card()
    {
        OblivionStandaloneSurface surface = new();
        OblivionStandaloneSurfaceSnapshot collapsed = surface.CreateSnapshot(2560, 1440);

        surface.ToggleExpansion(surface.Cards[0].Id.Value);
        OblivionStandaloneSurfaceSnapshot expanded = surface.CreateSnapshot(2560, 1440);

        Assert.Equal(
            collapsed.Cards[0].ExpansionAffordanceBounds,
            expanded.Cards[0].ExpansionAffordanceBounds);
        Assert.Equal(
            collapsed.Cards[0].ExpansionAffordanceBounds.Width,
            collapsed.Cards[1].ExpansionAffordanceBounds.Width);
        Assert.Equal(
            collapsed.Cards[0].ExpansionAffordanceBounds.Height,
            collapsed.Cards[1].ExpansionAffordanceBounds.Height);
        Assert.True(collapsed.Cards[0].ExpansionAffordanceBounds.Width >= 36);
        Assert.True(collapsed.Cards[0].ExpansionAffordanceBounds.Height >= 36);
    }

    [Fact]
    public void Page_scroll_offset_is_session_state_and_does_not_change_card_state()
    {
        OblivionStandaloneSurface surface = new();
        string secondId = surface.Cards[1].Id.Value;
        surface.Select(secondId);
        surface.ToggleExpansion(secondId);

        surface.SetPageScrollOffset(120);

        Assert.Equal(120, surface.Session.GetMainScrollOffset(surface.PageId));
        Assert.Equal(secondId, surface.SelectedCardId);
        Assert.True(surface.IsExpanded(surface.Cards[1]));
    }

    [Theory]
    [InlineData(500, 500, 0, -1, OblivionStandaloneScrollOwner.Page)]
    [InlineData(700, 500, 0, -1, OblivionStandaloneScrollOwner.Document)]
    [InlineData(700, 500, 200, -1, OblivionStandaloneScrollOwner.Page)]
    [InlineData(700, 500, 100, 1, OblivionStandaloneScrollOwner.Document)]
    [InlineData(700, 500, 0, 1, OblivionStandaloneScrollOwner.Page)]
    public void Wheel_routing_prefers_local_overflow_only_while_it_can_move(
        double extent,
        double viewport,
        double offset,
        double deltaY,
        OblivionStandaloneScrollOwner expected)
    {
        Assert.Equal(
            expected,
            OblivionStandaloneScrollRouting.ResolveOwner(
                extent,
                viewport,
                offset,
                deltaY));
    }

    [Fact]
    public void Product_semantic_assemblies_remain_Avalonia_free()
    {
        AssertAvaloniaFree(typeof(OblivionCard).Assembly);
        AssertAvaloniaFree(typeof(OblivionWorkspaceLoader).Assembly);
        AssertAvaloniaFree(typeof(PresentationMaterializer).Assembly);
    }

    private static void AssertStackGeometry(OblivionStandaloneSurfaceSnapshot snapshot)
    {
        Assert.Equal(2, snapshot.Cards.Count);
        Assert.Equal(
            Style.StackGap,
            snapshot.Cards[1].CardBounds.Y -
                (snapshot.Cards[0].CardBounds.Y + snapshot.Cards[0].CardBounds.Height));
        Assert.True(
            snapshot.Cards[0].CardBounds.Y + snapshot.Cards[0].CardBounds.Height <
            snapshot.Cards[1].CardBounds.Y);
    }

    private static void AssertAvaloniaFree(Assembly assembly)
    {
        Assert.DoesNotContain(
            assembly.GetReferencedAssemblies(),
            reference => reference.Name?.StartsWith("Avalonia", StringComparison.Ordinal) == true);
    }
}
