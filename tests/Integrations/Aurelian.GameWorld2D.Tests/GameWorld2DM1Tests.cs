using Aurelian.GameWorld2D;
using Aurelian.Graphics.Vulkan.Native2D;
using Dominatus.SpriteForge;
using Xunit;

namespace Aurelian.GameWorld2D.Tests;

public sealed class GameWorld2DM1Tests
{
    private static readonly World2DUnitScale Scale = new(256, 1.0 / 8.0);

    [Fact]
    public void UnitScale_SeparatesTileWorldAndPixelConversions()
    {
        Assert.Equal(new WorldPoint2(512, 768), Scale.TileToWorld(new TilePoint2(2, 3)));

        var camera = new Camera2D(
            new WorldPoint2(256, 512),
            new PixelRect(0, 0, 320, 180),
            2,
            new WorldRect(0, 0, 4096, 4096));
        var adapter = new WorldSpriteProjectionAdapter();

        Assert.Equal(new PixelPoint2(64, 64), adapter.WorldToPixel(new WorldPoint2(512, 768), camera.Snapshot(), Scale));
    }

    [Fact]
    public void Camera_FollowsClampsSnapsAndResizesDeterministically()
    {
        var camera = new Camera2D(
            new WorldPoint2(0, 0),
            new PixelRect(0, 0, 320, 180),
            1,
            new WorldRect(0, 0, 4096, 2048));

        camera.Follow(new WorldPoint2(2048, 1024), Scale);
        Assert.Equal(new WorldPoint2(768, 304), camera.Position);
        camera.SnapTo(new WorldPoint2(-10, 9000), Scale);
        Assert.Equal(new WorldPoint2(0, 608), camera.Position);
        camera.SetZoom(2, Scale);
        camera.Resize(new PixelRect(0, 0, 640, 360), Scale);
        Assert.Equal(new WorldPoint2(0, 608), camera.Position);
        Assert.Equal(camera.Snapshot(), camera.Snapshot());
    }

    [Theory]
    [InlineData(0, true, 0)]
    [InlineData(250, true, 1)]
    [InlineData(500, true, 0)]
    [InlineData(900, false, 1)]
    public void PlaybackSampling_QualifiesLoopAndOnce(int milliseconds, bool loop, int expected)
    {
        int actual = SpritePlaybackState.SampleFrameIndex(TimeSpan.FromMilliseconds(milliseconds), 4, 2, loop);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Playback_NewClipRestartsAndSameClipContinues()
    {
        SpriteForgeAtlas atlas = Atlas();
        var playback = new SpritePlaybackState();
        var resolver = new SpriteForgeResolver();
        WorldSprite first = Sprite("actor", "walk", TimeSpan.FromMilliseconds(0));
        WorldSprite continued = first with { Elapsed = TimeSpan.FromMilliseconds(300) };
        WorldSprite restarted = continued with { ClipId = "once" };

        Assert.EndsWith(":0", playback.Resolve(first, atlas, resolver).FrameId, StringComparison.Ordinal);
        Assert.EndsWith(":1", playback.Resolve(continued, atlas, resolver).FrameId, StringComparison.Ordinal);
        Assert.EndsWith(":0", playback.Resolve(restarted, atlas, resolver).FrameId, StringComparison.Ordinal);
        Assert.EndsWith(":1", playback.Resolve(restarted with { Elapsed = TimeSpan.FromSeconds(5) }, atlas, resolver).FrameId, StringComparison.Ordinal);
    }

    [Fact]
    public void Projection_UsesPivotUvCameraAndStablePainterOrder()
    {
        SpriteForgeAtlas atlas = Atlas();
        var playback = new SpritePlaybackState();
        var resolver = new SpriteForgeResolver();
        var adapter = new WorldSpriteProjectionAdapter();
        var camera = new Camera2D(
            new WorldPoint2(0, 0),
            new PixelRect(0, 0, 320, 180),
            1,
            new WorldRect(0, 0, 4096, 2048));
        WorldSprite[] sprites =
        [
            Sprite("z", "walk", TimeSpan.Zero) with { FeetY = 300 },
            Sprite("b", "walk", TimeSpan.Zero) with { FeetY = 200 },
            Sprite("a", "walk", TimeSpan.Zero) with { FeetY = 200 },
            Sprite("front", "walk", TimeSpan.Zero) with { Layer = WorldSpriteLayer.Foreground, FeetY = 0 },
        ];

        IReadOnlyList<OrderedWorldSprite> projected = adapter.Project(
            new WorldPresentationSnapshot(sprites),
            camera.Snapshot(),
            Scale,
            _ => new Native2DTextureHandle(7),
            sprite => playback.Resolve(sprite, atlas, resolver));

        Assert.Equal(["a", "b", "z", "front"], projected.Select(item => item.Source.StableId.Value));
        NativeQuadSubmission first = projected[0].Submission;
        Assert.Equal(new Native2DRect(112, 96, 32, 32), first.Destination);
        Assert.Equal(new Native2DUvRect(0, 0, 0.5f, 1), first.Uv);
    }

    [Fact]
    public void Projection_DoesNotMutateSourceSnapshot()
    {
        WorldSprite source = Sprite("actor", "walk", TimeSpan.Zero);
        var snapshot = new WorldPresentationSnapshot([source]);
        var adapter = new WorldSpriteProjectionAdapter();
        SpriteFrameMetadata frame = new("frame", 0, 0, 32, 32, 16, 32, 0, 0, 1, new UvRect(0, 0, 1, 1));
        var camera = new Camera2D(new WorldPoint2(0, 0), new PixelRect(0, 0, 320, 180), 1, new WorldRect(0, 0, 4096, 2048));

        _ = adapter.Project(snapshot, camera.Snapshot(), Scale, _ => new Native2DTextureHandle(1), _ => frame);

        Assert.Same(source, snapshot.Sprites[0]);
        Assert.Equal(new WorldPoint2(1024, 1024), source.Anchor);
    }

    [Fact]
    public void Diagnostics_RejectInvalidUnitsAndMissingFrames()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new World2DUnitScale(0, 1).Validate());
        SpriteForgeAtlas atlas = Atlas();
        WorldSprite missing = Sprite("actor", "missing", TimeSpan.Zero);
        KeyNotFoundException error = Assert.Throws<KeyNotFoundException>(
            () => new SpritePlaybackState().Resolve(missing, atlas, new SpriteForgeResolver()));
        Assert.Contains("Missing atlas clip", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NativePipelineOptions_ExposeExplicitStraightAlphaSamplingPolicies()
    {
        Assert.True(Native2DPipelineOptions.SpriteNearest.StraightAlphaBlend);
        Assert.False(Native2DPipelineOptions.SpriteNearest.LinearFiltering);
        Assert.True(Native2DPipelineOptions.SpriteLinear.StraightAlphaBlend);
        Assert.True(Native2DPipelineOptions.SpriteLinear.LinearFiltering);
        Assert.False(Native2DPipelineOptions.Textured.StraightAlphaBlend);
    }

    private static WorldSprite Sprite(string id, string clip, TimeSpan elapsed)
    {
        return new WorldSprite(
            new WorldPresentationId(id),
            new WorldPoint2(1024, 1024),
            new SpriteAssetId("world"),
            "hero",
            clip,
            elapsed,
            false,
            1,
            Native2DTint.White,
            WorldSpriteLayer.Actors,
            256);
    }

    private static SpriteForgeAtlas Atlas()
    {
        var walk = new SpriteForgeAnimation
        {
            Id = "walk",
            Grid = "actors",
            Row = 0,
            Frames = [new SpriteForgeFrameRef { Col = 0 }, new SpriteForgeFrameRef { Col = 1 }],
            Fps = 4,
            Loop = true,
        };
        var once = walk with { Id = "once", Loop = false };
        return new SpriteForgeAtlas
        {
            SourcePath = "inline",
            Image = "world.png",
            ResolvedImagePath = "world.png",
            Width = 64,
            Height = 32,
            Grids = new Dictionary<string, SpriteForgeGrid>(StringComparer.Ordinal)
            {
                ["actors"] = new SpriteForgeGrid
                {
                    Id = "actors",
                    Columns = 2,
                    Rows = 1,
                    CellWidth = 32,
                    CellHeight = 32,
                    DefaultPivot = SpriteForgePivots.BottomCenter,
                },
            },
            Sprites = new Dictionary<string, SpriteForgeSprite>(StringComparer.Ordinal)
            {
                ["hero"] = new SpriteForgeSprite
                {
                    Id = "hero",
                    Kind = "actor",
                    Animations = new Dictionary<string, SpriteForgeAnimation>(StringComparer.Ordinal)
                    {
                        ["walk"] = walk,
                        ["once"] = once,
                    },
                },
            },
        };
    }
}
