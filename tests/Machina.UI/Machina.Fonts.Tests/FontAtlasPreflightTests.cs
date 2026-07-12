using Xunit;
using Machina.Fonts.Generation;

namespace Machina.Fonts.Tests;

public sealed class FontAtlasPreflightTests
{
    private static readonly FontFaceId Face = new("Fake");

    [Fact]
    public async Task EnsureReadyAsync_ReturnsSuccessWhenAllGlyphsReady()
    {
        await using FakeFontAtlasService service = new();
        FontAtlasPreflightResult result = await FontAtlasPreflight.EnsureReadyAsync(service, [GlyphKey.FromChar(Face, 'A', 16)], TimeSpan.FromSeconds(5));
        Assert.True(result.Success);
        Assert.Empty(result.PendingGlyphs);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task EnsureReadyAsync_ReturnsFailuresForMissingGlyphs()
    {
        await using FakeFontAtlasService service = new(generator: new FakeGlyphGenerator(['?']));
        FontAtlasPreflightResult result = await FontAtlasPreflight.EnsureReadyAsync(service, [GlyphKey.FromChar(Face, '?', 16)], TimeSpan.FromSeconds(5));
        Assert.False(result.Success);
        Assert.Single(result.Failures);
    }

    [Fact]
    public async Task EnsureReadyAsync_TimesOutWithPendingGlyphs()
    {
        await using FakeFontAtlasService service = new(processingDelay: TimeSpan.FromSeconds(2));
        GlyphKey key = GlyphKey.FromChar(Face, 'P', 16);
        FontAtlasPreflightResult result = await FontAtlasPreflight.EnsureReadyAsync(service, [key], TimeSpan.FromMilliseconds(20));
        Assert.False(result.Success);
        Assert.Contains(key, result.PendingGlyphs);
    }

    [Fact]
    public async Task EnsureReadyAsync_UsesSnapshotVersionContainingReadyGlyphs()
    {
        await using FakeFontAtlasService service = new();
        GlyphKey key = GlyphKey.FromChar(Face, 'Q', 16);
        FontAtlasPreflightResult result = await FontAtlasPreflight.EnsureReadyAsync(service, [key], TimeSpan.FromSeconds(5));
        Assert.True(result.Snapshot.Glyphs.ContainsKey(key));
        Assert.Equal(service.Snapshot.Version, result.Snapshot.Version);
    }

    [Fact]
    public async Task EnsureReadyAsync_IsDeterministicForSameInputs()
    {
        GlyphKey[] keys = [GlyphKey.FromChar(Face, 'A', 16), GlyphKey.FromChar(Face, 'b', 16)];
        await using FakeFontAtlasService first = new();
        await using FakeFontAtlasService second = new();
        FontAtlasPreflightResult firstResult = await FontAtlasPreflight.EnsureReadyAsync(first, keys, TimeSpan.FromSeconds(5));
        FontAtlasPreflightResult secondResult = await FontAtlasPreflight.EnsureReadyAsync(second, keys, TimeSpan.FromSeconds(5));
        Assert.Equal(firstResult.Snapshot.Glyphs.Values.ToArray(), secondResult.Snapshot.Glyphs.Values.ToArray());
    }
}
