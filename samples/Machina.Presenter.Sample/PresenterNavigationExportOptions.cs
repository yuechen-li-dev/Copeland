namespace Machina.Presenter.Sample;

public sealed record PresenterNavigationExportOptions(
    bool IncludeNavigationShell,
    string? SelectedSectionId = null,
    string? SelectedTabId = null,
    string? SelectedPageId = null,
    IReadOnlyDictionary<string, double>? ScrollOffsetByPageId = null,
    string? InteractionBackendName = null)
{
    public PresenterNavigationExportOptions(
        bool includeNavigationShell,
        string? selectedPageId,
        IReadOnlyDictionary<string, double>? scrollOffsetByPageId)
        : this(
            includeNavigationShell,
            null,
            null,
            selectedPageId,
            scrollOffsetByPageId,
            includeNavigationShell ? AvaloniaPresenterInputBackend.BackendName : null)
    {
    }

    public static PresenterNavigationExportOptions Disabled { get; } = new(false);
}
