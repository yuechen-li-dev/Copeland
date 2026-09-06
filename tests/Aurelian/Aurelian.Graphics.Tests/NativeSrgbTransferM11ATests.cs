using Aurelian.Graphics.Vulkan.Native2D;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class NativeSrgbTransferM11ATests
{
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.04045f, 0.0031308f)]
    [InlineData(0.5f, 0.21404114f)]
    [InlineData(1f, 1f)]
    public void Decodes_Iec_Srgb_To_Linear(float encoded, float expected)
    {
        Assert.Equal(expected, NativeSrgbTransfer.Decode(encoded), 6);
    }

    [Fact]
    public void Decoding_Color_Preserves_Straight_Alpha()
    {
        Native2DTint decoded = NativeSrgbTransfer.Decode(new Native2DTint(0.5f, 0.25f, 0.75f, 0.4f));

        Assert.Equal(0.4f, decoded.Alpha);
        Assert.Equal(0.21404114f, decoded.Red, 6);
    }

    [Fact]
    public void Native_Pipelines_Declare_Authored_Srgb_Inputs_By_Default()
    {
        Assert.True(Native2DPipelineOptions.SpriteNearest.InputsAreSrgb);
        Assert.True(Native2DPipelineOptions.AnalyticShape2D.InputsAreSrgb);
        Assert.True(Native2DPipelineOptions.MsdfText.InputsAreSrgb);
    }
}
