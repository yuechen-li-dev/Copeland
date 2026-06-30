namespace Machina.Presenter.Sample;

public sealed record PresenterNavigationExportOptions(
    bool IncludeNavigationShell,
    string? SelectedSectionId = null,
    string? SelectedTabId = null,
    string? SelectedPageId = null,
    string? SelectedCardId = null,
    string? InvokeActionId = null,
    IReadOnlyDictionary<string, double>? ScrollOffsetByPageId = null,
    string? InteractionBackendName = null)
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
            includeNavigationShell ? AvaloniaPresenterInputBackend.BackendName : null)
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
            scrollOffsetByPageId,
            includeNavigationShell ? AvaloniaPresenterInputBackend.BackendName : null)
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
            scrollOffsetByPageId,
            includeNavigationShell ? AvaloniaPresenterInputBackend.BackendName : null)
    {
    }

    public static PresenterNavigationExportOptions DefaultShell { get; } =
        new(true, InteractionBackendName: AvaloniaPresenterInputBackend.BackendName);

    public static PresenterNavigationExportOptions Disabled { get; } = new(false);
}
