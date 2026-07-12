namespace Aurelian.Rendering.Contracts.Snapshots;

public readonly record struct RenderTransform2(
    double X,
    double Y,
    double RotationRadians,
    double ScaleX,
    double ScaleY)
{
    public static RenderTransform2 Identity { get; } = new(0, 0, 0, 1, 1);
}
