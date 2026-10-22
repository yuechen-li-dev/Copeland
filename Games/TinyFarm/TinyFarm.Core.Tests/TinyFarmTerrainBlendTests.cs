using System.Numerics;
using Aurelian.GameWorld2D;
using TinyFarm.Core;
using TinyFarm.Native;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmTerrainBlendTests
{
    [Fact]
    public void LinearCompositionUsesLinearLightAndOpaqueOutput()
    {
        byte[] pixels = GroundTextureBlender.Blend(1, 1, [Vector3.Zero], [Vector3.One], [.5f], GroundTextureBlend.Linear);
        Assert.Equal(new byte[] { 188, 188, 188, 255 }, pixels);
        Assert.Throws<ArgumentException>(() => GroundTextureBlender.Blend(1, 1, [Vector3.Zero], [Vector3.One], [float.NaN], GroundTextureBlend.Linear));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(.5f)]
    [InlineData(1f)]
    public void MultiscalePreservesConstantCoverageAndIsDeterministic(float weight)
    {
        const int Size = 24;
        var first = Enumerable.Range(0, Size * Size).Select(index => new Vector3((index % 7 + 1) / 9f, .2f, .6f)).ToArray();
        var second = first.Select(value => new Vector3(.5f, value.X, .1f)).ToArray();
        float[] mask = Enumerable.Repeat(weight, first.Length).ToArray();
        byte[] reference = GroundTextureBlender.Blend(Size, Size, first, second, mask, GroundTextureBlend.Linear);
        byte[] actual = GroundTextureBlender.Blend(Size, Size, first, second, mask, GroundTextureBlend.Multiscale);
        Assert.All(reference.Zip(actual), pair => Assert.InRange(Math.Abs(pair.First - pair.Second), 0, 1));
        Assert.Equal(actual, GroundTextureBlender.Blend(Size, Size, first, second, mask, GroundTextureBlend.Multiscale));
    }

    [Fact]
    public void PathDistanceFollowsAuthoredCapsulesIncludingEndCapsAndUnion()
    {
        GroundBrushStroke[] strokes = [new([new(0, 0), new(2, 0)], 1), new([new(1, 0), new(1, 2)], .5)];
        Assert.Equal(-1, GroundBrushRasterizer.SignedDistance(new(1, 0), strokes));
        Assert.Equal(0, GroundBrushRasterizer.SignedDistance(new(3, 0), strokes));
        Assert.Equal(-.5, GroundBrushRasterizer.SignedDistance(new(1, 2), strokes));
        Assert.Equal(1, GroundBrushRasterizer.SignedDistance(new(4, 0), strokes));
    }

    [Fact]
    public void ChoosingTerrainPresentationDoesNotChangeTheGameSnapshot()
    {
        using var legacy = new TinyFarm.TinyFarmApp(legacySaveDirectory: "unused", terrain: TinyFarmTerrainMode.Legacy);
        using var detail = new TinyFarm.TinyFarmApp(legacySaveDirectory: "unused", terrain: TinyFarmTerrainMode.Detail);
        Assert.Equal(TinyFarmSemanticHash.Compute(legacy.Game.State), TinyFarmSemanticHash.Compute(detail.Game.State));
        Assert.Equal(legacy.Game.Host.Session.Field.SemanticHash, detail.Game.Host.Session.Field.SemanticHash);
    }

    [Theory]
    [InlineData(TinyFarmTerrainMode.Alpha)]
    [InlineData(TinyFarmTerrainMode.Detail)]
    [InlineData(TinyFarmTerrainMode.Multiscale)]
    public void SceneCompositionReusesCacheAndPreservesSourceMasks(TinyFarmTerrainMode mode)
    {
        using var app = new TinyFarm.TinyFarmApp(legacySaveDirectory: "unused");
        var frame = TinyFarmFrameProjector.Project(app.Game.State, app.Game.Definitions) with
        {
            ActiveScene = TinyFarmSceneIds.Riverside,
        };
        var field = app.Game.Host.Session.Field;
        string before = field.SemanticHash;
        byte[] meadowPixels = [128, 160, 128, 255];
        byte[] earthPixels = [160, 128, 80, 255];
        var meadow = new SpriteAtlasResource(new("test-meadow"), "test", 1, 1, meadowPixels, SpriteSampling.Linear);
        var earth = new SpriteAtlasResource(new("test-earth"), "test", 1, 1, earthPixels, SpriteSampling.Linear);
        var study = new TinyFarmTerrainStudy(mode, meadow, earth);

        SpriteAtlasResource first = study.Get(frame, field);
        Assert.Same(first, study.Get(frame, field));
        Assert.Equal(first.Rgba8.LongLength, study.CachedBytes);
        Assert.Equal(before, field.SemanticHash);
        Assert.Equal(new byte[] { 128, 160, 128, 255 }, meadowPixels);
        Assert.Equal(new byte[] { 160, 128, 80, 255 }, earthPixels);
        // This corner is outside the bank: cached meadow must use the renderer's decoded sRGB tint.
        Assert.InRange(first.Rgba8[0], 104, 106);
        Assert.InRange(first.Rgba8[1], 149, 151);
        Assert.InRange(first.Rgba8[2], 109, 111);

        field.Fluid.LiquidMask.Cells[0] = 1;
        Assert.Throws<InvalidOperationException>(() => study.Get(frame, field));
    }
}
