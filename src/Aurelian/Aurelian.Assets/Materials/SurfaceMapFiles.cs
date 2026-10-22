using System.Runtime.InteropServices;
using System.Text.Json;
using Aurelian.Rendering.Contracts.Models;
using SkiaSharp;

namespace Aurelian.Assets.Materials;

/// <summary>Explicit authoring export. PNG data channels retain their byte values; metadata carries units and conventions.</summary>
public static class SurfaceMapFiles
{
    public static void Write(string directory, string name, SurfaceMapSet maps)
    {
        ArgumentNullException.ThrowIfNull(maps);
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains('/') || name.Contains('\\'))
        {
            throw new ArgumentException("AUR-SURFACE-004: Export name must be one file-name component.");
        }
        ModelTexture[] textures = [maps.BaseColor, maps.Normal, maps.Orm, maps.Height];
        foreach (ModelTexture texture in textures)
        {
            // Validate every public record before any unmanaged pixel copy or file publication.
            new ModelMaterial("surface-map") { BaseColorTexture = new(texture, new()) }.Validate();
        }
        if (!float.IsFinite(maps.TileSizeMetres.X) || maps.TileSizeMetres.X <= 0
            || !float.IsFinite(maps.TileSizeMetres.Y) || maps.TileSizeMetres.Y <= 0
            || !float.IsFinite(maps.HeightMinimumMetres) || !float.IsFinite(maps.HeightRangeMetres) || maps.HeightRangeMetres < 0)
        {
            throw new ArgumentException("AUR-SURFACE-004: Export requires finite tile size and height metadata.");
        }
        Directory.CreateDirectory(directory);
        WriteImage("base-color", maps.BaseColor);
        WriteImage("normal", maps.Normal);
        WriteImage("orm", maps.Orm);
        WriteImage("height", maps.Height);
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(new
        {
            TileMetres = new[] { maps.TileSizeMetres.X, maps.TileSizeMetres.Y },
            maps.HeightMinimumMetres, maps.HeightRangeMetres,
            NormalConvention = "tangent-space +Y; linear data",
            OrmChannels = "R=cavity occlusion, G=roughness, B=metallic; linear data",
            HeightUsage = "normalized preview/export only; no displacement or parallax",
            Textures = textures.Select(texture => new
            {
                texture.Identity, texture.Width, texture.Height,
            }),
        }, new JsonSerializerOptions { WriteIndented = true }));

        void WriteImage(string channel, ModelTexture texture)
        {
            using var bitmap = new SKBitmap(new SKImageInfo(texture.Width, texture.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
            Marshal.Copy(texture.Rgba.ToArray(), 0, bitmap.GetPixels(), texture.Rgba.Length);
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream file = File.Create(Path.Combine(directory, name + "-" + channel + ".png"));
            data.SaveTo(file);
        }
    }
}
