namespace Machina.Presenter.Sample;

public sealed record PresenterNavigationExportOptions(
    bool IncludeNavigationShell,
    string? SelectedPageId = null,
    IReadOnlyDictionary<string, double>? ScrollOffsetByPageId = null)
{
    public static PresenterNavigationExportOptions Disabled { get; } = new(false);
}
