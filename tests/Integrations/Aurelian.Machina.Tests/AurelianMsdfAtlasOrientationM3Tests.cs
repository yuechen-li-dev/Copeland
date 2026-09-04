using Machina.Fonts;
using Machina.Presentation;
using Aurelian.Graphics.Vulkan.Native2D;
using Xunit;

namespace Aurelian.Machina.Tests;

public sealed class AurelianMsdfAtlasOrientationM3Tests
{
    [Fact]
    public void AtlasResourceRejectsUnspecifiedRowOrder()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AurelianMsdfAtlasResource(
            new MachinaFontAtlasId("fixture"),
            FontAtlasSnapshot.Empty,
            new Dictionary<int, byte[]>(),
            AurelianMsdfAtlasRowOrder.Unspecified));
    }

    [Fact]
    public void UploadNormalizationFlipsTopToBottomArtifactRowsExactlyOnce()
    {
        var page = new FontAtlasPage(0, "fixture", 1, 2, null);
        var snapshot = new FontAtlasSnapshot(1, [page], new Dictionary<GlyphKey, GlyphAtlasEntry>());
        byte[] topToBottom =
        [
            1, 2, 3, 4,
            5, 6, 7, 8,
        ];
        var resource = new AurelianMsdfAtlasResource(
            new MachinaFontAtlasId("fixture"),
            snapshot,
            new Dictionary<int, byte[]> { [0] = topToBottom },
            AurelianMsdfAtlasRowOrder.TopToBottom);

        byte[] native = AurelianMsdfAtlasUpload.NormalizeRows(resource, page, topToBottom);

        Assert.Equal(
        [
            5, 6, 7, 8,
            1, 2, 3, 4,
        ], native);
        Assert.Equal(1, topToBottom[0]);
    }

    [Fact]
    public void UvNormalizationMovesAndOrdersTheMatchingPackedInterval()
    {
        Native2DUvRect native = AurelianMsdfAtlasUpload.NormalizeUv(
            new Native2DUvRect(0.25f, 0.125f, 0.5f, 0.375f));

        Assert.Equal(new Native2DUvRect(0.25f, 0.625f, 0.5f, 0.875f), native);
    }
}
