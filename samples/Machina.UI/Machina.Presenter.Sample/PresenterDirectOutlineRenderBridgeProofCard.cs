using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Standard.Authoring;
using Machina.Standard.Theme;

namespace Machina.Presenter.Sample;

public static class PresenterDirectOutlineRenderBridgeProofCard
{
    public const double MinimumWidth = 500d;
    public const double Height = 708d;
    public const double ProofImageWidth = 440d;
    public const double ProofImageHeight = 384d;
    public const double AlignmentGridWidth = 440d;
    public const double AlignmentGridHeight = 152d;

    public static UiNode Build(StandardTheme theme, double width)
    {
        double effectiveWidth = Math.Max(MinimumWidth, width);
        UiNode body = UI.Layer(
            id: "direct-outline-proof-card.body-layout",
            children:
            [
                UI.Anchor(
                    UI.Text(
                        "Proof surface",
                        id: "direct-outline-proof-card.proof-label",
                        size: TextSize.Sm,
                        color: theme.Colors.MutedForeground),
                    id: "direct-outline-proof-card.proof-label-slot",
                    left: 0,
                    top: 0,
                    width: 180,
                    height: 18),
                UI.Anchor(
                    UI.Rect(
                        id: PresenterDirectOutlineRenderBridgeProofLayout.ProofImageSlotLeafId,
                        width: ProofImageWidth,
                        height: ProofImageHeight,
                        style: CreateSlotStyle()),
                    id: PresenterDirectOutlineRenderBridgeProofLayout.ProofImageSlotLeafId + ".slot",
                    left: 0,
                    top: 28,
                    width: ProofImageWidth,
                    height: ProofImageHeight),
                UI.Anchor(
                    UI.Text(
                        "Alignment grid",
                        id: "direct-outline-proof-card.alignment-label",
                        size: TextSize.Sm,
                        color: theme.Colors.MutedForeground),
                    id: "direct-outline-proof-card.alignment-label-slot",
                    left: 0,
                    top: 432,
                    width: 180,
                    height: 18),
                UI.Anchor(
                    UI.Rect(
                        id: PresenterDirectOutlineRenderBridgeProofLayout.AlignmentGridImageSlotLeafId,
                        width: AlignmentGridWidth,
                        height: AlignmentGridHeight,
                        style: CreateSlotStyle()),
                    id: PresenterDirectOutlineRenderBridgeProofLayout.AlignmentGridImageSlotLeafId + ".slot",
                    left: 0,
                    top: 460,
                    width: AlignmentGridWidth,
                    height: AlignmentGridHeight),
            ]);

        return PresenterCard.BuildHostedCard(
            id: "direct-outline-proof-card",
            title: "DirectOutlineStatic Presenter Proof",
            badges:
            [
                "proof-only",
                "static/reference backend",
                "MSDF opt-in",
            ],
            body: body,
            theme: theme,
            options: new PresenterCardOptions(
                Width: effectiveWidth,
                Height: Height));
    }

    private static UiStyle CreateSlotStyle()
    {
        return new UiStyle(
            Background: ColorToken.Hex(0x0F172AFF),
            Padding: 0,
            Foreground: null,
            BorderColor: ColorToken.Hex(0x475569FF),
            BorderThickness: 1);
    }
}
