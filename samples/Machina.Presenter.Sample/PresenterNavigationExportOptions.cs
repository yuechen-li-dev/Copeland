namespace Machina.Presenter.Sample;

public sealed record PresenterNavigationExportOptions(
    bool IncludeNavigationShell,
    string? SelectedSectionId = null,
    string? SelectedTabId = null,
    string? SelectedPageId = null,
    string? SelectedCardId = null,
    string? ExpandedCardId = null,
    double? ExpandedCardBodyScroll = null,
    PresenterCompactPane? CompactPane = null,
    PresenterShellMode? ShellMode = null,
    int Width = 1120,
    int Height = 760,
    string? InvokeActionId = null,
    IReadOnlyDictionary<string, double>? ScrollOffsetByPageId = null,
    string? InteractionBackendName = null,
    bool RuntimeSizeExplicit = false)
{
    public PresenterNavigationExportOptions(
        bool includeNavigationShell,
        string? selectedPageId)
        : this(
            includeNavigationShell,
            null,
            null,
            selectedPageId,
            null,
            null,
            null,
            null,
            null,
            1120,
            760,
            null,
            null,
            includeNavigationShell ? AvaloniaPresenterInputBackend.BackendName : null,
            false)
    {
    }

    public PresenterNavigationExportOptions(
        bool includeNavigationShell,
        string? selectedPageId,
        IReadOnlyDictionary<string, double>? scrollOffsetByPageId)
        : this(
            includeNavigationShell,
            null,
            null,
            selectedPageId,
            null,
            null,
            null,
            null,
            null,
            1120,
            760,
            null,
            scrollOffsetByPageId,
            includeNavigationShell ? AvaloniaPresenterInputBackend.BackendName : null,
            false)
    {
    }

    public PresenterNavigationExportOptions(
        bool includeNavigationShell,
        string? selectedPageId,
        string? selectedCardId,
        IReadOnlyDictionary<string, double>? scrollOffsetByPageId)
        : this(
            includeNavigationShell,
            null,
            null,
            selectedPageId,
            selectedCardId,
            null,
            null,
            null,
            null,
            1120,
            760,
            null,
            scrollOffsetByPageId,
            includeNavigationShell ? AvaloniaPresenterInputBackend.BackendName : null,
            false)
    {
    }

    public static PresenterNavigationExportOptions DefaultShell { get; } =
        new(true, InteractionBackendName: AvaloniaPresenterInputBackend.BackendName);

    public static PresenterNavigationExportOptions Disabled { get; } = new(false);
}
