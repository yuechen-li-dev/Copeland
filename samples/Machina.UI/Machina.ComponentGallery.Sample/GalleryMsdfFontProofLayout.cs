using Machina.Layout.Documents;
using Machina.Layout.Geometry;

namespace Machina.ComponentGallery.Sample;

public static class GalleryMsdfFontProofLayout
{
    public const string SectionId = "msdf-proof-section";
    public const string ImageSlotLeafId = "msdf-proof-image-slot";

    public static bool TryGetImageSlotRect(ResolvedLayoutDocument resolved, out Rect rect)
    {
        foreach (KeyValuePair<Machina.Layout.Rows.NodeId, Machina.Layout.Documents.ResolvedLayoutNode> pair in resolved.Nodes)
        {
            if (pair.Key.Value.EndsWith(ImageSlotLeafId, StringComparison.Ordinal))
            {
                rect = pair.Value.Rect;
                return true;
            }
        }

        rect = default;
        return false;
    }
}

public sealed record GalleryMsdfFontProofPlacement(
    int X,
    int Y,
    int Width,
    int Height);
