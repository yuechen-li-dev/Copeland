using System.Text;
using Aurelian.Assets.Lighting;
using Aurelian.Rendering.Contracts.Models;
using Xunit;

namespace Aurelian.Assets.Tests;

public sealed class EnvironmentLightingTests
{
    [Fact]
    public void ArtifactRoundTripsAndRejectsCorruption()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".aenv");
        try
        {
            float[] data = new float[EnvironmentLighting.Size * EnvironmentLighting.Size * EnvironmentLighting.Bands * 4];
            Array.Fill(data, .25f);
            var asset = new EnvironmentLighting(data, "source");
            EnvironmentLightingAsset.Save(path, asset);
            Assert.Equal(asset.ContentKey, EnvironmentLightingAsset.Load(path).ContentKey);
            byte[] bytes = File.ReadAllBytes(path);
            bytes[^4] ^= 1;
            File.WriteAllBytes(path, bytes);
            Assert.Throws<InvalidDataException>(() => EnvironmentLightingAsset.Load(path));
            File.WriteAllBytes(path, []);
            Assert.Contains("AUR-ENV-001", Assert.Throws<InvalidDataException>(() => EnvironmentLightingAsset.Load(path)).Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RadianceScanlineImportsLinearHdrAndRejectsAnOverrun()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hdr");
        try
        {
            using (var file = File.Create(path))
            using (var writer = new BinaryWriter(file, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 1 +X 8\n"));
                writer.Write(new byte[] { 2, 2, 0, 8, 136, 128, 136, 64, 136, 32, 136, 130 });
            }
            var source = RadianceEnvironmentImporter.Load(path);
            Assert.Equal(8, source.Width);
            Assert.Equal(2, source.Rgba[0]);
            Assert.Equal(1, source.Rgba[1]);
            Assert.Equal(.5f, source.Rgba[2]);
            byte[] corrupted = File.ReadAllBytes(path);
            corrupted[^8] = 137;
            File.WriteAllBytes(path, corrupted);
            Assert.Throws<InvalidDataException>(() => RadianceEnvironmentImporter.Load(path));
            corrupted[^8] = 136;
            File.WriteAllBytes(path, corrupted[..^1]);
            Assert.Contains("AUR-ENV-002", Assert.Throws<InvalidDataException>(() => RadianceEnvironmentImporter.Load(path)).Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
