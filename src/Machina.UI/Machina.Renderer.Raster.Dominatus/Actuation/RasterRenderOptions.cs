using Machina.Renderer.Raster.Text;

namespace Machina.Renderer.Raster.Dominatus.Actuation;

public sealed record RasterRenderOptions(
    ITextRasterizer? TextRasterizer = null,
    bool FailOnUnsupportedCommands = true);
