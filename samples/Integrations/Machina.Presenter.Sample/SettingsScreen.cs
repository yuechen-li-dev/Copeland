using Machina.Core.Authoring;
using Machina.Core.Flat;
using Machina.Standard.Theme;

namespace Machina.Presenter.Sample;

public static class SettingsScreen
{
    public const int RootWidth = 640;
    public const int RootHeight = 360;
    public const int ProofSectionTop = 336;
    public const int ProofSectionLeft = 72;
    public const int ProofSectionWidth = (int)PresenterDirectOutlineRenderBridgeProofCard.MinimumWidth;
    public const int ProofSectionHeight = (int)PresenterDirectOutlineRenderBridgeProofCard.Height;
    public const int ExpandedRootHeight = ProofSectionTop + ProofSectionHeight + 24;

    public static UiDocument Build(DemoState state, StandardTheme? theme = null)
    {
        return Build(state, theme, new PresenterProofOptions());
    }

    public static UiDocument Build(
        DemoState state,
        StandardTheme? theme,
        PresenterProofOptions proofOptions)
    {
        var effectiveTheme = theme ?? StandardTheme.Default;
        List<Machina.Core.Flat.UiRow> rows =
        [
            Row.Root(
                id: "root",
                view: View.Rect(background: effectiveTheme.Colors.Background)),

            Row.Anchor(
                id: "settings-card",
                parent: "root",
                left: 72,
                top: 24,
                width: 500,
                height: 292,
                component: SettingsCard.Build(state, effectiveTheme)),
        ];

        if (proofOptions.IncludeDirectOutlineRenderBridgeProof)
        {
            rows.Add(
                Row.Anchor(
                    id: PresenterDirectOutlineRenderBridgeProofLayout.SectionId,
                    parent: "root",
                    left: ProofSectionLeft,
                    top: ProofSectionTop,
                    width: ProofSectionWidth,
                    height: ProofSectionHeight,
                    component: PresenterDirectOutlineRenderBridgeProofCard.Build(effectiveTheme, ProofSectionWidth)));
        }

        return UiDocument.Create(rows);
    }

    public static int GetWidth(PresenterProofOptions proofOptions)
    {
        return RootWidth;
    }

    public static int GetHeight(PresenterProofOptions proofOptions)
    {
        return proofOptions.IncludeDirectOutlineRenderBridgeProof
            ? ExpandedRootHeight
            : RootHeight;
    }
}
