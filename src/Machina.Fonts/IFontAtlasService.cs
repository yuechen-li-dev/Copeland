namespace Machina.Fonts;

public interface IFontAtlasService
{
    FontAtlasSnapshot Snapshot { get; }

    GlyphResolution Resolve(GlyphKey key);

    ValueTask QueueAsync(
        IReadOnlyList<GlyphKey> keys,
        CancellationToken cancellationToken = default);
}

public interface IFontAtlasVersionSource
{
    long Version { get; }

    ValueTask WaitForVersionChangeAsync(long version, CancellationToken cancellationToken = default);
}
