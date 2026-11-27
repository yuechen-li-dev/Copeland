using Aurelian.Machina;
using Aurelian.Rendering.Raster;
using Machina.Core.Lowering;
using Machina.Core.Nodes;
using Machina.Fonts.ReferenceRendering;
using Machina.Pipeline;

namespace Machina.Testing;

/// <summary>Renders normal Machina presentation through the deterministic CPU reference rasterizer.</summary>
public static class HeadlessPreview
{
    public static RasterSurface WritePng(UiNode declaration, int width, int height, string outputPath,
        UiLoweringOptions? options = null)
    {
        var prepared = new MachinaPresentationPipeline().Prepare(declaration, width, height, options);
        return WritePng(prepared, outputPath);
    }

    /// <summary>Writes a prepared frame, including any popup or transient widget state captured in it.</summary>
    public static RasterSurface WritePng(MachinaPreparedPresentation prepared, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var plan = MachinaPresentationTranslator.Translate(prepared.PresentationFrame);
        var surface = new AurelianCpuRasterRenderer().Render(plan).Surface;
        var pixels = surface.Pixels.Select(pixel => new Rgba32(pixel.R, pixel.G, pixel.B, pixel.A)).ToArray();
        RgbaPngWriter.Write(outputPath, new RgbaImage(surface.Width, surface.Height, pixels));
        return surface;
    }
}
