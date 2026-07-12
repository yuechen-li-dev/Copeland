namespace Aurelian.Core.Presentation.Screens;

public static class ScreenLayers
{
    public static readonly ScreenLayerSlot Background = new(new ScreenLayerKey("background"), 0);
    public static readonly ScreenLayerSlot World = new(new ScreenLayerKey("world"), 100);
    public static readonly ScreenLayerSlot Hud = new(new ScreenLayerKey("hud"), 200);
    public static readonly ScreenLayerSlot Overlay = new(new ScreenLayerKey("overlay"), 300);
    public static readonly ScreenLayerSlot Modal = new(new ScreenLayerKey("modal"), 400);
    public static readonly ScreenLayerSlot Debug = new(new ScreenLayerKey("debug"), 900);
    public static readonly ScreenLayerSlot Cursor = new(new ScreenLayerKey("cursor"), 1000);
}
