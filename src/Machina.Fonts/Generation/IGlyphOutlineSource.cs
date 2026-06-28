namespace Machina.Fonts.Generation;

public interface IGlyphOutlineSource
{
    ValueTask<GlyphOutlineLoadResult> LoadGlyphOutlineAsync(
        FontFaceId face,
        int codepoint,
        GlyphOutlineLoadOptions options,
        CancellationToken cancellationToken = default);
}
