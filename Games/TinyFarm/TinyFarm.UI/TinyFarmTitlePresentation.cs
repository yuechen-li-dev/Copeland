using Machina.Core.Actions;
using Aurelian.GameMenus;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Standard.Authoring;
using Machina.Standard.Text;
using Machina.Standard.Theme;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

internal static partial class TinyFarmMenuPresentation
{
    // Shared Aurelian menu buttons retain TinyFarm-owned art, copy, and action semantics.
    private static void Title(List<UiNode> nodes, TinyFarmGame game, IMachinaTextMeasurer? measurer)
    {
        nodes.Add(StandardUI.Label("TINYFARM", 80, 140, 470, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.TitleName],
            size: TextSize.H1, color: TinyFarmUiTheme.Accent));
        nodes.Add(StandardUI.Label("THE SLEEPING SPRING", 82, 192, 460, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.TitleSubtitle],
            size: TextSize.Md, color: TinyFarmUiTheme.Accent));
        nodes.Add(StandardUI.Label("A little home. A world beyond the garden.", 82, 244, 460, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.TitleInvitation],
            size: TextSize.Sm, color: TinyFarmUiTheme.Text));
        string[] labels = ["NEW GAME / ENTER", "LOAD GAME / N", "QUIT / Q"];
        var entries = new List<GameMenuEntry>();
        for (int index = 0; index < TinyFarmGame.TitleActions.Length; index++)
        {
            UiAction<TinyFarmMenuMessage> action = TinyFarmGame.TitleActions[index];
            bool disabled = game.LoadInProgress || action == TinyFarmUiActions.Load && !game.MenuSaveAvailable;
            entries.Add(new GameMenuEntry(action.Name, labels[index], disabled, Action: action));
        }
        GameMenuTemplate.AddButtons(nodes, entries, game.TitleSelection,
            new GameMenuButtonLayout(80, 304, 380),
            new GameMenuButtonStyle(Background: 0x19362BE8, SelectedBackground: 0x4A6655F4,
                Foreground: Gold, DisabledForeground: 0x8A9B8EFF, Border: 0x83978480, SelectedBorder: Gold));
        nodes.Add(StandardUI.Label(game.MenuSaveAvailable ? "Your local checkpoint is ready." : "No saved game yet. Begin a new spring.", 82, 540, 480, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.TitleCheckpoint],
            size: TextSize.Sm, color: TinyFarmUiTheme.Text));
        nodes.Add(StandardUI.Place(StandardUI.Paragraph(game.LoadInProgress ? "Loading your garden..." : game.Status, 440, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.TitleStatus],
            foreground: TinyFarmUiTheme.Text, measurer: measurer), 82, 584));
        nodes.Add(StandardUI.Label("Click to choose / UP-DOWN + ENTER", 82, 676, 500, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.TitleControls],
            size: TextSize.Sm, color: TinyFarmUiTheme.Text));
    }
}
