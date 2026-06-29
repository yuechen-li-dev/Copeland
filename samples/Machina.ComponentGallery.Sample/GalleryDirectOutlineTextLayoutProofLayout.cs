using Machina.Layout.Documents;
using Machina.Layout.Geometry;

namespace Machina.ComponentGallery.Sample;

public static class GalleryDirectOutlineTextLayoutProofLayout
{
    public const string SectionId = "direct-outline-text-layout-proof-section";
    public const string ProofImageSlotLeafId = "direct-outline-text-layout-proof-image-slot";
    public const string AlignmentGridImageSlotLeafId = "direct-outline-text-alignment-grid-slot";

    public static bool TryGetProofImageSlotRect(ResolvedLayoutDocument resolved, out Rect rect)
    {
        return TryGetRect(resolved, ProofImageSlotLeafId, out rect);
    }

    public static bool TryGetAlignmentGridImageSlotRect(ResolvedLayoutDocument resolved, out Rect rect)
    {
        return TryGetRect(resolved, AlignmentGridImageSlotLeafId, out rect);
    }

    private static bool TryGetRect(ResolvedLayoutDocument resolved, string leafId, out Rect rect)
    {
        foreach (KeyValuePair<Machina.Layout.Rows.NodeId, ResolvedLayoutNode> pair in resolved.Nodes)
        {
            if (pair.Key.Value.EndsWith(leafId, StringComparison.Ordinal))
            {
                rect = pair.Value.Rect;
                return true;
            }
        }

        rect = default;
        return false;
    }
}

public sealed record GalleryDirectOutlineTextLayoutProofPlacement(
    int ProofX,
    int ProofY,
    int ProofWidth,
    int ProofHeight,
    int AlignmentGridX,
    int AlignmentGridY,
    int AlignmentGridWidth,
    int AlignmentGridHeight);
