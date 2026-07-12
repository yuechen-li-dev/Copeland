using Machina.Layout.Documents;
using Machina.Layout.Geometry;

namespace Machina.ComponentGallery.Sample;

public static class GalleryDirectOutlineRenderBridgeProofLayout
{
    public const string SectionId = "direct-outline-render-bridge-proof-section";
    public const string ProofImageSlotLeafId = "direct-outline-render-bridge-proof-image-slot";
    public const string AlignmentGridImageSlotLeafId = "direct-outline-render-bridge-layout-grid-slot";

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

public sealed record GalleryDirectOutlineRenderBridgeProofPlacement(
    int ProofX,
    int ProofY,
    int ProofWidth,
    int ProofHeight,
    int AlignmentGridX,
    int AlignmentGridY,
    int AlignmentGridWidth,
    int AlignmentGridHeight);
