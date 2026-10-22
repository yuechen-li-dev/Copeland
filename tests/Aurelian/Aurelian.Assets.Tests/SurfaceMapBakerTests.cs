using System.Numerics;
using Aurelian.Assets.Materials;
using Xunit;
using SkiaSharp;

namespace Aurelian.Assets.Tests;

public sealed class SurfaceMapBakerTests
{
    [Fact]
    public void FlatHeightProducesFlatNormalsAndGltfChannelPacking()
    {
        var maps = SurfaceMapBaker.Bake("flat", 16, 16, new(2, 3),
            _ => new(0, new(.21404114f), .4f, .7f, .8f));
        Assert.Equal(new byte[] { 128, 128, 255, 255 }, maps.Normal.Rgba.Take(4).ToArray());
        Assert.Equal(new byte[] { 204, 102, 178, 255 }, maps.Orm.Rgba.Take(4).ToArray());
        Assert.Equal(128, maps.BaseColor.Rgba[0]);
        Assert.Equal(0, maps.HeightRangeMetres);
        var material = maps.Material("surface");
        material.Validate();
        Assert.Same(material.OcclusionTexture!.Texture, material.MetallicRoughnessTexture!.Texture);
    }

    [Fact]
    public void PeriodicHeightNormalsMatchIndependentDerivativesIncludingTheWrap()
    {
        const int size = 64;
        const float amplitude = .01f;
        var maps = SurfaceMapBaker.Bake("wave", size, size, new(2, 3), uv =>
            new(amplitude * MathF.Sin(MathF.Tau * uv.X) + amplitude * MathF.Cos(MathF.Tau * uv.Y), new(.2f), .5f));
        foreach (int y in new[] { 0, 7, 32, 63 })
        {
            foreach (int x in new[] { 0, 11, 32, 63 })
            {
                float u = (x + .5f) / size;
                float v = (y + .5f) / size;
                float finiteDifference = MathF.Sin(MathF.Tau / size) * size;
                float du = amplitude * MathF.Cos(MathF.Tau * u) * finiteDifference / 2;
                float dv = -amplitude * MathF.Sin(MathF.Tau * v) * finiteDifference / 3;
                Vector3 expected = Vector3.Normalize(new(-du, -dv, 1));
                int first = (y * size + x) * 4;
                Vector3 actual = new(maps.Normal.Rgba[first], maps.Normal.Rgba[first + 1], maps.Normal.Rgba[first + 2]);
                actual = actual / 255 * 2 - Vector3.One;
                Assert.InRange(Vector3.Distance(expected, actual), 0, .007f);
            }
        }
        var twiceAsLarge = SurfaceMapBaker.Bake("wave", size, size, new(4, 6), uv =>
            new(amplitude * MathF.Sin(MathF.Tau * uv.X) + amplitude * MathF.Cos(MathF.Tau * uv.Y), new(.2f), .5f));
        Assert.NotEqual(maps.Normal.Identity, twiceAsLarge.Normal.Identity);
    }

    [Fact]
    public void RecipesAreDeterministicAndReactToSeedAndWetness()
    {
        var first = ProceduralSurfaceMaps.Asphalt("road", seed: uint.MaxValue, resolution: 64);
        var replay = ProceduralSurfaceMaps.Asphalt("road", seed: uint.MaxValue, resolution: 64);
        Assert.Equal(first.Normal.Rgba.ToArray(), replay.Normal.Rgba.ToArray());
        Assert.Equal(first.Orm.Identity, replay.Orm.Identity);
        var changed = ProceduralSurfaceMaps.Asphalt("road", seed: 3, resolution: 64);
        Assert.NotEqual(first.Normal.Identity, changed.Normal.Identity);
        var dry = ProceduralSurfaceMaps.Asphalt("road", wetness: 0, seed: uint.MaxValue, resolution: 64);
        Assert.Equal(first.Normal.Rgba.ToArray(), dry.Normal.Rgba.ToArray());
        Assert.True(MeanGreen(first) < MeanGreen(dry));
        ProceduralSurfaceMaps.Masonry("brick", resolution: 64).Material("brick").Validate();
        ProceduralSurfaceMaps.Pavers("stone", resolution: 64).Material("stone").Validate();

        static double MeanGreen(SurfaceMapSet maps) => maps.Orm.Rgba.Where((_, index) => index % 4 == 1).Average(item => item);
    }

    [Fact]
    public void ExportPreservesLinearDataBytesAndRejectsMalformedImagesBeforeWriting()
    {
        var maps = SurfaceMapBaker.Bake("flat", 16, 16, Vector2.One, _ => new(0, new(.2f), .4f));
        string directory = Path.Combine(AppContext.BaseDirectory, "surface-map-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            SurfaceMapFiles.Write(directory, "flat", maps);
            using var image = SKBitmap.Decode(Path.Combine(directory, "flat-normal.png"));
            Assert.Equal(new SKColor(128, 128, 255, 255), image.GetPixel(0, 0));
            var invalid = maps with { Height = maps.Height with { Rgba = [0, 0, 0, 255] } };
            Assert.Throws<InvalidDataException>(() => SurfaceMapFiles.Write(directory, "invalid", invalid));
            Assert.False(File.Exists(Path.Combine(directory, "invalid-base-color.png")));
            Assert.Throws<ArgumentException>(() => SurfaceMapFiles.Write(directory, "../escape", maps));
        }
        finally
        {
            if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); }
        }
    }

    [Fact]
    public void InvalidInputsFailInsteadOfPublishingMaps()
    {
        Assert.Throws<ArgumentException>(() => SurfaceMapBaker.Bake("bad", 2048, 16, Vector2.One, _ => default));
        Assert.Throws<ArgumentException>(() => SurfaceMapBaker.Bake("bad", 16, 16, Vector2.Zero, _ => default));
        Assert.Throws<ArgumentException>(() => SurfaceMapBaker.Bake("bad", 16, 16, Vector2.One, _ => new(float.NaN, Vector3.One, .5f)));
        Assert.Throws<ArgumentException>(() => ProceduralSurfaceMaps.Asphalt("bad", wetness: 2));
    }
}
