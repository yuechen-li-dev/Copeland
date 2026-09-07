using Aurelian.Graphics.Vulkan.Native2D;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class NativeMsdfSubmissionM2Tests
{
    [Fact]
    public void Valid_Msdf_Submission_Is_Accepted()
    {
        NativeMsdfQuadSubmission submission = CreateSubmission(NativeMsdfParameters.Create(4, 0.75f));

        Native2DSubmissionValidator.ValidateValues(submission);
    }

    [Fact]
    public void Shared_Msdf_Policy_Compensates_Only_Small_Screen_Ranges()
    {
        NativeMsdfParameters small = NativeMsdfParameters.Create(4, 0.5f);
        NativeMsdfParameters large = NativeMsdfParameters.Create(8, 1);

        Assert.InRange(small.Threshold, 0.54f, 0.55f);
        Assert.Equal(0.5f, large.Threshold);
    }

    [Theory]
    [InlineData(0, 1, 0.5)]
    [InlineData(4, 0, 0.5)]
    [InlineData(4, 1, -0.1)]
    [InlineData(4, 1, 1.1)]
    public void Invalid_Msdf_Parameters_Are_Rejected(float pixelRange, float fieldScale, float threshold)
    {
        NativeMsdfQuadSubmission submission = CreateSubmission(new NativeMsdfParameters(pixelRange, fieldScale, threshold));

        Assert.Throws<ArgumentException>(() => Native2DSubmissionValidator.ValidateValues(submission));
    }

    [Fact]
    public void Msdf_Pipeline_Uses_Linear_Filtering_And_Straight_Alpha()
    {
        Assert.True(Native2DPipelineOptions.MsdfText.LinearFiltering);
        Assert.True(Native2DPipelineOptions.MsdfText.StraightAlphaBlend);
        Assert.False(Native2DPipelineOptions.Textured.LinearFiltering);
        Assert.False(Native2DPipelineOptions.Textured.StraightAlphaBlend);
    }

    private static NativeMsdfQuadSubmission CreateSubmission(NativeMsdfParameters parameters)
    {
        return new NativeMsdfQuadSubmission(
            new Native2DRect(10, 20, 30, 40),
            new Native2DUvRect(0.1f, 0.2f, 0.3f, 0.4f),
            new Native2DTextureHandle(1),
            Native2DTint.White,
            parameters);
    }
}
