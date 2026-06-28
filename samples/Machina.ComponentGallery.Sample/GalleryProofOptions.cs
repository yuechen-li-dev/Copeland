namespace Machina.ComponentGallery.Sample;

public sealed record GalleryProofOptions(
    bool IncludeDirectOutlineTextProof = false,
    bool IncludeMsdfFontProof = false)
{
    public bool HasAnyProof => IncludeDirectOutlineTextProof || IncludeMsdfFontProof;
}
