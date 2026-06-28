namespace Machina.Fonts.Generation;

public sealed class FakeGlyphGenerator
{
    private readonly HashSet<int> missingCodepoints;

    public FakeGlyphGenerator(IEnumerable<int>? missingCodepoints = null)
    {
        this.missingCodepoints = missingCodepoints is null ? [] : [.. missingCodepoints];
    }

    public FakeGlyphGenerationResult Generate(GlyphKey key)
    {
        if (missingCodepoints.Contains(key.Codepoint))
        {
            return FakeGlyphGenerationResult.Missing(key, "Codepoint is configured as missing.");
        }

        double widthFactor = GetWidthFactor(key.Codepoint);
        if (key.Weight == MachinaFontWeight.Bold)
        {
            widthFactor += 0.08;
        }

        double advance = Math.Ceiling(key.EmSize * widthFactor);
        double width = Math.Max(1, Math.Ceiling(key.EmSize * widthFactor) + 4);
        double height = Math.Max(1, Math.Ceiling(key.EmSize) + 4);
        double bearingY = Math.Ceiling(key.EmSize * 0.8);
        GlyphMetrics metrics = new(advance, 0, bearingY, width, height);

        return FakeGlyphGenerationResult.Generated(key, metrics, (int)width, (int)height);
    }

    private static double GetWidthFactor(int codepoint)
    {
        if (codepoint == ' ')
        {
            return 0.35;
        }

        if (codepoint >= '0' && codepoint <= '9')
        {
            return 0.55;
        }

        if (char.IsPunctuation((char)Math.Min(codepoint, char.MaxValue)))
        {
            return 0.32;
        }

        if (codepoint >= 'A' && codepoint <= 'Z')
        {
            return 0.72;
        }

        if (codepoint >= 'a' && codepoint <= 'z')
        {
            return 0.58;
        }

        return 0.65;
    }
}

public sealed record FakeGlyphGenerationResult(
    GlyphKey Key,
    GlyphMetrics? Metrics,
    int Width,
    int Height,
    string? MissingReason)
{
    public bool IsMissing => MissingReason is not null;

    public static FakeGlyphGenerationResult Generated(GlyphKey key, GlyphMetrics metrics, int width, int height)
    {
        return new FakeGlyphGenerationResult(key, metrics, width, height, null);
    }

    public static FakeGlyphGenerationResult Missing(GlyphKey key, string reason)
    {
        return new FakeGlyphGenerationResult(key, null, 0, 0, reason);
    }
}
