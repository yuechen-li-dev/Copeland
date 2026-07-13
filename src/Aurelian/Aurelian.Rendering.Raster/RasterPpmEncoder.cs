using Aurelian.Rendering.Contracts.Resolved2D;

namespace Aurelian.Rendering.Raster;

/// <summary>
/// Deterministic P6 encoding for retained raster artifact contracts.
/// Alpha is intentionally omitted by the PPM format.
/// </summary>
public static class RasterPpmEncoder
{
    public static byte[] EncodeP6(RasterSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        byte[] header = System.Text.Encoding.ASCII.GetBytes($"P6\n{surface.Width} {surface.Height}\n255\n");
        Resolved2DRgbaColor[] pixels = surface.CopyPixels();
        var output = new byte[header.Length + (pixels.Length * 3)];
        Buffer.BlockCopy(header, 0, output, 0, header.Length);

        var outputIndex = header.Length;
        foreach (Resolved2DRgbaColor pixel in pixels)
        {
            output[outputIndex++] = pixel.R;
            output[outputIndex++] = pixel.G;
            output[outputIndex++] = pixel.B;
        }

        return output;
    }
}
