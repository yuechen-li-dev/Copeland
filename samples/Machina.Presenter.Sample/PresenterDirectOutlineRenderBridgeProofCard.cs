using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Standard.Authoring;
using Machina.Standard.Theme;

namespace Machina.Presenter.Sample;

public static class PresenterDirectOutlineRenderBridgeProofCard
{
    public const double Width = 500d;
    public const double Height = 708d;
    public const double ProofImageWidth = 440d;
    public const double ProofImageHeight = 384d;
    public const double AlignmentGridWidth = 440d;
    public const double AlignmentGridHeight = 152d;

    public static UiNode Build(StandardTheme theme)
    {
        UiStyle slotStyle = new(
            Background: ColorToken.Hex(0x0F172AFF),
            Padding: 0,
            Foreground: null,
            BorderColor: ColorToken.Hex(0x475569FF),
            BorderThickness: 1);

        return StandardUI.Card(
            id: "direct-outline-proof-card",
            theme: theme,
            gap: 10,
            children:
            [
                UI.Text(
                    "DirectOutlineStatic Presenter Proof",
                    id: "title",
                    size: TextSize.Md,
                    color: theme.Colors.Foreground),
                UI.Text(
                    "Render bridge proof",
                    id: "subtitle",
                    size: TextSize.Sm,
                    color: theme.Colors.MutedForeground),
                UI.Text(
                    "Static/reference backend",
                    id: "backend-status",
                    size: TextSize.Sm,
                    color: theme.Colors.MutedForeground),
                UI.Text(
                    "MSDF experimental remains opt-in",
                    id: "msdf-status",
                    size: TextSize.Sm,
                    color: theme.Colors.MutedForeground),
                UI.Rect(
                    id: PresenterDirectOutlineRenderBridgeProofLayout.ProofImageSlotLeafId,
                    width: ProofImageWidth,
                    height: ProofImageHeight,
                    style: slotStyle),
                UI.Rect(
                    id: PresenterDirectOutlineRenderBridgeProofLayout.AlignmentGridImageSlotLeafId,
                    width: AlignmentGridWidth,
                    height: AlignmentGridHeight,
                    style: slotStyle),
            ]);
    }
}
