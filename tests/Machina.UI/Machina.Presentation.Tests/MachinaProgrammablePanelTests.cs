using Copeland.SpanAllocation;
using Machina.Layout.Geometry;
using Xunit;

namespace Machina.Presentation.Tests;

public sealed class MachinaProgrammablePanelTests
{
    [Theory]
    [InlineData(50, SpanAllocationStatus.Exact, 10, 10, 10)]
    [InlineData(80, SpanAllocationStatus.SurplusDistributed, 10, 20, 30)]
    public void EdgeAllocationResolvesExactAndWeightedSurplus(
        int width,
        SpanAllocationStatus expectedStatus,
        int fixedLength,
        int firstFlexLength,
        int secondFlexLength)
    {
        MachinaPanelLoweringResult result = MachinaProgrammablePanelLowerer.Lower(Create(width, 60));
        MachinaPanelResolvedSegment[] top = result.Segments.Where(segment => segment.Edge == "top").ToArray();

        Assert.Equal([fixedLength, firstFlexLength, secondFlexLength], top.Select(segment => segment.Length));
        Assert.Equal([0, fixedLength, fixedLength + firstFlexLength], top.Select(segment => segment.Offset));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Edge == "top");
        Assert.Equal(expectedStatus, Assert.Single(result.EdgeAllocations, edge => edge.Edge == "top").Status);
        AssertContiguous(top, width - 20);
    }

    [Fact]
    public void UnderflowIsExplicitAndProducesOnlyNonnegativeContiguousGeometry()
    {
        MachinaPanelLoweringResult result = MachinaProgrammablePanelLowerer.Lower(Create(35, 60));
        MachinaPanelResolvedSegment[] top = result.Segments.Where(segment => segment.Edge == "top").ToArray();

        Assert.Equal([10, 5, 0], top.Select(segment => segment.Length));
        Assert.Single(result.Diagnostics, diagnostic =>
            diagnostic.Edge == "top" && diagnostic.Code == "COPE-SPAN-ALLOC-0100");
        Assert.All(result.Quads, quad =>
        {
            Assert.True(quad.DestinationRect.Width > 0);
            Assert.True(quad.DestinationRect.Height > 0);
        });
        AssertContiguous(top, 15);
    }

    [Fact]
    public void CropUsesOnlyTheSourcePixelsNeededAtBorderScale()
    {
        MachinaPanelLoweringResult result = MachinaProgrammablePanelLowerer.Lower(Create(50, 60, borderScale: 0.5));
        MachinaPanelQuad fixedTop = Assert.Single(result.Quads, quad => quad.SemanticId == "top.fixed");

        Assert.Equal(10, fixedTop.DestinationRect.Width);
        Assert.Equal(20, fixedTop.SourceRect.Width);
    }

    [Fact]
    public void NineSliceIsAPrebuiltOverFourThreeSliceEdgePrograms()
    {
        var nineSlice = new MachinaNineSlicePrimitive(
            "test.nine",
            new MachinaTextureAssetId("test.atlas"),
            new Rect(0, 0, 40, 40),
            new Rect(0, 0, 100, 80),
            new MachinaSliceMargins(10, 10, 10, 10),
            MachinaNineSliceMode.Stretch,
            MachinaNineSliceMode.Stretch);

        MachinaProgrammablePanelPrimitive panel = MachinaPanelPrebuilt.NineSlice(nineSlice);
        MachinaPanelLoweringResult programmable = MachinaProgrammablePanelLowerer.Lower(panel);
        IReadOnlyList<MachinaNineSliceQuad> compatibility = MachinaNineSliceLowerer.Lower(nineSlice);

        Assert.All([panel.Top, panel.Right, panel.Bottom, panel.Left], edge =>
        {
            MachinaPanelEdgeSegment segment = Assert.Single(edge.Segments);
            Assert.Equal(SpanAllocationKind.Flex, segment.AllocationKind);
            Assert.Equal(0, segment.MinimumLength);
        });
        Assert.Equal(programmable.Quads.Count, compatibility.Count);
        Assert.Equal(
            programmable.Quads.Select(quad => (quad.DestinationRect, quad.SourceRect)),
            compatibility.Select(quad => (quad.DestinationRect, quad.SourceRect)));
    }

    private static MachinaProgrammablePanelPrimitive Create(int width, int height, double borderScale = 1)
    {
        var source = new Rect(0, 0, 20, 20);
        MachinaPanelEdgeProgram edge = new([
            new MachinaPanelEdgeSegment("fixed", source, SpanAllocationKind.Fixed, 10, 0, MachinaPanelSampling.Crop),
            new MachinaPanelEdgeSegment("flex-a", source, SpanAllocationKind.Flex, 10, 1, MachinaPanelSampling.Stretch),
            new MachinaPanelEdgeSegment("flex-b", source, SpanAllocationKind.Flex, 10, 2, MachinaPanelSampling.Stretch),
        ]);
        return new MachinaProgrammablePanelPrimitive(
            "test.programmable",
            new MachinaTextureAssetId("test.atlas"),
            new Rect(0, 0, width, height),
            new Rect(0, 0, 10, 10),
            new Rect(30, 0, 10, 10),
            new Rect(30, 30, 10, 10),
            new Rect(0, 30, 10, 10),
            edge,
            edge,
            edge,
            edge,
            MachinaPanelCenterPolicy.StretchRegion,
            new Rect(10, 10, 20, 20),
            borderScale);
    }

    private static void AssertContiguous(IReadOnlyList<MachinaPanelResolvedSegment> segments, int extent)
    {
        int offset = 0;
        foreach (MachinaPanelResolvedSegment segment in segments)
        {
            Assert.Equal(offset, segment.Offset);
            Assert.True(segment.Length >= 0);
            offset += segment.Length;
        }

        Assert.Equal(extent, offset);
    }
}
