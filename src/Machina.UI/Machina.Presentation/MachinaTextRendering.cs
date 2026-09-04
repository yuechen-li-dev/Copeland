using Machina.Fonts.ReferenceRendering;

namespace Machina.Presentation;

/// <summary>
/// A presentation choice only. It does not participate in measurement, layout, or hit testing.
/// </summary>
public enum MachinaTextRenderingMode
{
    RasterPixel,
    Msdf,
}

/// <summary>
/// Stable, renderer-neutral identity for an upstream-produced font atlas.
/// </summary>
public readonly record struct MachinaFontAtlasId
{
    public MachinaFontAtlasId(string value)
    {
        Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Font atlas identity must not be empty or whitespace.", nameof(value))
            : value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// The smallest compositor-facing text value. Machina owns the already-positioned glyph run and
/// atlas semantics; backends own only realization.
/// </summary>
public sealed record MachinaTextPresentationPrimitive
{
    public MachinaTextPresentationPrimitive(
        MachinaGlyphRun glyphRun,
        MachinaFontAtlasId atlasIdentity,
        MachinaTextRenderingMode renderingMode)
    {
        GlyphRun = glyphRun ?? throw new ArgumentNullException(nameof(glyphRun));
        AtlasIdentity = atlasIdentity;
        RenderingMode = renderingMode;
    }

    public MachinaGlyphRun GlyphRun { get; }

    public MachinaFontAtlasId AtlasIdentity { get; }

    public MachinaTextRenderingMode RenderingMode { get; }
}
