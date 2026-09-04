namespace Machina.Fonts.ReferenceRendering;

public sealed record InkMaskExtractionOptions(
    Rgba32 BackgroundColor,
    Rgba32 BaselineGuideColor,
    int InkDistanceThreshold = 12,
    int BaselineDistanceThreshold = 24)
{
    public InkMaskExtractionOptions Validate()
    {
        if (InkDistanceThreshold < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(InkDistanceThreshold));
        }

        if (BaselineDistanceThreshold < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(BaselineDistanceThreshold));
        }

        return this;
    }
}

public sealed record InkMaskBounds(
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    public int Width => (Right - Left) + 1;

    public int Height => (Bottom - Top) + 1;
}

public sealed record InkMaskPoint(int X, int Y);

public sealed class InkMask
{
    private readonly float[] coverage;

    public InkMask(int width, int height)
        : this(width, height, new float[checked(width * height)])
    {
    }

    public InkMask(int width, int height, float[] coverage)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        ArgumentNullException.ThrowIfNull(coverage);

        int expectedLength = checked(width * height);
        if (coverage.Length != expectedLength)
        {
            throw new ArgumentException($"Coverage array length must be {expectedLength}.", nameof(coverage));
        }

        for (int index = 0; index < coverage.Length; index++)
        {
            float value = coverage[index];
            if (!float.IsFinite(value) || value < 0f || value > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(coverage), $"Coverage value at index {index} must be finite and within [0, 1].");
            }
        }

        Width = width;
        Height = height;
        this.coverage = coverage.ToArray();
    }

    public int Width { get; }

    public int Height { get; }

    public float GetCoverage(int x, int y)
    {
        ValidateCoordinates(x, y);
        return coverage[(y * Width) + x];
    }

    public void SetCoverage(int x, int y, float value)
    {
        ValidateCoordinates(x, y);

        if (!float.IsFinite(value) || value < 0f || value > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        coverage[(y * Width) + x] = value;
    }

    public bool IsInk(int x, int y, float threshold = 0.001f)
    {
        return GetCoverage(x, y) > threshold;
    }

    public InkMaskBounds? ComputeBounds(float threshold = 0.001f)
    {
        int minX = Width;
        int minY = Height;
        int maxX = -1;
        int maxY = -1;

        for (int y = 0; y < Height; y++)
        {
            int rowOffset = y * Width;
            for (int x = 0; x < Width; x++)
            {
                if (coverage[rowOffset + x] <= threshold)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return maxX < 0 || maxY < 0
            ? null
            : new InkMaskBounds(minX, minY, maxX, maxY);
    }

    public IReadOnlyList<InkMaskPoint> ExtractEdges(float threshold = 0.001f)
    {
        List<InkMaskPoint> edges = [];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (!IsInk(x, y, threshold))
                {
                    continue;
                }

                if (x == 0 || x == Width - 1 || y == 0 || y == Height - 1)
                {
                    edges.Add(new InkMaskPoint(x, y));
                    continue;
                }

                if (!IsInk(x - 1, y, threshold)
                    || !IsInk(x + 1, y, threshold)
                    || !IsInk(x, y - 1, threshold)
                    || !IsInk(x, y + 1, threshold))
                {
                    edges.Add(new InkMaskPoint(x, y));
                }
            }
        }

        return edges;
    }

    public RgbaImage ToImage(
        Rgba32 foreground,
        Rgba32 background,
        bool showBaselineGuide = false,
        double baselineY = 0d,
        Rgba32? baselineGuideColor = null)
    {
        RgbaImage image = new(Width, Height);

        for (int y = 0; y < Height; y++)
        {
            int rowOffset = y * Width;
            for (int x = 0; x < Width; x++)
            {
                float amount = coverage[rowOffset + x];
                image.SetPixel(x, y, Blend(background, foreground, amount));
            }
        }

        if (showBaselineGuide)
        {
            if (baselineGuideColor is null)
            {
                throw new ArgumentException("Baseline guide color must be provided when drawing the baseline guide.", nameof(baselineGuideColor));
            }

            int baselineRow = (int)Math.Round(baselineY, MidpointRounding.AwayFromZero);
            if ((uint)baselineRow < (uint)Height)
            {
                for (int x = 0; x < Width; x++)
                {
                    image.SetPixel(x, baselineRow, baselineGuideColor.Value);
                }
            }
        }

        return image;
    }

    public static InkMask FromImage(
        RgbaImage image,
        InkMaskExtractionOptions options)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(options);
        InkMaskExtractionOptions validated = options.Validate();

        InkMask mask = new(image.Width, image.Height);

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Rgba32 pixel = image.GetPixel(x, y);
                mask.SetCoverage(x, y, IsInkPixel(pixel, validated) ? 1f : 0f);
            }
        }

        return mask;
    }

    public static InkMask FromAlpha(RgbaImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        InkMask mask = new(image.Width, image.Height);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                mask.SetCoverage(x, y, image.GetPixel(x, y).A / 255f);
            }
        }

        return mask;
    }

    public static bool IsInkPixel(Rgba32 pixel, InkMaskExtractionOptions options)
    {
        return !IsBaselinePixel(pixel, options)
            && ComputeColorDistance(pixel, options.BackgroundColor) > options.InkDistanceThreshold;
    }

    public static bool IsBaselinePixel(Rgba32 pixel, InkMaskExtractionOptions options)
    {
        return ComputeColorDistance(pixel, options.BaselineGuideColor) <= options.BaselineDistanceThreshold;
    }

    public static int ComputeColorDistance(Rgba32 left, Rgba32 right)
    {
        int deltaR = Math.Abs(left.R - right.R);
        int deltaG = Math.Abs(left.G - right.G);
        int deltaB = Math.Abs(left.B - right.B);
        return Math.Max(deltaR, Math.Max(deltaG, deltaB));
    }

    private static Rgba32 Blend(Rgba32 background, Rgba32 foreground, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        byte r = (byte)Math.Round(background.R + ((foreground.R - background.R) * amount), MidpointRounding.AwayFromZero);
        byte g = (byte)Math.Round(background.G + ((foreground.G - background.G) * amount), MidpointRounding.AwayFromZero);
        byte b = (byte)Math.Round(background.B + ((foreground.B - background.B) * amount), MidpointRounding.AwayFromZero);
        byte a = (byte)Math.Round(background.A + ((foreground.A - background.A) * amount), MidpointRounding.AwayFromZero);
        return new Rgba32(r, g, b, a);
    }

    private void ValidateCoordinates(int x, int y)
    {
        if ((uint)x >= (uint)Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if ((uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }
    }
}
