namespace Aurelian.Core.Presentation.Screens;

public static class Layer
{
    public static ScreenLayerSlot At(string name, int order)
    {
        return new ScreenLayerSlot(new ScreenLayerKey(name), order);
    }
}
