namespace Machina.Fonts.Generation;

public interface IGlyphDistanceFieldGenerator
{
    GeneratedGlyphDistanceField Generate(
        GlyphOutline outline,
        MsdfGenerationSettings settings,
        CancellationToken cancellationToken = default);
}
