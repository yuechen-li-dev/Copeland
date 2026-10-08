using Machina.Core.Authoring;
using Machina.Core.Lowering;
using Machina.Core.Measurement;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Pipeline;
using Machina.Presentation;

namespace Aurelian.Games;

internal sealed class StarterHud(ITextMeasurer font)
{
    private string? previous;
    private MachinaPresentationFrame? frame;

    public MachinaPresentationFrame Prepare(StarterGame game)
    {
        StarterObservation state = game.Observe();
        string text = $"{state.View}   AMMO {state.Ammo}   SHOTS {state.Shots}   HITS {state.Hits}   " +
            (state.Reloading ? "RELOADING" : "LMB FIRE / R RELOAD / V VIEW / ESC MENU");
        if (previous == text && frame is not null) return frame;
        UiNode[] nodes =
        [
            UI.Anchor(UI.Rect(style: new UiStyle(Background: ColorToken.Hex(0x071520E8))), left: 16, top: 16, width: 928, height: 36),
            UI.Anchor(UI.Text(text, color: ColorToken.Hex(0xFFF1D4FF), size: TextSize.Sm), left: 32, top: 26, width: 896, height: 20),
            UI.Anchor(UI.Rect(style: new UiStyle(Background: ColorToken.Hex(0xFFF1D4FF))), left: 474, top: 299, width: 12, height: 2),
            UI.Anchor(UI.Rect(style: new UiStyle(Background: ColorToken.Hex(0xFFF1D4FF))), left: 479, top: 294, width: 2, height: 12),
        ];
        frame = new MachinaPresentationPipeline().Prepare(UI.Surface(width: 960, height: 600, children: nodes),
            960, 600, new UiLoweringOptions(font)).PresentationFrame;
        previous = text;
        return frame;
    }
}
