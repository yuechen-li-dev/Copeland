namespace Machina.Fonts;

public abstract record GlyphResolution;

public sealed record GlyphReady : GlyphResolution
{
    public GlyphReady(GlyphAtlasEntry entry)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
    }

    public GlyphAtlasEntry Entry { get; }
}

public sealed record GlyphPending(GlyphMetrics? EstimatedMetrics = null) : GlyphResolution;

public sealed record GlyphMissing : GlyphResolution
{
    public GlyphMissing(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Missing reason must not be empty.", nameof(reason));
        Reason = reason;
    }

    public string Reason { get; }
}
