using Machina.Core.Actions;
using Aurelian.GameMenus;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Standard.Authoring;
using Machina.Standard.Theme;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

internal static partial class TinyFarmMenuPresentation
{
    // Shared Aurelian menu buttons retain TinyFarm-owned art, copy, and action semantics.
    private static void Title(List<UiNode> nodes, TinyFarmGame game)
    {
        Label(nodes, "title-name", "TINYFARM", 80, 140, 470, TextSize.H1, Gold);
        Label(nodes, "title-subtitle", "THE SLEEPING SPRING", 82, 192, 460, TextSize.Md, Gold);
        Label(nodes, "title-invitation", "A little home. A world beyond the garden.", 82, 244, 460, TextSize.Sm);
        string[] labels = ["NEW GAME / ENTER", "LOAD GAME / N", "QUIT / Q"];
        var entries = new List<GameMenuEntry>();
        for (int index = 0; index < TinyFarmGame.TitleActions.Length; index++)
        {
            string action = TinyFarmGame.TitleActions[index];
            bool disabled = game.LoadInProgress || action == "load" && !game.MenuSaveAvailable;
            entries.Add(new GameMenuEntry(action, labels[index], disabled));
        }
        GameMenuTemplate.AddButtons(nodes, entries, game.TitleSelection,
            new GameMenuButtonLayout(80, 304, 380),
            new GameMenuButtonStyle(Background: 0x19362BE8, SelectedBackground: 0x4A6655F4,
                Foreground: Gold, DisabledForeground: 0x8A9B8EFF, Border: 0x83978480, SelectedBorder: Gold));
        Label(nodes, "title-checkpoint", game.MenuSaveAvailable ? "Your local checkpoint is ready." : "No saved game yet. Begin a new spring.",
            82, 540, 480, TextSize.Sm);
        Wrap(nodes, "title-status", game.LoadInProgress ? "Loading your garden..." : game.Status, 82, 584, 44);
        Label(nodes, "title-controls", "Click to choose / UP-DOWN + ENTER", 82, 676, 500, TextSize.Sm);
    }
}
