using Machina.Fonts.ReferenceRendering;
using Xunit;

namespace Machina.Fonts.Tests.Rendering;

public sealed class PpmImageWriterTests
{
    [Fact]
    public void PpmWriter_WritesDeterministicFile()
    {
        RgbaImage image = CreateImage();
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "proof.ppm");

        PpmImageWriter.Write(path, image);
        byte[] first = File.ReadAllBytes(path);

        PpmImageWriter.Write(path, image);
        byte[] second = File.ReadAllBytes(path);

        Assert.Equal(first, second);
    }

    [Fact]
    public void PpmWriter_WritesExpectedHeader()
    {
        byte[] bytes = PpmImageWriter.BuildBytes(CreateImage());
        string header = System.Text.Encoding.ASCII.GetString(bytes, 0, "P6\n2 2\n255\n".Length);

        Assert.Equal("P6\n2 2\n255\n", header);
    }

    [Fact]
    public void PpmWriter_WritesExpectedByteLength()
    {
        byte[] bytes = PpmImageWriter.BuildBytes(CreateImage());

        Assert.Equal("P6\n2 2\n255\n".Length + (2 * 2 * 3), bytes.Length);
    }

    private static RgbaImage CreateImage()
    {
        return new RgbaImage(
            2,
            2,
            [
                new Rgba32(255, 0, 0, 255),
                new Rgba32(0, 255, 0, 255),
                new Rgba32(0, 0, 255, 255),
                new Rgba32(255, 255, 255, 255),
            ]);
    }
}
