using Machina.Core.Flat;
using Machina.Standard.Theme;

namespace Machina.ComponentGallery.Sample;

public static class GalleryScreen
{
    public const int Width = 960;
    public const int Height = 1000;
    public const int DirectOutlineProofSectionHeight = 1080;
    public const int DirectOutlineRenderBridgeProofSectionHeight = 1148;
    public const int DirectOutlineTextLayoutProofSectionHeight = 1248;
    public const int MsdfProofSectionHeight = 208;
    private const int ProofSectionTop = 776;
    private const int ProofSectionGap = 24;

    public static UiDocument Build(GalleryState state, bool includeMsdfFontProof = false, StandardTheme? theme = null)
    {
        return Build(state, new GalleryProofOptions(IncludeMsdfFontProof: includeMsdfFontProof), theme);
    }

    public static UiDocument Build(GalleryState state, GalleryProofOptions proofOptions, StandardTheme? theme = null)
    {
        var effectiveTheme = theme ?? StandardTheme.Default;
        List<Machina.Core.Flat.UiRow> rows =
        [
            Row.Root(
                id: "root",
                view: View.Rect(background: effectiveTheme.Colors.Background)),

            Row.Anchor(
                id: "header",
                parent: "root",
                left: 24,
                top: 24,
                width: 912,
                height: 54,
                component: GallerySections.Header(state, effectiveTheme)),

            Row.Anchor(
                id: "text-section",
                parent: "root",
                left: 24,
                top: 96,
                width: 288,
                height: 300,
                component: GallerySections.TextSection(effectiveTheme)),

            Row.Anchor(
                id: "badges-section",
                parent: "root",
                left: 24,
                top: 420,
                width: 288,
                height: 156,
                component: GallerySections.BadgesSection(effectiveTheme)),

            Row.Anchor(
                id: "actions-section",
                parent: "root",
                left: 24,
                top: 584,
                width: 288,
                height: 168,
                component: GallerySections.ActionsSection(state, effectiveTheme)),

            Row.Anchor(
                id: "buttons-section",
                parent: "root",
                left: 336,
                top: 96,
                width: 288,
                height: 152,
                component: GallerySections.ButtonsSection(state, effectiveTheme)),

            Row.Anchor(
                id: "selection-section",
                parent: "root",
                left: 336,
                top: 272,
                width: 288,
                height: 320,
                component: GallerySections.SelectionSection(state, effectiveTheme)),

            Row.Anchor(
                id: "input-section",
                parent: "root",
                left: 336,
                top: 616,
                width: 288,
                height: 152,
                component: GallerySections.InputSection(state, effectiveTheme)),

            Row.Anchor(
                id: "cards-section",
                parent: "root",
                left: 648,
                top: 96,
                width: 288,
                height: 360,
                component: GallerySections.CardsSection(effectiveTheme)),

            Row.Anchor(
                id: "theme-section",
                parent: "root",
                left: 648,
                top: 480,
                width: 288,
                height: 280,
                component: GallerySections.ThemeSection(effectiveTheme)),
        ];

        var nextTop = ProofSectionTop;

        if (proofOptions.IncludeDirectOutlineTextProof)
        {
            rows.Add(
                Row.Anchor(
                    id: GalleryDirectOutlineTextProofLayout.SectionId,
                    parent: "root",
                    left: 24,
                    top: nextTop,
                    width: 912,
                    height: DirectOutlineProofSectionHeight,
                    component: GallerySections.DirectOutlineTextProofSection(effectiveTheme, proofOptions.IncludeMsdfFontProof)));

            nextTop += DirectOutlineProofSectionHeight + ProofSectionGap;
        }

        if (proofOptions.IncludeDirectOutlineRenderBridgeProof)
        {
            rows.Add(
                Row.Anchor(
                    id: GalleryDirectOutlineRenderBridgeProofLayout.SectionId,
                    parent: "root",
                    left: 24,
                    top: nextTop,
                    width: 912,
                    height: DirectOutlineRenderBridgeProofSectionHeight,
                    component: GallerySections.DirectOutlineRenderBridgeProofSection(effectiveTheme)));

            nextTop += DirectOutlineRenderBridgeProofSectionHeight + ProofSectionGap;
        }

        if (proofOptions.IncludeDirectOutlineTextLayoutProof)
        {
            rows.Add(
                Row.Anchor(
                    id: GalleryDirectOutlineTextLayoutProofLayout.SectionId,
                    parent: "root",
                    left: 24,
                    top: nextTop,
                    width: 912,
                    height: DirectOutlineTextLayoutProofSectionHeight,
                    component: GallerySections.DirectOutlineTextLayoutProofSection(effectiveTheme)));

            nextTop += DirectOutlineTextLayoutProofSectionHeight + ProofSectionGap;
        }

        if (proofOptions.IncludeMsdfFontProof)
        {
            rows.Add(
                Row.Anchor(
                    id: GalleryMsdfFontProofLayout.SectionId,
                    parent: "root",
                    left: 24,
                    top: nextTop,
                    width: 912,
                    height: MsdfProofSectionHeight,
                    component: GallerySections.MsdfFontProofSection(effectiveTheme)));
        }

        return UiDocument.Create(rows);
    }

    public static int GetHeight(GalleryProofOptions proofOptions)
    {
        var height = Height;

        if (proofOptions.IncludeDirectOutlineTextProof)
        {
            height += DirectOutlineProofSectionHeight + ProofSectionGap;
        }

        if (proofOptions.IncludeDirectOutlineRenderBridgeProof)
        {
            height += DirectOutlineRenderBridgeProofSectionHeight + ProofSectionGap;
        }

        if (proofOptions.IncludeDirectOutlineTextLayoutProof)
        {
            height += DirectOutlineTextLayoutProofSectionHeight + ProofSectionGap;
        }

        if (proofOptions.IncludeMsdfFontProof)
        {
            height += MsdfProofSectionHeight + ProofSectionGap;
        }

        return height;
    }
}
