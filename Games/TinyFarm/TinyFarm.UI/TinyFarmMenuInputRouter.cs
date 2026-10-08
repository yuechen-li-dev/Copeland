using Aurelian.Composition;
using Machina.Runtime.Input;
using TinyFarm.Core;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

internal static class TinyFarmMenuInputRouter
{
    public static void Handle(TinyFarmGame game, TinyFarmNativeUi ui, LayerInputEvent input,
        Func<LayerPoint, PointerPoint> toMenuPoint)
    {
        if (!game.IsModalMenu && game.Screen != TinyFarmScreen.Paused)
        {
            ui.ResetPointer();
            return;
        }
        switch (input)
        {
            case LayerTextEntered text:
                game.EnterMenuText(text.Text);
                break;
            case LayerKeyChanged { IsPressed: true, Key: LayerKey.Backspace }:
                game.EditMenuSearch(false);
                break;
            case LayerKeyChanged { IsPressed: true, Key: LayerKey.Delete }:
                game.EditMenuSearch(true);
                break;
            case LayerKeyChanged { IsPressed: true, Key: LayerKey.Tab } when game.IsAgentMenu:
                game.Menus.SearchFocused = !game.Menus.SearchFocused;
                break;
            case LayerPointerMoved moved when game.Screen == TinyFarmScreen.Stats:
                ui.ScrollStats(new UiPointerMoved(toMenuPoint(moved.Position), null, UiModifiers.None));
                break;
            case LayerPointerWheel wheel when game.Screen == TinyFarmScreen.Stats:
                ui.ScrollStats(new UiPointerWheel(toMenuPoint(wheel.Position), wheel.DeltaX, wheel.DeltaY, UiModifiers.None));
                break;
            case LayerPointerButtonChanged { Button: LayerPointerButton.Primary } pointer:
                ui.Pointer(TinyFarmFrameProjector.Project(game.State, game.Definitions),
                    toMenuPoint(pointer.Position), pointer.IsPressed);
                break;
            case LayerPointerWheel wheel when game.Screen == TinyFarmScreen.Inventory:
                PointerPoint point = toMenuPoint(wheel.Position);
                if (point.X >= 234 && point.X < 854 && point.Y >= 218 && point.Y < 594)
                {
                    int count = game.Menus.Rows(game.State, game.Definitions).Count;
                    game.Menus.Scroll(-Math.Sign(wheel.DeltaY) * 3, count);
                }
                break;
        }
    }
}
