namespace Machina.Fonts.ReferenceRendering;

public static class PpmImageWriter
{
    public static void Write(string path, RgbaImage image)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(image);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, BuildBytes(image));
    }

    public static byte[] BuildBytes(RgbaImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        byte[] header = System.Text.Encoding.ASCII.GetBytes($"P6\n{image.Width} {image.Height}\n255\n");
        byte[] bytes = new byte[checked(header.Length + (image.Width * image.Height * 3))];
        Buffer.BlockCopy(header, 0, bytes, 0, header.Length);

        int offset = header.Length;
        foreach (Rgba32 pixel in image.Pixels)
        {
            bytes[offset++] = pixel.R;
            bytes[offset++] = pixel.G;
            bytes[offset++] = pixel.B;
        }

        return bytes;
    }
}
