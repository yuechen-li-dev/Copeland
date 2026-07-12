namespace Machina.Fonts.Generation;

public interface IGlyphPairAdjustmentSource
{
    ValueTask<GlyphPairAdjustment?> GetPairAdjustmentAsync(
        GlyphKey left,
        GlyphKey right,
        CancellationToken cancellationToken = default);
}
