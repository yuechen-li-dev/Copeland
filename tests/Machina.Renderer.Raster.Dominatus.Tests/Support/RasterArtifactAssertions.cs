using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Machina.Renderer.Raster.Dominatus.Tests.Support;

internal static class RasterArtifactAssertions
{
    public static string Sha256Hex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            builder.Append(b.ToString("x2"));
        }

        return builder.ToString();
    }

    public static void AssertPpmHeaderAndLength(byte[] ppm, int width, int height)
    {
        var header = $"P6\n{width} {height}\n255\n";
        var headerBytes = System.Text.Encoding.ASCII.GetBytes(header);

        Assert.True(ppm.Length >= headerBytes.Length, "PPM payload is shorter than header.");
        Assert.Equal(header, System.Text.Encoding.ASCII.GetString(ppm, 0, headerBytes.Length));

        var expectedLength = headerBytes.Length + (width * height * 3);
        Assert.Equal(expectedLength, ppm.Length);
    }

    public static void MaybeWriteArtifact(string name, byte[] ppm)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MACHINA_WRITE_RENDER_ARTIFACTS"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var outputDirectory = Path.Combine("artifacts", "render", "m0e");
        Directory.CreateDirectory(outputDirectory);

        var path = Path.Combine(outputDirectory, name + ".ppm");
        File.WriteAllBytes(path, ppm);
    }
}
