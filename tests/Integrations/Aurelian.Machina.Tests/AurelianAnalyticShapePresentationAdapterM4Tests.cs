using Aurelian.Graphics.Vulkan.Native2D;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Presentation;
using Xunit;

namespace Aurelian.Machina.Tests;

public sealed class AurelianAnalyticShapePresentationAdapterM4Tests
{
    [Fact]
    public void AdapterPreservesShapeIdentityAndClippedLocalCoordinates()
    {
        var primitive = new MachinaAnalyticShapePrimitive(
            "card",
            MachinaAnalyticShapeKind.RoundedRect,
            new Rect(10, 20, 100, 50),
            ColorToken.Hex(0x112233CC),
            12);

        NativeAnalyticShapeSubmission submission = AurelianAnalyticShapePresentationAdapter.Adapt(
            primitive,
            new Rect(35, 20, 50, 50))!.Value;

        Assert.Equal(new Native2DRect(35, 20, 50, 50), submission.Destination);
        Assert.Equal(new Native2DSize(100, 50), submission.ShapeSize);
        Assert.Equal(new Native2DUvRect(0.25f, 0, 0.75f, 1), submission.LocalCoordinates);
        Assert.Equal(NativeAnalyticShapeKind.RoundedRect, submission.Kind);
        Assert.Equal(12, submission.Radius);
    }

    [Fact]
    public void AdapterReturnsNoSubmissionForFullyClippedShape()
    {
        var primitive = new MachinaAnalyticShapePrimitive(
            "status",
            MachinaAnalyticShapeKind.Circle,
            new Rect(0, 0, 16, 16),
            ColorToken.White);

        Assert.Null(AurelianAnalyticShapePresentationAdapter.Adapt(primitive, new Rect(20, 20, 4, 4)));
    }
}
