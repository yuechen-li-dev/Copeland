namespace Machina.Layout.Frames;

public readonly record struct EdgeInsets(
    double Top,
    double Right,
    double Bottom,
    double Left)
{
    public static EdgeInsets All(double value) => new(value, value, value, value);
}
