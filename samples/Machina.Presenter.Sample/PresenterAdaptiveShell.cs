namespace Machina.Presenter.Sample;

public enum PresenterShellMode
{
    Wide,
    Compact
}

public enum PresenterCompactPane
{
    CardList,
    Inspector
}

public static class PresenterShellModeResolver
{
    public const int BreakpointWidth = 760;

    public static PresenterShellMode Resolve(double windowWidth)
    {
        return windowWidth >= BreakpointWidth
            ? PresenterShellMode.Wide
            : PresenterShellMode.Compact;
    }
}
