using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Standard.Authoring;
using Machina.Standard.Theme;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

internal static partial class TinyFarmMenuPresentation
{
    // Sunkill's VnMachinaLayer.BuildMenu / MenuButton pattern: named actions,
    // selected entries and real StandardUI buttons over a separate art background.
    private static void Title(List<UiNode> nodes, TinyFarmGame game)
    {
        Label(nodes, "title-name", "TINYFARM", 80, 140, 470, TextSize.H1, Gold);
        Label(nodes, "title-subtitle", "THE SLEEPING SPRING", 82, 192, 460, TextSize.Md, Gold);
        Label(nodes, "title-invitation", "A little home. A world beyond the garden.", 82, 244, 460, TextSize.Sm);
        string[] labels = ["NEW GAME / ENTER", "LOAD GAME / N", "QUIT / Q"];
        for (int index = 0; index < TinyFarmGame.TitleActions.Length; index++)
        {
            string action = TinyFarmGame.TitleActions[index];
            bool selected = index == game.TitleSelection;
            bool disabled = game.LoadInProgress || action == "load" && !game.MenuSaveAvailable;
            nodes.Add(UI.Anchor(StandardUI.Button(labels[index], id: "menu." + action,
                action: UiAction.Named(action), disabled: disabled,
                style: new StandardButtonStyle(ColorToken.Hex(selected ? 0x4A6655F4u : 0x19362BE8u),
                    ColorToken.Hex(Gold), ColorToken.Hex(selected ? Gold : 0x83978480), selected ? 2 : 1,
                    new TextStyle(ColorToken.Hex(disabled ? 0x8A9B8EFF : Gold), TextSize.Md, TextAlignX.Left, TextAlignY.Center),
                    380, 56, CornerRadius: 8, HorizontalPadding: 16)), left: 80, top: 304 + index * 72, width: 380, height: 56));
        }
        Label(nodes, "title-checkpoint", game.MenuSaveAvailable ? "Your local checkpoint is ready." : "No saved game yet. Begin a new spring.",
            82, 540, 480, TextSize.Sm);
        Wrap(nodes, "title-status", game.LoadInProgress ? "Loading your garden..." : game.Status, 82, 584, 44);
        Label(nodes, "title-controls", "Click to choose / UP-DOWN + ENTER", 82, 676, 500, TextSize.Sm);
    }
}
