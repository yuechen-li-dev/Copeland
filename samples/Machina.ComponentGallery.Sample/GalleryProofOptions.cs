namespace Machina.ComponentGallery.Sample;

public sealed record GalleryProofOptions(
    bool IncludeDirectOutlineTextProof = false,
    bool IncludeDirectOutlineRenderBridgeProof = false,
    bool IncludeDirectOutlineTextLayoutProof = false,
    bool IncludeMsdfFontProof = false)
{
    public bool HasAnyProof => IncludeDirectOutlineTextProof
        || IncludeDirectOutlineRenderBridgeProof
        || IncludeDirectOutlineTextLayoutProof
        || IncludeMsdfFontProof;
}
