using Machina.Layout.Documents;
using Machina.Layout.Geometry;

namespace Machina.ComponentGallery.Sample;

public static class GalleryDirectOutlineTextProofLayout
{
    public const string SectionId = "direct-outline-proof-section";
    public const string ProofImageSlotLeafId = "direct-outline-proof-image-slot";
    public const string ComparisonSurfaceLeafId = "direct-outline-proof-comparison-surface";
    public const string ComparisonDirectImageSlotLeafId = "direct-outline-comparison-direct-slot";
    public const string ComparisonMsdfImageSlotLeafId = "direct-outline-comparison-msdf-slot";

    public static bool TryGetProofImageSlotRect(ResolvedLayoutDocument resolved, out Rect rect)
    {
        return TryGetRect(resolved, ProofImageSlotLeafId, out rect);
    }

    public static bool TryGetComparisonDirectSlotRect(ResolvedLayoutDocument resolved, out Rect rect)
    {
        return TryGetRect(resolved, ComparisonDirectImageSlotLeafId, out rect);
    }

    public static bool TryGetComparisonSurfaceRect(ResolvedLayoutDocument resolved, out Rect rect)
    {
        return TryGetRect(resolved, ComparisonSurfaceLeafId, out rect);
    }

    public static bool TryGetComparisonMsdfSlotRect(ResolvedLayoutDocument resolved, out Rect rect)
    {
        return TryGetRect(resolved, ComparisonMsdfImageSlotLeafId, out rect);
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

public sealed record GalleryDirectOutlineTextProofPlacement(
    int ProofX,
    int ProofY,
    int ProofWidth,
    int ProofHeight,
    int ComparisonDirectX,
    int ComparisonDirectY,
    int ComparisonDirectWidth,
    int ComparisonDirectHeight,
    GalleryMsdfFontProofPlacement? ComparisonMsdfPlacement);
