using Machina.Renderer.Raster.Encoding;
using Machina.Renderer.Raster.Surface;

namespace Machina.Renderer.Raster.Dominatus.Models;

public sealed record RasterFrame(
    int Width,
    int Height,
    RasterSurface Surface)
{
    public byte[] ToPpm()
    {
        return PpmWriter.WriteP6(Surface);
    }
}
