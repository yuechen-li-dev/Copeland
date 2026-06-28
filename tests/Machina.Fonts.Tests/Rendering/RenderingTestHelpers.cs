using Machina.Fonts;
using Machina.Fonts.Generation;
using Machina.Fonts.ReferenceRendering;
using Xunit;

namespace Machina.Fonts.Tests.Rendering;

internal static class RenderingTestHelpers
{
    public static DistanceFieldPageReference CreatePage(
        DistanceFieldKind kind,
        int width,
        int height,
        Func<int, int, float[]> pixelFactory,
        string sourcePath = "synthetic.dfpage")
    {
        int channelCount = kind switch
        {
            DistanceFieldKind.Sdf => 1,
            DistanceFieldKind.Psdf => 1,
            DistanceFieldKind.Msdf => 3,
            DistanceFieldKind.Mtsdf => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        float[] data = new float[checked(width * height * channelCount)];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float[] channels = pixelFactory(x, y);
                Assert.Equal(channelCount, channels.Length);
                int offset = ((y * width) + x) * channelCount;
                Array.Copy(channels, 0, data, offset, channelCount);
            }
        }

        return new DistanceFieldPageReference(sourcePath, kind, 0, width, height, channelCount, data);
    }

    public static GlyphAtlasEntry CreateEntry(int x, int y, int width, int height, int pageWidth, int pageHeight, char value = 'A')
    {
        GlyphKey key = GlyphKey.FromChar(new FontFaceId("machina-reference-render"), value, 32);
        GlyphMetrics metrics = new(width, 0, height, width, height);
        return new GlyphAtlasEntry(
            key,
            0,
            x,
            y,
            width,
            height,
            x / (double)pageWidth,
            y / (double)pageHeight,
            (x + width) / (double)pageWidth,
            (y + height) / (double)pageHeight,
            metrics,
            GlyphFieldPlacement.CreateFromMetricsBox(metrics));
    }
}
