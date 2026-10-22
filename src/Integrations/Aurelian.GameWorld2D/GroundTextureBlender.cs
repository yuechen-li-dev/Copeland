using System.Numerics;

namespace Aurelian.GameWorld2D;

public enum GroundTextureBlend
{
    Linear,
    Multiscale,
}

/// <summary>Cold, linear-light material composition. Geometry and gameplay masks are supplied by their owners.</summary>
public static class GroundTextureBlender
{
    public static byte[] Blend(int width, int height, Vector3[] ground, Vector3[] material, float[] coverage,
        GroundTextureBlend method)
    {
        int count = checked(width * height);
        if (width < 1 || height < 1 || ground.Length != count || material.Length != count || coverage.Length != count
            || !Enum.IsDefined(method) || coverage.Any(value => !float.IsFinite(value) || value is < 0 or > 1)
            || ground.Concat(material).Any(color => !Valid(color)))
        {
            throw new ArgumentException("Ground composition requires matching finite linear RGB images and coverage in [0,1].");
        }
        var result = new Vector3[count];
        if (method == GroundTextureBlend.Linear)
        {
            for (int pixel = 0; pixel < count; pixel++)
            {
                result[pixel] = Vector3.Lerp(ground[pixel], material[pixel], coverage[pixel]);
            }
        }
        else
        {
            // Undecimated Gaussian/Laplacian pyramid: fine coverage stays sharp while broad color blends smoothly.
            // This CPU cache implementation is not the paper's inline GPU mipmap approximation.
            var groundLevels = new List<Vector3[]> { ground };
            var materialLevels = new List<Vector3[]> { material };
            var maskLevels = new List<Vector3[]> { coverage.Select(value => new Vector3(value)).ToArray() };
            foreach (int step in new[] { 1, 4, 12 })
            {
                groundLevels.Add(Blur(groundLevels[^1], width, height, step));
                materialLevels.Add(Blur(materialLevels[^1], width, height, step));
                maskLevels.Add(Blur(maskLevels[^1], width, height, step));
            }
            for (int pixel = 0; pixel < count; pixel++)
            {
                Vector3 color = Vector3.Lerp(groundLevels[3][pixel], materialLevels[3][pixel], maskLevels[3][pixel].X);
                for (int level = 2; level >= 0; level--)
                {
                    Vector3 baseDetail = groundLevels[level][pixel] - groundLevels[level + 1][pixel];
                    Vector3 materialDetail = materialLevels[level][pixel] - materialLevels[level + 1][pixel];
                    color += Vector3.Lerp(baseDetail, materialDetail, maskLevels[level][pixel].X);
                }
                result[pixel] = color;
            }
        }
        byte[] rgba = new byte[count * 4];
        for (int pixel = 0; pixel < count; pixel++)
        {
            rgba[pixel * 4] = Encode(result[pixel].X);
            rgba[pixel * 4 + 1] = Encode(result[pixel].Y);
            rgba[pixel * 4 + 2] = Encode(result[pixel].Z);
            rgba[pixel * 4 + 3] = 255;
        }
        return rgba;
    }

    private static Vector3[] Blur(Vector3[] source, int width, int height, int step)
    {
        int[] weights = [1, 4, 6, 4, 1];
        var temporary = new Vector3[source.Length];
        var result = new Vector3[source.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector3 sum = Vector3.Zero;
                for (int tap = -2; tap <= 2; tap++)
                {
                    int sampleX = Math.Clamp(x + tap * step, 0, width - 1);
                    sum += source[y * width + sampleX] * weights[tap + 2];
                }
                temporary[y * width + x] = sum / 16;
            }
        }
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector3 sum = Vector3.Zero;
                for (int tap = -2; tap <= 2; tap++)
                {
                    int sampleY = Math.Clamp(y + tap * step, 0, height - 1);
                    sum += temporary[sampleY * width + x] * weights[tap + 2];
                }
                result[y * width + x] = sum / 16;
            }
        }
        return result;
    }

    private static bool Valid(Vector3 color)
    {
        return float.IsFinite(color.X) && float.IsFinite(color.Y) && float.IsFinite(color.Z)
            && color.X is >= 0 and <= 1 && color.Y is >= 0 and <= 1 && color.Z is >= 0 and <= 1;
    }

    public static float Decode(byte value)
    {
        float channel = value / 255f;
        return channel <= .04045f ? channel / 12.92f : MathF.Pow((channel + .055f) / 1.055f, 2.4f);
    }

    private static byte Encode(float value)
    {
        value = Math.Clamp(value, 0, 1);
        float encoded = value <= .0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1 / 2.4f) - .055f;
        return (byte)Math.Clamp((int)MathF.Round(encoded * 255), 0, 255);
    }
}
