using Xunit;
using Machina.Fonts.Generation;

namespace Machina.Fonts.Tests;

public sealed class FakeFontAtlasServiceTests
{
    private static readonly FontFaceId Face = new("Fake");

    [Fact]
    public async Task QueueAsync_AcceptsGlyphsWithoutBlockingUntilReady()
    {
        await using FakeFontAtlasService service = new(processingDelay: TimeSpan.FromMilliseconds(200));
        GlyphKey key = GlyphKey.FromChar(Face, 'A', 12);
        await service.QueueAsync([key]);
        Assert.True(service.Resolve(key) is GlyphPending);
    }

    [Fact]
    public async Task Resolve_ReturnsPendingBeforeWorkerCompletes()
    {
        await using FakeFontAtlasService service = new(processingDelay: TimeSpan.FromMilliseconds(100));
        GlyphKey key = GlyphKey.FromChar(Face, 'B', 12);
        await service.QueueAsync([key]);
        Assert.IsType<GlyphPending>(service.Resolve(key));
    }

    [Fact]
    public async Task Worker_PublishesReadyGlyphs()
    {
        await using FakeFontAtlasService service = new();
        GlyphKey key = GlyphKey.FromChar(Face, 'C', 12);
        FontAtlasPreflightResult result = await FontAtlasPreflight.EnsureReadyAsync(service, [key], TimeSpan.FromSeconds(5));
        Assert.True(result.Success);
        Assert.IsType<GlyphReady>(service.Resolve(key));
    }

    [Fact]
    public async Task QueueAsync_DeduplicatesRepeatedGlyphs()
    {
        await using FakeFontAtlasService service = new();
        GlyphKey key = GlyphKey.FromChar(Face, 'D', 12);
        await FontAtlasPreflight.EnsureReadyAsync(service, [key, key, key], TimeSpan.FromSeconds(5));
        Assert.Single(service.Snapshot.Glyphs);
    }

    [Fact]
    public async Task Snapshot_IsImmutableAcrossPublication()
    {
        await using FakeFontAtlasService service = new();
        GlyphKey first = GlyphKey.FromChar(Face, 'E', 12);
        await FontAtlasPreflight.EnsureReadyAsync(service, [first], TimeSpan.FromSeconds(5));
        FontAtlasSnapshot old = service.Snapshot;
        GlyphKey second = GlyphKey.FromChar(Face, 'F', 12);
        await FontAtlasPreflight.EnsureReadyAsync(service, [second], TimeSpan.FromSeconds(5));
        Assert.Single(old.Glyphs);
        Assert.Equal(2, service.Snapshot.Glyphs.Count);
    }

    [Fact]
    public async Task Worker_IncrementsVersionWhenGlyphsPublish()
    {
        await using FakeFontAtlasService service = new();
        long start = service.Snapshot.Version;
        await FontAtlasPreflight.EnsureReadyAsync(service, [GlyphKey.FromChar(Face, 'G', 12)], TimeSpan.FromSeconds(5));
        Assert.True(service.Snapshot.Version > start);
    }

    [Fact]
    public async Task Packer_CreatesNewPagesWhenNeeded()
    {
        await using FakeFontAtlasService service = new(packer: new FakeAtlasPacker(24, 24));
        GlyphKey[] keys = Enumerable.Range('A', 8).Select(value => GlyphKey.FromCodepoint(Face, value, 12)).ToArray();
        await FontAtlasPreflight.EnsureReadyAsync(service, keys, TimeSpan.FromSeconds(5));
        Assert.True(service.Snapshot.Pages.Count > 1);
    }

    [Fact]
    public async Task MissingGlyph_IsReportedWithoutPoisoningOtherGlyphs()
    {
        await using FakeFontAtlasService service = new(generator: new FakeGlyphGenerator(['X']));
        GlyphKey missing = GlyphKey.FromChar(Face, 'X', 12);
        GlyphKey ready = GlyphKey.FromChar(Face, 'Y', 12);
        FontAtlasPreflightResult result = await FontAtlasPreflight.EnsureReadyAsync(service, [missing, ready], TimeSpan.FromSeconds(5));
        Assert.False(result.Success);
        Assert.IsType<GlyphMissing>(service.Resolve(missing));
        Assert.IsType<GlyphReady>(service.Resolve(ready));
    }

    [Fact]
    public async Task Dispose_StopsWorkerCleanly()
    {
        FakeFontAtlasService service = new();
        await service.QueueAsync([GlyphKey.FromChar(Face, 'Z', 12)]);
        await service.DisposeAsync();
    }
}
