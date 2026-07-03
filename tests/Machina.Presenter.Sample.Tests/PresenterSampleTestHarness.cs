using Machina.Layout.Geometry;
using Machina.Presenter.Sample;
using Machina.Standard.Theme;

namespace Machina.Presenter.Sample.Tests;

internal static class PresenterSampleTestHarness
{
    public const int WideWidth = 1280;
    public const int WideHeight = 720;
    public const int CompactWidth = 960;
    public const int CompactHeight = 540;
    public const string ExpandedDocsCardId = "doc-aurelian-build-topology-m13b";
    public const string AlternateDocsCardId = "doc-copeland-markdown-frontend-m12a";

    public static PresenterProofOptions ProofOptions { get; } = new();

    public static StandardTheme Theme { get; } = StandardTheme.Default;

    public static PresenterNavigationModel CreateModel()
    {
        return PresenterNavigationCatalog.CreateModel();
    }

    public static PresenterNavigationState CreateDefaultState()
    {
        return PresenterNavigationState.CreateDefault(CreateModel());
    }

    public static PresenterNavigationState CreateDocsState(
        string? selectedCardId = AlternateDocsCardId,
        string? expandedCardId = null,
        double mainScrollOffset = 0,
        double inspectorScrollOffset = 0,
        double rawSourceScrollOffset = 0,
        double bodyScrollOffset = 0)
    {
        PresenterNavigationState state = CreateDefaultState()
            .WithSelectedSection("oblivion")
            .WithSelectedTab("oblivion", "docs")
            .WithScrollOffset(OblivionWorkbenchCatalog.DocsPageId, mainScrollOffset)
            .WithInspectorScrollOffset(OblivionWorkbenchCatalog.DocsPageId, inspectorScrollOffset);

        if (!string.IsNullOrWhiteSpace(selectedCardId))
        {
            state = state
                .WithSelectedCard(OblivionWorkbenchCatalog.DocsPageId, selectedCardId)
                .WithRawMarkdownSourceScrollOffset(selectedCardId, rawSourceScrollOffset);
        }

        if (!string.IsNullOrWhiteSpace(expandedCardId))
        {
            state = state.WithCardViewState(
                OblivionWorkbenchCatalog.DocsPageId,
                expandedCardId,
                new OblivionCardViewState(true, bodyScrollOffset));
        }

        return state;
    }

    public static PresenterNavigationLayout CreateLayout(
        int width = WideWidth,
        int height = WideHeight,
        PresenterShellMode? shellMode = null)
    {
        PresenterShellMode effectiveShellMode = shellMode ?? PresenterShellModeResolver.Resolve(width);
        return PresenterNavigationLayout.Create(width, height, effectiveShellMode);
    }

    public static PresenterNavigationShellRenderResult RenderShell(
        PresenterNavigationState state,
        int width = WideWidth,
        int height = WideHeight,
        PresenterNavigationRenderSession? session = null)
    {
        PresenterNavigationLayout layout = CreateLayout(width, height);
        return PresenterNavigationShellRenderer.Render(
            DemoState.Default,
            state,
            Theme,
            ProofOptions,
            session,
            layout);
    }

    public static PresenterPageRenderResult RenderDocsPage(
        PresenterNavigationState? state = null,
        int width = WideWidth,
        int height = WideHeight)
    {
        PresenterNavigationLayout layout = CreateLayout(width, height);
        return PresenterNavigationCatalog.RenderPage(
            OblivionWorkbenchCatalog.DocsPageId,
            DemoState.Default,
            Theme,
            ProofOptions,
            layout.ContentVisibleWidth,
            layout.ViewportHeight,
            state ?? CreateDocsState(),
            layout.ShellMode);
    }

    public static PresenterInputEvent Wheel(PresenterInputPoint point, float delta)
    {
        return new PresenterInputEvent(PresenterInputKind.Wheel, point, WheelDeltaY: delta);
    }

    public static PresenterInputPoint Center(Rect rect)
    {
        return new PresenterInputPoint(
            (float)(rect.X + (rect.Width / 2)),
            (float)(rect.Y + (rect.Height / 2)));
    }
}
