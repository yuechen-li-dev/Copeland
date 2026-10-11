using SkiaSharp;

/// <summary>Transparent-image ingress for the 512 path: foreground crop, black
/// alpha composite, Lanczos-3 resize and RGB normalization. Opaque images require
/// an explicitly supplied mask; this tool does not silently invent one.</summary>
internal static class Trellis2ImageInput
{
    public static float[] Prepare(string path, string outputDirectory)
    {
        using var bitmap = SKBitmap.Decode(path) ?? throw new InvalidDataException("Cannot decode image: " + path);
        if (bitmap.Width > 4096 || bitmap.Height > 4096)
        {
            throw new InvalidDataException("Image exceeds the bounded 4096x4096 input profile.");
        }
        int width = bitmap.Width;
        int height = bitmap.Height;
        var rgba = new byte[width * height * 4];
        bool transparent = false;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                SKColor color = bitmap.GetPixel(x, y);
                int first = (y * width + x) * 4;
                // Premultiplied color makes alpha-aware resampling well-defined.
                rgba[first] = (byte)((color.Red * color.Alpha + 127) / 255);
                rgba[first + 1] = (byte)((color.Green * color.Alpha + 127) / 255);
                rgba[first + 2] = (byte)((color.Blue * color.Alpha + 127) / 255);
                rgba[first + 3] = color.Alpha;
                transparent |= color.Alpha != 255;
            }
        }
        if (!transparent)
        {
            throw new InvalidDataException("TRELLIS input requires transparent background or an authored alpha mask. Background removal is not implemented.");
        }
        if (Math.Max(width, height) > 1024)
        {
            double factor = 1024.0 / Math.Max(width, height);
            int nextWidth = (int)(width * factor);
            int nextHeight = (int)(height * factor);
            rgba = Resize(rgba, width, height, nextWidth, nextHeight, 4);
            width = nextWidth;
            height = nextHeight;
        }
        int minX = width;
        int minY = height;
        int maxX = -1;
        int maxY = -1;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (rgba[(y * width + x) * 4 + 3] > 204)
                {
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }
        }
        if (maxX <= minX || maxY <= minY)
        {
            throw new InvalidDataException("Alpha mask contains no non-degenerate foreground above 80% opacity.");
        }
        int size = Math.Max(maxX - minX, maxY - minY);
        int left = (int)Math.Round((minX + maxX) * 0.5 - size / 2);
        int top = (int)Math.Round((minY + maxY) * 0.5 - size / 2);
        int right = (int)Math.Round((minX + maxX) * 0.5 + size / 2);
        int bottom = (int)Math.Round((minY + maxY) * 0.5 + size / 2);
        int cropWidth = right - left;
        int cropHeight = bottom - top;
        var rgb = new byte[cropWidth * cropHeight * 3];
        for (int y = 0; y < cropHeight; y++)
        {
            for (int x = 0; x < cropWidth; x++)
            {
                int sourceX = x + left;
                int sourceY = y + top;
                if (sourceX >= 0 && sourceY >= 0 && sourceX < width && sourceY < height)
                {
                    int source = (sourceY * width + sourceX) * 4;
                    int destination = (y * cropWidth + x) * 3;
                    byte alpha = rgba[source + 3];
                    for (int channel = 0; channel < 3; channel++)
                    {
                        // Pillow returns straight RGBA after its alpha-aware resize.
                        // Recreate that quantization, then the upstream FP32 black
                        // composite and truncating uint8 conversion.
                        int straight = alpha == 0 ? 0 : Math.Min(255, rgba[source + channel] * 255 / alpha);
                        float value = straight / 255f * (alpha / 255f);
                        rgb[destination + channel] = (byte)(value * 255);
                    }
                }
            }
        }
        byte[] resized = Resize(rgb, cropWidth, cropHeight, 512, 512, 3);
        SaveRgb(resized, Path.Combine(outputDirectory, "encoder-input.png"));
        File.WriteAllBytes(Path.Combine(outputDirectory, "encoder-input.rgb"), resized);
        float[] mean = [0.485f, 0.456f, 0.406f];
        float[] std = [0.229f, 0.224f, 0.225f];
        var patches = new float[1024 * 768];
        for (int patch = 0; patch < 1024; patch++)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                for (int y = 0; y < 16; y++)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        int imageX = patch % 32 * 16 + x;
                        int imageY = patch / 32 * 16 + y;
                        float pixel = resized[(imageY * 512 + imageX) * 3 + channel] / 255f;
                        patches[patch * 768 + channel * 256 + y * 16 + x] = (pixel - mean[channel]) / std[channel];
                    }
                }
            }
        }
        return patches;
    }

    private static byte[] Resize(byte[] input, int width, int height, int targetWidth, int targetHeight, int channels)
    {
        var horizontal = new byte[targetWidth * height * channels];
        var output = new byte[targetWidth * targetHeight * channels];
        Resample(input, horizontal, width, targetWidth, height, channels, vertical: false);
        Resample(horizontal, output, height, targetHeight, targetWidth, channels, vertical: true);
        return output;
    }

    private static void Resample(byte[] input, byte[] output, int sourceLength, int targetLength,
        int otherLength, int channels, bool vertical)
    {
        double scale = (double)sourceLength / targetLength;
        double filterScale = Math.Max(1, scale);
        for (int destination = 0; destination < targetLength; destination++)
        {
            double center = (destination + 0.5) * scale;
            int first = Math.Max(0, (int)Math.Floor(center - 3 * filterScale + 0.5));
            int end = Math.Min(sourceLength, (int)Math.Floor(center + 3 * filterScale + 0.5));
            var coefficients = new double[end - first];
            double sum = 0;
            for (int index = first; index < end; index++)
            {
                double distance = (index + 0.5 - center) / filterScale;
                double weight = Lanczos(distance);
                coefficients[index - first] = weight;
                sum += weight;
            }
            for (int other = 0; other < otherLength; other++)
            {
                for (int channel = 0; channel < channels; channel++)
                {
                    double value = 0;
                    for (int index = first; index < end; index++)
                    {
                        int offset = vertical ? index * otherLength + other : other * sourceLength + index;
                        value += input[offset * channels + channel] * coefficients[index - first] / sum;
                    }
                    int outputOffset = vertical ? destination * otherLength + other : other * targetLength + destination;
                    output[outputOffset * channels + channel] = (byte)Math.Clamp((int)Math.Floor(value + 0.5), 0, 255);
                }
            }
        }
    }

    private static double Lanczos(double distance)
    {
        if (Math.Abs(distance) < 1e-12)
        {
            return 1;
        }
        if (Math.Abs(distance) >= 3)
        {
            return 0;
        }
        double phase = Math.PI * distance;
        return Math.Sin(phase) / phase * Math.Sin(phase / 3) / (phase / 3);
    }

    private static void SaveRgb(byte[] rgb, string path)
    {
        using var bitmap = new SKBitmap(512, 512);
        for (int y = 0; y < 512; y++)
        {
            for (int x = 0; x < 512; x++)
            {
                int first = (y * 512 + x) * 3;
                bitmap.SetPixel(x, y, new SKColor(rgb[first], rgb[first + 1], rgb[first + 2]));
            }
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(path);
        data.SaveTo(file);
    }
}
