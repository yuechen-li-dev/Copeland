using Machina.Core.Authoring;
using Machina.Core.Lowering;
using Machina.Core.Measurement;
using Machina.Core;
using Machina.Core.Nodes;
using Machina.Layout.Geometry;
using Machina.Core.Styling;
using Machina.Pipeline;
using Machina.Presentation;

namespace Aurelian.Beacon3D;

internal sealed class BeaconHud(ITextMeasurer font)
{
    private string? previous;
    private MachinaPresentationFrame? frame;

    public MachinaPresentationFrame Prepare(BeaconGame game)
    {
        string header = $"BEACON RUN   WAVE {game.Wave}/3   KILLS {game.Kills}/9   BEACONS {game.CollectedCount}/3";
        string status = $"HEALTH {game.Health}   AMMO {game.Ammo}/12   {game.CombatStatus}";
        string objective = game.GateOpen ? "GATE OPEN - HEAD NORTH TO ESCAPE" : "SURVIVE THE STALKERS. COLLECT THE GOLD BEACONS.";
        string signature = header + status + objective + game.Hurt;
        if (signature == previous && frame is not null)
        {
            return frame;
        }
        var nodes = new List<UiNode>();
        if (game.Hurt)
        {
            nodes.Add(UI.Anchor(UI.Rect(style: new UiStyle(Background: ColorToken.Hex(0xAE193030))),
                left: 0, top: 0, width: 960, height: 600));
        }
        nodes.Add(UI.Anchor(UI.Rect(style: new UiStyle(Background: ColorToken.Hex(0x071520D8))),
            left: 16, top: 16, width: 928, height: 76));
        nodes.Add(Label("hud.header", header, 32, 28, 896));
        nodes.Add(Label("hud.objective", objective, 32, 57, 896));
        nodes.Add(UI.Anchor(UI.Rect(style: new UiStyle(Background: ColorToken.Hex(0x071520E8))),
            left: 16, top: 548, width: 928, height: 36));
        nodes.Add(Label("hud.status", status, 32, 558, 896));
        nodes.Add(UI.Anchor(UI.Rect(style: new UiStyle(Background: ColorToken.Hex(0xFFF1D4FF))),
            left: 474, top: 299, width: 12, height: 2));
        nodes.Add(UI.Anchor(UI.Rect(style: new UiStyle(Background: ColorToken.Hex(0xFFF1D4FF))),
            left: 479, top: 294, width: 2, height: 12));
        var surface = UI.Surface(id: "beacon.hud", width: 960, height: 600, children: nodes);
        frame = new MachinaPresentationPipeline().Prepare(surface, 960, 600, new UiLoweringOptions(font)).PresentationFrame;
        previous = signature;
        return frame;
    }

    private static UiNode Label(string id, string text, double x, double y, double width)
    {
        return UI.Anchor(UI.Text(text, id: id, color: ColorToken.Hex(0xFFF1D4FF), size: TextSize.Sm),
            left: x, top: y, width: width, height: 20);
    }
}
