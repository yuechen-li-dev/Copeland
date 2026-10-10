using System.Collections.Immutable;
using System.Text;

namespace Aurelian.Assets.Lighting;

public sealed record LinearEnvironmentImage(int Width, int Height, ImmutableArray<float> Rgba);

/// <summary>Radiance RGBE equirectangular import. Orientation and unsupported encodings fail explicitly.</summary>
public static class RadianceEnvironmentImporter
{
    public static LinearEnvironmentImage Load(string path)
    {
        try
        {
            return Read(path);
        }
        catch (EndOfStreamException error)
        {
            throw new InvalidDataException("AUR-ENV-002: Truncated HDR header or scanline.", error);
        }
    }

    private static LinearEnvironmentImage Read(string path)
    {
        using var file = File.OpenRead(path);
        if (file.Length > 100_000_000)
        {
            throw new InvalidDataException("AUR-ENV-002: HDR source exceeds the import storage limit.");
        }
        using var reader = new BinaryReader(file, Encoding.ASCII);
        string signature = Line(reader);
        if (signature is not "#?RADIANCE" and not "#?RGBE")
        {
            throw new InvalidDataException("AUR-ENV-002: Expected a Radiance RGBE header.");
        }
        bool format = false;
        for (int line = 0; line < 64; line++)
        {
            string text = Line(reader);
            if (text.Length == 0)
            {
                break;
            }
            if (text == "FORMAT=32-bit_rle_rgbe")
            {
                format = true;
            }
        }
        string[] dimensions = Line(reader).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!format || dimensions.Length != 4 || dimensions[0] != "-Y" || dimensions[2] != "+X"
            || !int.TryParse(dimensions[1], out int height) || !int.TryParse(dimensions[3], out int width)
            || width < 8 || width > 4096 || height < 1 || height > 2048)
        {
            throw new InvalidDataException("AUR-ENV-002: Supported HDR layout is -Y height +X width, with modern RGBE scanlines up to 4096x2048.");
        }
        float[] rgba = new float[width * height * 4];
        byte[] scanline = new byte[width * 4];
        for (int y = 0; y < height; y++)
        {
            if (reader.ReadByte() != 2 || reader.ReadByte() != 2 || (reader.ReadByte() * 256 + reader.ReadByte()) != width)
            {
                throw new InvalidDataException("AUR-ENV-002: Unsupported legacy RGBE scanline encoding or mismatched width.");
            }
            for (int channel = 0; channel < 4; channel++)
            {
                int x = 0;
                for (int packet = 0; packet < width && x < width; packet++)
                {
                    int code = reader.ReadByte();
                    int count = code > 128 ? code - 128 : code;
                    if (count == 0 || x + count > width)
                    {
                        throw new InvalidDataException("AUR-ENV-002: Invalid HDR run length.");
                    }
                    if (code > 128)
                    {
                        byte value = reader.ReadByte();
                        for (int item = 0; item < count; item++)
                        {
                            scanline[(x + item) * 4 + channel] = value;
                        }
                    }
                    else
                    {
                        for (int item = 0; item < count; item++)
                        {
                            scanline[(x + item) * 4 + channel] = reader.ReadByte();
                        }
                    }
                    x += count;
                }
            }
            for (int x = 0; x < width; x++)
            {
                int source = x * 4;
                int target = (y * width + x) * 4;
                float scale = scanline[source + 3] == 0 ? 0 : MathF.ScaleB(1, scanline[source + 3] - 136);
                for (int channel = 0; channel < 3; channel++)
                {
                    float value = scanline[source + channel] * scale;
                    if (!float.IsFinite(value) || value > 60000)
                    {
                        throw new InvalidDataException("AUR-ENV-002: HDR radiance exceeds the finite rendering range.");
                    }
                    rgba[target + channel] = value;
                }
                rgba[target + 3] = 1;
            }
        }
        return new(width, height, rgba.ToImmutableArray());
    }

    private static string Line(BinaryReader reader)
    {
        var text = new StringBuilder();
        for (int index = 0; index < 1024; index++)
        {
            byte value = reader.ReadByte();
            if (value == 10)
            {
                return text.ToString().TrimEnd('\r');
            }
            text.Append((char)value);
        }
        throw new InvalidDataException("AUR-ENV-002: HDR header line exceeds its bound.");
    }
}
