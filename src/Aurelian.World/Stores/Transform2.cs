namespace Aurelian.World.Stores;

public readonly record struct Transform2(
    double X,
    double Y,
    double RotationRadians,
    double ScaleX,
    double ScaleY)
{
    public static Transform2 Identity { get; } = new(0, 0, 0, 1, 1);
}
