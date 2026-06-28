namespace Machina.Fonts.Generation;

public enum GlyphHintingMode
{
    None,
    Native,
    Auto,
}

public sealed record GlyphOutlineLoadOptions
{
    public GlyphOutlineLoadOptions(
        float emSize,
        int faceIndex,
        GlyphHintingMode hintingMode,
        bool normalizeToEm,
        bool includeColorGlyphLayers = false)
    {
        if (!float.IsFinite(emSize) || emSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(emSize), "Em size must be finite and greater than zero.");
        }

        if (faceIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(faceIndex), "Face index must be greater than or equal to zero.");
        }

        EmSize = emSize;
        FaceIndex = faceIndex;
        HintingMode = hintingMode;
        NormalizeToEm = normalizeToEm;
        IncludeColorGlyphLayers = includeColorGlyphLayers;
    }

    public float EmSize { get; }

    public int FaceIndex { get; }

    public GlyphHintingMode HintingMode { get; }

    public bool NormalizeToEm { get; }

    public bool IncludeColorGlyphLayers { get; }
}
