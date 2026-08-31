using System.Reflection;
using Oblivion.App;
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
    public void M20b_native_recon_uses_the_exact_m20a_diagram_ir_with_stable_geometry_and_svg()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "M20aDenseDiagram.oblivion");
        OblivionStandaloneSurface surface = new(root);
        OblivionCard card = surface.Cards.Single(candidate => candidate.Kind == OblivionCardKind.Diagram);
        OblivionDiagramSemanticProjectionResult projection = new OblivionDiagramCardRealizer()
            .ProjectSemanticDiagram(card, root);

        Assert.True(projection.Succeeded, string.Join(Environment.NewLine, projection.Diagnostics));
        Assert.Equal(16, projection.Diagram!.Nodes.Count);
        Assert.Equal(31, projection.Diagram.Edges.Count);
        M20bNativeDiagramGeometry first = M20bNativeDiagramLayout.Resolve(projection.Diagram);
        M20bNativeDiagramGeometry second = M20bNativeDiagramLayout.Resolve(projection.Diagram);
        string light = M20bNativeDiagramSvgEmitter.Emit(first, OblivionResolvedAppearance.Light);
        string dark = M20bNativeDiagramSvgEmitter.Emit(second, OblivionResolvedAppearance.Dark);

        Assert.Equal(first.Width, second.Width);
        Assert.Equal(first.Height, second.Height);
        Assert.Equal(first.Nodes.ToArray(), second.Nodes.ToArray());
        Assert.Equal(first.Edges.ToArray(), second.Edges.ToArray());
        Assert.Equal(first.EdgeLabels.ToArray(), second.EdgeLabels.ToArray());
        Assert.NotEqual(light, dark);
        Assert.Equal(16, Count(light, "data-diagram-node-id="));
        Assert.Equal(31, Count(light, "data-semantic-identity="));
        Assert.All(projection.Diagram.Nodes, node => Assert.Contains($"id=\"{node.Id}\"", light));
    }

    [Fact]
    public void M20b_vertical_and_horizontal_slots_fill_the_usable_viewport()
    {
        OblivionStandaloneSurface surface = new();
        foreach (OblivionCard card in surface.Cards)
        {
            surface.ToggleExpansion(card.Id.Value);
        }

        surface.SetLayout(OblivionViewportLayoutMode.VerticalSplit);
        OblivionStandaloneSurfaceSnapshot vertical = surface.CreateSnapshot(2560, 1440);
        surface.SetLayout(OblivionViewportLayoutMode.HorizontalSplit);
        OblivionStandaloneSurfaceSnapshot horizontal = surface.CreateSnapshot(2560, 1440);

        Assert.Equal(2, vertical.Slots.Count);
        Assert.Equal(2, horizontal.Slots.Count);
        Assert.Equal(2, vertical.Cards.Count);
        Assert.Equal(2, horizontal.Cards.Count);
        Assert.Equal(vertical.Slots.Sum(slot => slot.Bounds.Height) + Style.StackGap, 1440 - (Style.OuterVerticalMargin * 2));
        Assert.Equal(horizontal.Slots.Sum(slot => slot.Bounds.Width) + Style.StackGap, 2560 - (Style.OuterHorizontalMargin * 2));
    }

    [Theory]
    [InlineData(OblivionAppearance.Light, OblivionResolvedAppearance.Dark, OblivionResolvedAppearance.Light)]
    [InlineData(OblivionAppearance.Dark, OblivionResolvedAppearance.Light, OblivionResolvedAppearance.Dark)]
    [InlineData(OblivionAppearance.System, OblivionResolvedAppearance.Light, OblivionResolvedAppearance.Light)]
    [InlineData(OblivionAppearance.System, OblivionResolvedAppearance.Dark, OblivionResolvedAppearance.Dark)]
    public void Startup_resolves_typed_appearance_against_the_platform_only_for_system(
        OblivionAppearance configured,
        OblivionResolvedAppearance platform,
        OblivionResolvedAppearance expected)
    {
        OblivionResolvedAppearance resolved = OblivionStandaloneAppearanceResolver.Resolve(
            configured,
            platform);

        Assert.Equal(expected, resolved);
        Assert.Contains(
            expected == OblivionResolvedAppearance.Light ? "m19p-light-v1" : "m19p-dark-v1",
            OblivionMermaidRendererOptions.RenderingOptionsFor(resolved));
    }

    [Fact]
    public void Dark_and_light_styles_populate_the_same_complete_token_shape()
    {
        AssertStyleComplete(OblivionStandaloneStyles.Dark);
        AssertStyleComplete(OblivionStandaloneStyles.Light);
        Assert.Equal(
            OblivionStandaloneStyles.Dark.GetType(),
            OblivionStandaloneStyles.Light.GetType());
    }

    [Fact]
    public void Appearance_changes_colors_without_changing_two_card_geometry_or_state()
    {
        OblivionStandaloneSurface darkSurface = new(style: OblivionStandaloneStyles.Dark);
        OblivionStandaloneSurface lightSurface = new(style: OblivionStandaloneStyles.Light);
        foreach (OblivionCard card in darkSurface.Cards)
        {
            darkSurface.ToggleExpansion(card.Id.Value);
        }
        foreach (OblivionCard card in lightSurface.Cards)
        {
            lightSurface.ToggleExpansion(card.Id.Value);
        }

        OblivionStandaloneSurfaceSnapshot dark = darkSurface.CreateSnapshot(2560, 1440);
        OblivionStandaloneSurfaceSnapshot light = lightSurface.CreateSnapshot(2560, 1440);

        Assert.Equal(dark.Width, light.Width);
        Assert.Equal(dark.ViewportHeight, light.ViewportHeight);
        Assert.Equal(dark.PageContentHeight, light.PageContentHeight);
        Assert.Equal(dark.Cards.Select(card => card.Card.Id), light.Cards.Select(card => card.Card.Id));
        Assert.Equal(dark.Cards.Select(card => card.CardBounds), light.Cards.Select(card => card.CardBounds));
        Assert.Equal(
            dark.Cards.Select(card => card.ExpansionAffordanceBounds),
            light.Cards.Select(card => card.ExpansionAffordanceBounds));
        Assert.Equal(dark.Cards.Select(card => card.IsSelected), light.Cards.Select(card => card.IsSelected));
        Assert.Equal(dark.Cards.Select(card => card.CardView.IsExpanded), light.Cards.Select(card => card.CardView.IsExpanded));
        Assert.NotEqual(
            dark.ShellFrame.Surface.GetPixel(0, 0),
            light.ShellFrame.Surface.GetPixel(0, 0));
    }

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
    public void Standalone_projects_a_real_three_card_push_without_a_cardinality_special_case()
    {
        string vaultRoot = CopyVaultToTemporaryDirectory();
        string source = Path.Combine(Path.GetTempPath(), $"m19k-standalone-{Guid.NewGuid():N}.md");
        File.WriteAllText(source, "# Third Card\n\nPushed through the product mutation path.\n");
        try
        {
            OblivionApplication application = new();
            OblivionWorkspaceSession session = application.OpenWorkspace(vaultRoot).Session!;
            OblivionStackOperationResult push = application.PushMarkdownCard(
                session,
                new OblivionPushMarkdownCardRequest(source, CardId: "third-card"));

            Assert.True(push.Succeeded, string.Join(Environment.NewLine, push.Diagnostics));
            OblivionStandaloneSurface surface = new(vaultRoot);
            OblivionStandaloneSurfaceSnapshot snapshot = surface.CreateSnapshot(2560, 1440);
            Assert.Equal(
                ["physical-atom", "notebook-stack", "third-card"],
                surface.Cards.Select(card => card.Id.Value));
            AssertStackGeometry(snapshot);
        }
        finally
        {
            File.Delete(source);
            Directory.Delete(vaultRoot, recursive: true);
        }
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
    public void Single_layout_expansion_fills_the_slot_and_collapse_remains_compact()
    {
        OblivionStandaloneSurface surface = new();
        OblivionStandaloneSurfaceSnapshot collapsed = surface.CreateSnapshot(2560, 1440);

        surface.ToggleExpansion(surface.Cards[0].Id.Value);
        OblivionStandaloneSurfaceSnapshot expanded = surface.CreateSnapshot(2560, 1440);

        Assert.Single(collapsed.Cards);
        Assert.Single(expanded.Cards);
        Assert.Equal(collapsed.Cards[0].SlotBounds, expanded.Cards[0].SlotBounds);
        Assert.Equal(collapsed.Cards[0].SlotBounds.Height, expanded.Cards[0].CardBounds.Height);
        Assert.Equal(Style.CollapsedCardHeight, collapsed.Cards[0].CardBounds.Height);

        surface.Collapse(surface.Cards[0].Id.Value);
        OblivionStandaloneSurfaceSnapshot restored = surface.CreateSnapshot(2560, 1440);

        Assert.Equal(collapsed.Cards[0].CardBounds, restored.Cards[0].CardBounds);
    }

    [Fact]
    public void Vertical_split_allocates_both_expanded_cards_without_page_overflow()
    {
        OblivionStandaloneSurface surface = new();
        foreach (OblivionCard card in surface.Cards)
        {
            surface.ToggleExpansion(card.Id.Value);
        }
        surface.SetLayout(OblivionViewportLayoutMode.VerticalSplit);

        OblivionStandaloneSurfaceSnapshot snapshot = surface.CreateSnapshot(2560, 1440);

        Assert.Equal(snapshot.ViewportHeight, snapshot.PageContentHeight);
        Assert.Equal(2, snapshot.Cards.Count);
        Assert.All(snapshot.Cards, card => Assert.Equal(card.SlotBounds, card.CardBounds));
        Assert.Equal(
            Style.StackGap,
            snapshot.Cards[1].SlotBounds.Y -
                (snapshot.Cards[0].SlotBounds.Y + snapshot.Cards[0].SlotBounds.Height));
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
        OblivionStandaloneCardSnapshot card = Assert.Single(resized.Cards);
        Assert.True(card.IsSelected);
        Assert.Equal(secondId, card.Card.Id.Value);
        Assert.Equal(card.SlotBounds.Height, card.CardBounds.Height);
    }

    [Fact]
    public void Responsive_width_applies_identically_to_both_cards()
    {
        OblivionStandaloneSurface surface = new();
        surface.SetLayout(OblivionViewportLayoutMode.VerticalSplit);

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
        surface.SetLayout(OblivionViewportLayoutMode.VerticalSplit);
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
        for (int index = 1; index < snapshot.Cards.Count; index++)
        {
            OblivionStandaloneCardSnapshot previous = snapshot.Cards[index - 1];
            OblivionStandaloneCardSnapshot current = snapshot.Cards[index];
            Assert.Equal(
                Style.StackGap,
                current.CardBounds.Y -
                    (previous.CardBounds.Y + previous.CardBounds.Height));
            Assert.True(
                previous.CardBounds.Y + previous.CardBounds.Height < current.CardBounds.Y);
        }
    }

    private static void AssertStyleComplete(OblivionStandaloneStyle style)
    {
        Assert.True(style.DevelopmentWidth > 0);
        Assert.True(style.DevelopmentHeight > 0);
        Assert.True(style.MaximumReadableWidth > 0);
        Assert.False(string.IsNullOrWhiteSpace(style.CardSubtitle));
        Assert.All(
            new[]
            {
                style.PageBackground,
                style.CardBackground,
                style.CardBorder,
                style.SelectedCardBorder,
                style.PrimaryText,
                style.SecondaryText,
                style.BadgeSurface,
                style.BadgeText,
                style.BadgeBorder,
                style.AffordanceSurface,
                style.AffordanceAccent,
                style.DocumentSurface,
                style.DocumentText,
                style.DocumentHeading,
                style.DocumentMutedText,
                style.DocumentCodeSurface,
                style.DocumentBorder,
                style.DocumentQuoteBorder,
                style.DocumentLinkText,
                style.DocumentDiagnosticText,
            },
            token => Assert.Equal(0xFFu, token.Rgba & 0xFFu));
        Assert.NotEqual(style.PageBackground, style.PrimaryText);
        Assert.NotEqual(style.DocumentSurface, style.DocumentText);
        Assert.NotEqual(style.CardBackground, style.SelectedCardBorder);
    }

    private static string CopyVaultToTemporaryDirectory()
    {
        string vaultRoot = Path.Combine(
            Path.GetTempPath(),
            "oblivion-m19k-standalone-tests",
            Guid.NewGuid().ToString("N"));
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

        return vaultRoot;
    }

    private static void AssertAvaloniaFree(Assembly assembly)
    {
        Assert.DoesNotContain(
            assembly.GetReferencedAssemblies(),
            reference => reference.Name?.StartsWith("Avalonia", StringComparison.Ordinal) == true);
    }

    private static int Count(string source, string value)
    {
        return source.Split(value, StringSplitOptions.None).Length - 1;
    }
}
