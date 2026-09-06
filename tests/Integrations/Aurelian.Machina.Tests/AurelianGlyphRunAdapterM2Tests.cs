using Aurelian.Graphics.Vulkan.Native2D;
using Machina.Fonts;
using Machina.Fonts.ReferenceRendering;
using Xunit;

namespace Aurelian.Machina.Tests;

public sealed class AurelianGlyphRunAdapterM2Tests
{
    private static readonly FontFaceId Face = new("fixture");
    private static readonly GlyphKey Key = GlyphKey.FromChar(Face, 'M', 64);

    [Fact]
    public void Adapt_Derives_Destination_From_Qualified_Field_Plane_And_Preserves_Atlas_Uvs()
    {
        FontAtlasSnapshot atlas = CreateAtlas();
        MachinaGlyphRun run = CreateRun(Key);

        NativeMsdfQuadSubmission submission = Assert.Single(AurelianGlyphRunAdapter.Adapt(
            run,
            atlas,
            new Dictionary<int, Native2DTextureHandle> { [0] = new(17) },
            new Native2DTint(0.25f, 0.5f, 0.75f, 1f)));

        Assert.Equal(new Native2DRect(8, 12, 16, 20), submission.Destination);
        Assert.Equal(new Native2DUvRect(0.125f, 0.25f, 0.375f, 0.5625f), submission.Uv);
        Assert.Equal(new Native2DTextureHandle(17), submission.AtlasTexture);
        Assert.Equal(4f, submission.Msdf.PixelRange);
        Assert.Equal(0.25f, submission.Msdf.FieldScale);
        Assert.Equal(0.5f, submission.Msdf.Threshold);
    }

    [Fact]
    public void Adapt_Rejects_Missing_Glyph_Deterministically()
    {
        FontAtlasSnapshot atlas = CreateAtlas();
        GlyphKey missing = GlyphKey.FromChar(Face, 'Q', 64);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => AurelianGlyphRunAdapter.Adapt(
            CreateRun(missing),
            atlas,
            new Dictionary<int, Native2DTextureHandle> { [0] = new(17) },
            Native2DTint.White));

        Assert.Contains("Missing atlas entry", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Adapt_Rejects_Storage_Rectangle_Outside_Page()
    {
        GlyphAtlasEntry invalidEntry = CreateEntry(x: 240);
        FontAtlasSnapshot atlas = new(
            1,
            [new FontAtlasPage(0, "page.dfpage", 256, 256, null)],
            new Dictionary<GlyphKey, GlyphAtlasEntry> { [Key] = invalidEntry });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => AurelianGlyphRunAdapter.Adapt(
            CreateRun(Key),
            atlas,
            new Dictionary<int, Native2DTextureHandle> { [0] = new(17) },
            Native2DTint.White));

        Assert.Contains("outside page", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Adapt_ClipsDestinationAndUvsWithoutChangingLayout()
    {
        NativeMsdfQuadSubmission submission = Assert.Single(AurelianGlyphRunAdapter.Adapt(
            CreateRun(Key),
            CreateAtlas(),
            new Dictionary<int, Native2DTextureHandle> { [0] = new(17) },
            Native2DTint.White,
            new Native2DRect(12, 14, 8, 10)));

        Assert.Equal(new Native2DRect(12, 14, 8, 10), submission.Destination);
        Assert.Equal(new Native2DUvRect(0.1875f, 0.28125f, 0.3125f, 0.4375f), submission.Uv);
    }

    [Fact]
    public void AdaptInto_AppendsTheSameQualifiedSubmissionsWithoutReplacingExistingStorage()
    {
        var destination = new List<NativeMsdfQuadSubmission>
        {
            new(
                new Native2DRect(0, 0, 1, 1),
                Native2DUvRect.Full,
                new Native2DTextureHandle(99),
                Native2DTint.White,
                new NativeMsdfParameters(1, 1, 0.5f)),
        };

        AurelianGlyphRunAdapter.AdaptInto(
            CreateRun(Key),
            CreateAtlas(),
            new Dictionary<int, Native2DTextureHandle> { [0] = new(17) },
            Native2DTint.White,
            destination);

        Assert.Equal(2, destination.Count);
        Assert.Equal(new Native2DTextureHandle(99), destination[0].AtlasTexture);
        Assert.Equal(new Native2DRect(8, 12, 16, 20), destination[1].Destination);
    }

    private static FontAtlasSnapshot CreateAtlas()
    {
        GlyphAtlasEntry entry = CreateEntry();
        return new FontAtlasSnapshot(
            1,
            [new FontAtlasPage(0, "page.dfpage", 256, 256, null)],
            new Dictionary<GlyphKey, GlyphAtlasEntry> { [Key] = entry });
    }

    private static GlyphAtlasEntry CreateEntry(int x = 32)
    {
        return new GlyphAtlasEntry(
            Key,
            0,
            x,
            64,
            64,
            80,
            x / 256d,
            64 / 256d,
            (x + 64) / 256d,
            144 / 256d,
            new GlyphMetrics(40, 1, 48, 34, 50),
            new GlyphFieldPlacement(-2, -48, 14, -28, 4, 1));
    }

    private static MachinaGlyphRun CreateRun(GlyphKey key)
    {
        MachinaGlyphPlacement glyph = new(
            key,
            GlyphId: 42,
            new MachinaTextSpan(0, 1),
            OriginX: 10,
            BaselineY: 60,
            Advance: 40,
            new MachinaPlaneBounds(1, -48, 35, 2),
            TokenId: 0,
            IsWhitespace: false);
        return new MachinaGlyphRun("M", [], [], [glyph]);
    }
}
