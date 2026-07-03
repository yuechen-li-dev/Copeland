using Machina.Layout.Frames;

namespace Machina.Core.Authoring;

public readonly record struct UiPadding(
    double Top,
    double Right,
    double Bottom,
    double Left)
{
    public static UiPadding Zero => new(0, 0, 0, 0);

    public static UiPadding All(double value)
    {
        return new UiPadding(value, value, value, value);
    }

    public EdgeInsets ToEdgeInsets()
    {
        return new EdgeInsets(Top, Right, Bottom, Left);
    }
}
