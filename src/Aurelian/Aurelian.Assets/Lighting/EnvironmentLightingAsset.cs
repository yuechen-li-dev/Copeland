using System.Runtime.InteropServices;
using System.Text;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.Assets.Lighting;

public static class EnvironmentLightingAsset
{
    public static void Save(string path, EnvironmentLighting asset)
    {
        using var file = File.Create(path);
        using var writer = new BinaryWriter(file, Encoding.UTF8);
        writer.Write(EnvironmentLighting.Decoder);
        writer.Write(asset.SourceKey);
        writer.Write(asset.ContentKey);
        writer.Write(MemoryMarshal.AsBytes(asset.Pixels.AsSpan()));
    }

    public static EnvironmentLighting Load(string path)
    {
        try
        {
            return Read(path);
        }
        catch (Exception error) when (error is EndOfStreamException or FormatException)
        {
            throw new InvalidDataException("AUR-ENV-001: Truncated or malformed environment header.", error);
        }
    }

    private static EnvironmentLighting Read(string path)
    {
        using var file = File.OpenRead(path);
        if (file.Length > 1_000_000)
        {
            throw new InvalidDataException("AUR-ENV-001: Environment artifact exceeds its bounded storage contract.");
        }
        using var reader = new BinaryReader(file, Encoding.UTF8);
        if (reader.ReadString() != EnvironmentLighting.Decoder)
        {
            throw new InvalidDataException("AUR-ENV-001: Unsupported environment decoder.");
        }
        string source = reader.ReadString();
        string hash = reader.ReadString();
        int length = EnvironmentLighting.Size * EnvironmentLighting.Size * EnvironmentLighting.Bands * 16;
        byte[] payload = reader.ReadBytes(length);
        if (payload.Length != length || file.Position != file.Length)
        {
            throw new InvalidDataException("AUR-ENV-001: Truncated or trailing environment payload.");
        }
        var asset = new EnvironmentLighting(MemoryMarshal.Cast<byte, float>(payload).ToArray(), source);
        if (hash != asset.ContentKey)
        {
            throw new InvalidDataException("AUR-ENV-001: Environment checksum mismatch.");
        }
        return asset;
    }
}
