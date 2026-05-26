using Machina.Renderer.Raster.Text;

namespace Machina.Pipeline;

public sealed record MachinaRasterPipelineOptions(
    int Width,
    int Height,
    ITextRasterizer? TextRasterizer = null);
