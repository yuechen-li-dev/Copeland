using Machina.Renderer.Raster.Surface;

namespace Machina.Renderer.Raster.Encoding;

public static class PpmWriter
{
    public static byte[] WriteP6(RasterSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        var header = $"P6\n{surface.Width} {surface.Height}\n255\n";
        var headerBytes = System.Text.Encoding.ASCII.GetBytes(header);
        var payloadLength = surface.Width * surface.Height * 3;

        var output = new byte[headerBytes.Length + payloadLength];
        Buffer.BlockCopy(headerBytes, 0, output, 0, headerBytes.Length);

        var writeIndex = headerBytes.Length;

        for (var i = 0; i < surface.Pixels.Length; i++)
        {
            var pixel = surface.Pixels[i];
            output[writeIndex++] = pixel.R;
            output[writeIndex++] = pixel.G;
            output[writeIndex++] = pixel.B;
        }

        return output;
    }
}
