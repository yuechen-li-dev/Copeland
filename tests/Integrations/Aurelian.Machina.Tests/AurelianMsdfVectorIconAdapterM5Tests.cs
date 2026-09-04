using Aurelian.Graphics.Vulkan.Native2D;
using Machina.Core.Styling;
using Machina.Core.Assets;
using Machina.Layout.Geometry;
using Machina.Presentation;
using Machina.VectorAssets;
using Xunit;

namespace Aurelian.Machina.Tests;

public sealed class AurelianMsdfVectorIconAdapterM5Tests
{
    [Fact]
    public void Adapter_ProducesOneContainedQuadWithTintAndExactAtlasUvs()
    {
        VectorIconMsdfArtifact folder = VectorIconFixtures.CompileCanonical()["Folder"];
        VectorIconAtlas atlas = VectorIconAtlasPacker.Pack([folder]);
        var primitive = new MachinaVectorIconPresentationPrimitive(
            "folder",
            folder.Identity,
            new Rect(10, 20, 100, 100),
            ColorToken.Hex(0x3366CC80));

        NativeMsdfQuadSubmission submission = AurelianMsdfVectorIconAdapter.Adapt(
            primitive,
            atlas,
            new Native2DTextureHandle(7));

        Assert.Equal(7UL, submission.AtlasTexture.Value);
        Assert.Equal(0x33 / 255f, submission.Color.Red, 6);
        Assert.Equal(0x66 / 255f, submission.Color.Green, 6);
        Assert.Equal(0xCC / 255f, submission.Color.Blue, 6);
        Assert.Equal(0x80 / 255f, submission.Color.Alpha, 6);
        Assert.True(submission.Destination.Width > submission.Destination.Height);
        Assert.Equal((float)atlas.Entries[folder.Identity].U0, submission.Uv.U0);
        Assert.Equal(1f - (float)atlas.Entries[folder.Identity].V1, submission.Uv.V0);
        Assert.True(submission.Msdf.PixelRange > 0);
        Assert.True(submission.Msdf.FieldScale > 0);
    }

    [Fact]
    public void Adapter_ClipsDestinationAndUvsTogether()
    {
        VectorIconMsdfArtifact play = VectorIconFixtures.CompileCanonical()["Play"];
        VectorIconAtlas atlas = VectorIconAtlasPacker.Pack([play]);
        var unclipped = new MachinaVectorIconPresentationPrimitive(
            "play",
            play.Identity,
            new Rect(0, 0, 32, 32),
            ColorToken.White);
        NativeMsdfQuadSubmission whole = AurelianMsdfVectorIconAdapter.Adapt(unclipped, atlas, new Native2DTextureHandle(1));
        var clipped = unclipped with { };
        clipped = new MachinaVectorIconPresentationPrimitive(
            clipped.SourceId,
            clipped.Icon,
            clipped.DestinationRect,
            clipped.Tint,
            new Rect(16, 0, 16, 32));

        NativeMsdfQuadSubmission half = AurelianMsdfVectorIconAdapter.Adapt(clipped, atlas, new Native2DTextureHandle(1));

        Assert.True(half.Destination.Width < whole.Destination.Width);
        Assert.True(half.Uv.U0 > whole.Uv.U0);
        Assert.True(half.Uv.U1 < whole.Uv.U1);
        Assert.True(half.Uv.U1 > half.Uv.U0);
    }

    [Fact]
    public void Adapter_RejectsMissingIdentityAndInvalidTexture()
    {
        VectorIconMsdfArtifact play = VectorIconFixtures.CompileCanonical()["Play"];
        VectorIconAtlas atlas = VectorIconAtlasPacker.Pack([play]);
        var missing = new MachinaVectorIconPresentationPrimitive(
            "missing",
            new MachinaVectorIconId("missing"),
            new Rect(0, 0, 24, 24),
            ColorToken.White);

        Assert.Throws<ArgumentOutOfRangeException>(() => AurelianMsdfVectorIconAdapter.Adapt(missing, atlas, default));
        Assert.Throws<InvalidOperationException>(() => AurelianMsdfVectorIconAdapter.Adapt(missing, atlas, new Native2DTextureHandle(1)));
    }
}
