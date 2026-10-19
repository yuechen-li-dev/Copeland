using System.Text;
using Aurelian.Assets.ArtSlots;
using SkiaSharp;
using Xunit;

namespace Aurelian.Assets.Tests;

public sealed class ArtSlotCatalogTests
{
    private const string Square = """
        <svg xmlns="http://www.w3.org/2000/svg" width="40" height="20" viewBox="0 0 40 20">
          <rect x="0" y="10" width="20" height="10" fill="#ff0000"/>
        </svg>
        """;

    [Fact]
    public void PlaceholdersRasterizeAtTheSlotSizeWithStraightAlpha()
    {
        string directory = Write(Square, ManifestFor());
        ArtSlotCatalog catalog = ArtSlotCatalog.Load(Path.Combine(directory, "art-slots.json"));
        ArtSlotImage image = catalog.Realize("prop.block", scale: 2);

        Assert.True(image.IsPlaceholder);
        Assert.Equal(80, image.Width);
        Assert.Equal(40, image.Height);
        // Bottom-left quarter is the red rectangle; the top right is transparent.
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(image, 5, 35));
        Assert.Equal(0, Pixel(image, 70, 5)[3]);
        Assert.Single(catalog.Pending);
        Assert.Contains("prop.block", catalog.PendingReport());
    }

    [Fact]
    public void FinalArtReplacesThePlaceholderOnlyWhenItsHashAndAspectMatch()
    {
        byte[] png = Png(80, 40, new SKColor(0, 128, 255));
        string hash = ArtSlotCatalog.Hash(png);
        string directory = Write(Square, ManifestFor(final: "block.png", finalHash: hash), ("block.png", png));
        ArtSlotCatalog catalog = ArtSlotCatalog.Load(Path.Combine(directory, "art-slots.json"));
        ArtSlotImage image = catalog.Realize("prop.block");
        Assert.False(image.IsPlaceholder);
        Assert.Equal(new byte[] { 0, 128, 255, 255 }, Pixel(image, 10, 10));
        Assert.Empty(catalog.Pending);

        string wrongHash = Write(Square, ManifestFor(final: "block.png", finalHash: new string('0', 64)), ("block.png", png));
        Assert.Throws<InvalidDataException>(() => ArtSlotCatalog.Load(Path.Combine(wrongHash, "art-slots.json")));

        byte[] square = Png(40, 40, SKColors.White);
        string wrongAspect = Write(Square, ManifestFor(final: "block.png", finalHash: ArtSlotCatalog.Hash(square)), ("block.png", square));
        var mismatched = ArtSlotCatalog.Load(Path.Combine(wrongAspect, "art-slots.json"));
        Assert.Contains("aspect", Assert.Throws<InvalidDataException>(() => mismatched.Realize("prop.block")).Message);
    }

    [Fact]
    public void PlaceholdersMustDeclareTheSlotCanvas()
    {
        string wrongCanvas = Square.Replace("viewBox=\"0 0 40 20\"", "viewBox=\"0 0 64 64\"").Replace("width=\"40\" height=\"20\"", "width=\"64\" height=\"64\"");
        string directory = Write(wrongCanvas, ManifestFor());
        var catalog = ArtSlotCatalog.Load(Path.Combine(directory, "art-slots.json"));
        Assert.Contains("viewBox", Assert.Throws<InvalidDataException>(() => catalog.Realize("prop.block")).Message);
    }

    private static string ManifestFor(string? final = null, string? finalHash = null)
    {
        string finalFields = final is null ? "" : $"\"final\": \"{final}\", \"finalSha256\": \"{finalHash}\",";
        return $$"""
            {
              // comments are allowed
              "schemaVersion": 1,
              "slots": [
                {
                  "id": "prop.block",
                  "placeholder": "placeholders/block.svg",
                  {{finalFields}}
                  "width": 40,
                  "height": 20,
                  "usage": "A test block.",
                  "brief": "A red block."
                }
              ]
            }
            """;
    }

    private static string Write(string svg, string manifest, params (string Name, byte[] Bytes)[] files)
    {
        string directory = Path.Combine(Path.GetTempPath(), "aurelian-art-slots", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "placeholders"));
        File.WriteAllText(Path.Combine(directory, "placeholders", "block.svg"), svg, Encoding.UTF8);
        File.WriteAllText(Path.Combine(directory, "art-slots.json"), manifest, Encoding.UTF8);
        foreach ((string name, byte[] bytes) in files)
        {
            File.WriteAllBytes(Path.Combine(directory, name), bytes);
        }
        return directory;
    }

    private static byte[] Png(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] Pixel(ArtSlotImage image, int x, int y)
    {
        int index = (y * image.Width + x) * 4;
        return image.Rgba[index..(index + 4)];
    }
}
