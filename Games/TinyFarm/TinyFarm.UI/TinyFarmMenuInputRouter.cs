using Machina.Runtime.Input;
using TinyFarm.Core;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

/// <summary>One tick of cursor input, already mapped into TinyFarm's 1280x720 menu space.</summary>
internal readonly record struct TinyFarmPointerInput(
    PointerPoint Point,
    bool Moved,
    bool Pressed,
    bool Released,
    double WheelX,
    double WheelY);

/// <summary>Cursor input for TinyFarm's own screens: inventory, stats, stove and chest. Play ignores the cursor.</summary>
internal static class TinyFarmMenuInputRouter
{
    public static void Pointer(TinyFarmGame game, TinyFarmNativeUi ui, TinyFarmPointerInput pointer)
    {
        if (!game.IsModalMenu && game.Screen != TinyFarmScreen.Paused)
        {
            ui.ResetPointer();
            return;
        }
        if (pointer.Moved && game.Screen == TinyFarmScreen.Stats)
        {
            // Drags the stats scrollbar thumb while the button is held.
            ui.ScrollStats(new UiPointerMoved(pointer.Point, null, UiModifiers.None));
        }
        if (pointer.WheelX != 0 || pointer.WheelY != 0)
        {
            Wheel(game, ui, pointer);
        }
        if (pointer.Pressed)
        {
            ui.Pointer(TinyFarmFrameProjector.Project(game.State, game.Definitions), pointer.Point, true);
        }
        if (pointer.Released)
        {
            ui.Pointer(TinyFarmFrameProjector.Project(game.State, game.Definitions), pointer.Point, false);
        }
    }

    private static void Wheel(TinyFarmGame game, TinyFarmNativeUi ui, TinyFarmPointerInput pointer)
    {
        if (game.Screen == TinyFarmScreen.Stats)
        {
            ui.ScrollStats(new UiPointerWheel(pointer.Point, pointer.WheelX, pointer.WheelY, UiModifiers.None));
            return;
        }
        if (game.Screen != TinyFarmScreen.Inventory)
        {
            return;
        }
        PointerPoint point = pointer.Point;
        bool overRows = point.X >= 234 && point.X < 854 && point.Y >= 218 && point.Y < 594;
        if (overRows && pointer.WheelY != 0)
        {
            int count = game.Menus.Rows(game.State, game.Definitions).Count;
            game.Menus.Scroll(-Math.Sign(pointer.WheelY) * 3, count);
        }
    }
}
