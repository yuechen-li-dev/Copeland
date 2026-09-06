using Aurelian.Graphics.Vulkan.Native2D;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Presentation;

namespace Aurelian.Machina.Graphics;

public sealed record AurelianProgrammablePanelLoweringResult(
    IReadOnlyList<NativeQuadSubmission> Quads,
    IReadOnlyList<MachinaPanelResolvedSegment> Segments,
    IReadOnlyList<MachinaPanelEdgeAllocation> EdgeAllocations,
    IReadOnlyList<MachinaPanelDiagnostic> Diagnostics);

/// <summary>
/// Realizes renderer-neutral programmable panel quads through the existing
/// ordered native 2D path. Allocation and sampling remain upstream semantics.
/// </summary>
public static class AurelianProgrammablePanelAdapter
{
    public static AurelianProgrammablePanelLoweringResult Lower(
        MachinaProgrammablePanelPrimitive primitive,
        Native2DTextureHandle texture,
        int atlasWidth,
        int atlasHeight,
        MachinaViewportTransform viewport)
    {
        MachinaPanelLoweringResult panel = MachinaProgrammablePanelLowerer.Lower(primitive);
        var result = new List<NativeQuadSubmission>(panel.Quads.Count);
        foreach (MachinaPanelQuad quad in panel.Quads)
        {
            Rect physical = viewport.ToPhysical(quad.DestinationRect);
            result.Add(new NativeQuadSubmission(
                new Native2DRect(
                    (float)physical.X,
                    (float)physical.Y,
                    (float)physical.Width,
                    (float)physical.Height),
                AurelianNineSliceAdapter.ToInsetUv(quad.SourceRect, atlasWidth, atlasHeight),
                texture,
                ToTint(primitive.Tint)));
        }

        return new AurelianProgrammablePanelLoweringResult(
            result,
            panel.Segments,
            panel.EdgeAllocations,
            panel.Diagnostics);
    }

    private static Native2DTint ToTint(ColorToken color)
    {
        const float denominator = 255f;
        return new Native2DTint(
            (byte)(color.Rgba >> 24) / denominator,
            (byte)(color.Rgba >> 16) / denominator,
            (byte)(color.Rgba >> 8) / denominator,
            (byte)color.Rgba / denominator);
    }
}
