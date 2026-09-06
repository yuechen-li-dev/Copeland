using Aurelian.Ariadne.VnDemo;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Machina.Graphics;
using Copeland.SpanAllocation;
using Machina.Layout.Geometry;
using Machina.Presentation;
using Xunit;

namespace Sunkill.Tests;

public sealed class SunkillM15Tests
{
    [Fact]
    public void GeneratedRuntimeMetadataCarriesTheFullColumnarAuthoredEdgeProgram()
    {
        VnUiSkin skin = LoadGeneratedSkin();

        Assert.Equal("RuntimeToml", skin.Atlas.AuthoringKind.ToString());
        Assert.Equal(25, skin.Atlas.Regions.Count);
        var panel = Assert.Single(skin.Atlas.ProgrammablePanels).Value;
        Assert.Equal(9, panel.Top.Segments.Count);
        Assert.Equal(9, panel.Bottom.Segments.Count);
        Assert.Equal(9, panel.Left.Segments.Count);
        Assert.Equal(9, panel.Right.Segments.Count);
        Assert.Equal(3, panel.Top.Segments[4].Weight);
        Assert.Equal(44, panel.Top.Segments[4].MinimumLength);
        Assert.Equal("Tile", panel.Top.Segments[2].Sampling.ToString());
        Assert.Equal("dialogue.top.center", panel.Top.Segments[4].RegionId);
    }

    [Theory]
    [InlineData(220, SpanAllocationStatus.Underflow)]
    [InlineData(292, SpanAllocationStatus.Exact)]
    [InlineData(800, SpanAllocationStatus.SurplusDistributed)]
    [InlineData(1200, SpanAllocationStatus.SurplusDistributed)]
    public void ProgrammablePanelResolvesNarrowExactNominalAndWideWithoutOverlap(
        int width,
        SpanAllocationStatus expectedTopStatus)
    {
        VnUiSkin skin = LoadGeneratedSkin();
        MachinaProgrammablePanelPrimitive primitive = skin.CreateProgrammable(
            "test.dialogue",
            "dialogue",
            new Rect(0, 0, width, 220));

        MachinaPanelLoweringResult result = MachinaProgrammablePanelLowerer.Lower(primitive);
        MachinaPanelEdgeAllocation top = Assert.Single(result.EdgeAllocations, edge => edge.Edge == "top");
        MachinaPanelResolvedSegment[] segments = result.Segments.Where(segment => segment.Edge == "top").ToArray();

        Assert.Equal(expectedTopStatus, top.Status);
        Assert.Equal(width - 76, top.Extent);
        AssertContiguous(segments, top.Extent);
        Assert.All(result.Quads, quad =>
        {
            Assert.True(quad.DestinationRect.Width > 0);
            Assert.True(quad.DestinationRect.Height > 0);
        });
        if (expectedTopStatus == SpanAllocationStatus.Underflow)
        {
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Edge == "top" && diagnostic.Code == "COPE-SPAN-ALLOC-0100");
        }
    }

    [Fact]
    public void AurelianRealizesAllocatorResolvedQuadsThroughTheExistingNativeContract()
    {
        VnUiSkin skin = LoadGeneratedSkin();
        MachinaProgrammablePanelPrimitive primitive = skin.CreateProgrammable(
            "test.native",
            "dialogue",
            new Rect(40, 470, 1200, 220));

        AurelianProgrammablePanelLoweringResult result = AurelianProgrammablePanelAdapter.Lower(
            primitive,
            new Native2DTextureHandle(9),
            skin.Atlas.Width,
            skin.Atlas.Height,
            MachinaViewportTransform.Create(1280, 720, 1537, 864));

        Assert.NotEmpty(result.Quads);
        Assert.All(result.Quads, quad =>
        {
            Assert.Equal(new Native2DTextureHandle(9), quad.Texture);
            Assert.InRange(quad.Uv.U0, 0, 1);
            Assert.InRange(quad.Uv.V0, 0, 1);
            Assert.InRange(quad.Uv.U1, 0, 1);
            Assert.InRange(quad.Uv.V1, 0, 1);
        });
        Assert.All(result.EdgeAllocations, edge => Assert.NotEqual(SpanAllocationStatus.Rejected, edge.Status));
    }

    private static VnUiSkin LoadGeneratedSkin()
    {
        return VnUiSkin.Load(Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "Integrations",
            "Aurelian.Ariadne.VnDemo",
            "Assets",
            "sunkill-dialogue-panel.runtime.toml"));
    }

    private static void AssertContiguous(IReadOnlyList<MachinaPanelResolvedSegment> segments, int extent)
    {
        int next = 0;
        foreach (MachinaPanelResolvedSegment segment in segments)
        {
            Assert.Equal(next, segment.Offset);
            Assert.True(segment.Length >= 0);
            next += segment.Length;
        }

        Assert.Equal(extent, next);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Copeland.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
