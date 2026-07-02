namespace Machina.Presenter.Sample;

public readonly record struct PresenterSurfaceSize(
    int WindowWidth,
    int WindowHeight,
    int SurfaceX,
    int SurfaceY,
    int SurfaceWidth,
    int SurfaceHeight,
    bool IsLetterboxed)
{
    public const int MinimumSurfaceWidth = 960;
    public const int MinimumSurfaceHeight = 540;
    public const int DefaultRuntimeSurfaceWidth = 1280;
    public const int DefaultRuntimeSurfaceHeight = 720;

    private const int AspectWidth = 16;
    private const int AspectHeight = 9;

    public static PresenterSurfaceSize DefaultRuntime { get; } = Compute(
        DefaultRuntimeSurfaceWidth,
        DefaultRuntimeSurfaceHeight);

    public static PresenterSurfaceSize Compute(
        int windowWidth,
        int windowHeight,
        bool clampToMinimumWindowSize = true)
    {
        int effectiveWindowWidth = clampToMinimumWindowSize
            ? Math.Max(MinimumSurfaceWidth, windowWidth)
            : Math.Max(1, windowWidth);
        int effectiveWindowHeight = clampToMinimumWindowSize
            ? Math.Max(MinimumSurfaceHeight, windowHeight)
            : Math.Max(1, windowHeight);

        long widthLimitedHeight = (long)effectiveWindowWidth * AspectHeight;
        long heightLimitedWidth = (long)effectiveWindowHeight * AspectWidth;

        int surfaceWidth;
        int surfaceHeight;

        if (widthLimitedHeight <= heightLimitedWidth)
        {
            surfaceWidth = effectiveWindowWidth;
            surfaceHeight = (int)(widthLimitedHeight / AspectWidth);
        }
        else
        {
            surfaceWidth = (int)(heightLimitedWidth / AspectHeight);
            surfaceHeight = effectiveWindowHeight;
        }

        int surfaceX = (effectiveWindowWidth - surfaceWidth) / 2;
        int surfaceY = (effectiveWindowHeight - surfaceHeight) / 2;

        return new PresenterSurfaceSize(
            WindowWidth: effectiveWindowWidth,
            WindowHeight: effectiveWindowHeight,
            SurfaceX: surfaceX,
            SurfaceY: surfaceY,
            SurfaceWidth: surfaceWidth,
            SurfaceHeight: surfaceHeight,
            IsLetterboxed: surfaceX > 0 || surfaceY > 0);
    }

    public static PresenterSurfaceSize ComputeFromClientSize(double clientWidth, double clientHeight)
    {
        return Compute(
            (int)Math.Floor(clientWidth),
            (int)Math.Floor(clientHeight),
            clampToMinimumWindowSize: false);
    }
}
