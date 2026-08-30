namespace Machina.Presenter.Sample;

public enum PresenterShellMode
{
    Wide,
    Compact
}

public static class PresenterShellModeExtensions
{
    public static OblivionShellMode ToOblivionShellMode(this PresenterShellMode mode)
    {
        return mode == PresenterShellMode.Wide
            ? OblivionShellMode.Wide
            : OblivionShellMode.Compact;
    }
}

public static class PresenterShellModeResolver
{
    public const int BreakpointWidth = 1120;

    public static PresenterShellMode Resolve(double windowWidth)
    {
        return windowWidth >= BreakpointWidth
            ? PresenterShellMode.Wide
            : PresenterShellMode.Compact;
    }
}
