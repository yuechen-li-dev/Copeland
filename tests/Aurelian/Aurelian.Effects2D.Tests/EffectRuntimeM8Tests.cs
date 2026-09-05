using System.Numerics;
using Aurelian.Effects2D;
using Xunit;

namespace Aurelian.Effects2D.Tests;

public sealed class EffectRuntimeM8Tests
{
    private static readonly EffectCatalog Catalog = EffectCatalog.CreateSmallGameDefaults();

    [Fact]
    public void CatalogIsClosedImmutableAndValid()
    {
        Assert.Equal(6, Catalog.Definitions.Count);
        Assert.All(Catalog.Definitions, definition => definition.Validate());
        Assert.Throws<KeyNotFoundException>(() => Catalog.Get(new VisualEffectId("unknown")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EffectDefinition(
            new VisualEffectId("bad"),
            EffectEmitterKind.Burst,
            TimeSpan.Zero,
            -1,
            1,
            1,
            0,
            0,
            0,
            EffectPainterLayer.FrontOfActors,
            EffectBlendMode.StraightAlpha,
            0,
            EffectMaterialIds.AnalyticParticle).Validate());
        Assert.Throws<ArgumentException>(() => new EffectCatalog([
            Catalog.Get(VisualEffectIds.HarvestPuff) with
            {
                Id = new VisualEffectId("unknown-material-effect"),
                MaterialId = new EffectMaterialId("missing")
            }
        ]));
    }

    [Fact]
    public void NegativeCountAndUnsupportedBlendAreRejected()
    {
        EffectDefinition valid = Catalog.Get(VisualEffectIds.HarvestPuff);
        Assert.Throws<ArgumentOutOfRangeException>(() => (valid with { SpawnCount = -1 }).Validate());
        Assert.Throws<ArgumentException>(() => (valid with { BlendMode = (EffectBlendMode)99 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new EffectRuntime(Catalog, particleCapacity: 0));
    }

    [Fact]
    public void StableIdsHaveValueSemantics()
    {
        Assert.Equal(new VisualEffectId("a"), new VisualEffectId("a"));
        Assert.Equal(new VisualEffectEventId("e"), new VisualEffectEventId("e"));
        Assert.NotEqual(new VisualEffectEventId("e"), new VisualEffectEventId("f"));
    }

    [Fact]
    public void BurstSpawnIsDeterministicAndExpires()
    {
        VisualEffectEvent request = Request(VisualEffectIds.HarvestPuff, "harvest:4", 1729);
        var first = new EffectRuntime(Catalog);
        var second = new EffectRuntime(Catalog);

        Assert.True(first.TryEmit(request, out _));
        Assert.True(second.TryEmit(request, out _));
        Assert.Equal(first.BuildParticleDrawData(), second.BuildParticleDrawData());
        Assert.Equal(10, first.ActiveParticleCount);

        first.Update(TimeSpan.FromSeconds(1));
        Assert.Equal(0, first.ActiveParticleCount);
        Assert.Equal(0, first.ActiveEmitterCount);
    }

    [Fact]
    public void DedupePreventsPresentationRebuildRespawn()
    {
        var runtime = new EffectRuntime(Catalog);
        VisualEffectEvent request = Request(VisualEffectIds.SwordHit, "attack:11:0", 45);

        Assert.True(runtime.TryEmit(request, out _));
        Assert.False(runtime.TryEmit(request, out string? diagnostic));
        Assert.Contains("already realized", diagnostic, StringComparison.Ordinal);
        Assert.Equal(14, runtime.ActiveParticleCount);
    }

    [Fact]
    public void CapacityRejectsNewestDeterministicallyAndDoesNotLeak()
    {
        var runtime = new EffectRuntime(Catalog, particleCapacity: 20, emitterCapacity: 4);
        Assert.True(runtime.TryEmit(Request(VisualEffectIds.SwordHit, "first", 1), out _));
        Assert.False(runtime.TryEmit(Request(VisualEffectIds.HarvestPuff, "second", 2), out string? diagnostic));
        Assert.Contains("newest request", diagnostic, StringComparison.Ordinal);
        Assert.Equal(14, runtime.ActiveParticleCount);
        Assert.Equal(1, runtime.DroppedEffectCount);

        runtime.Update(TimeSpan.FromSeconds(1));
        Assert.Equal(0, runtime.ActiveParticleCount);
        Assert.Equal(0, runtime.ActiveEmitterCount);
    }

    [Fact]
    public void AmbientAndTrailStayBounded()
    {
        var runtime = new EffectRuntime(Catalog, particleCapacity: 32);
        Assert.True(runtime.TryEmit(Request(VisualEffectIds.AmbientMotes, "scene:ambient", 9), out _));
        Assert.True(runtime.TryEmit(Request(VisualEffectIds.FootstepDust, "move:trail", 10), out _));

        for (int index = 0; index < 3_600; index++)
        {
            runtime.Update(TimeSpan.FromSeconds(1.0 / 60.0));
        }

        Assert.InRange(runtime.ActiveParticleCount, 0, 32);
        Assert.Equal(0, runtime.ActiveEmitterCount);
    }

    [Fact]
    public void ThousandRequestsRespectBoundedCapacityAndAllExpire()
    {
        var runtime = new EffectRuntime(Catalog, particleCapacity: 128, emitterCapacity: 16);
        for (int index = 0; index < 1_000; index++)
        {
            runtime.TryEmit(Request(VisualEffectIds.PickupSparkle, $"pickup:{index}", (ulong)index), out _);
        }

        Assert.InRange(runtime.ActiveParticleCount, 0, 128);
        Assert.InRange(runtime.ActiveEmitterCount, 0, 16);
        Assert.True(runtime.DroppedEffectCount > 0);
        runtime.Update(TimeSpan.FromSeconds(2));
        Assert.Equal(0, runtime.ActiveParticleCount);
        Assert.Equal(0, runtime.ActiveEmitterCount);
    }

    [Fact]
    public void WorldAndScreenTransformsAreExplicit()
    {
        var transform = new EffectCameraTransform(
            new Vector2(10, 20),
            new Vector2(4, 8),
            PixelsPerWorldUnit: 2,
            Zoom: 1.5f);

        Assert.Equal(new Vector2(34, 38), transform.Project(new Vector2(20, 30), EffectCoordinateSpace.World));
        Assert.Equal(new Vector2(20, 30), transform.Project(new Vector2(20, 30), EffectCoordinateSpace.Screen));
    }

    [Fact]
    public void DrawDataHasStablePainterOrderAndShaderQuad()
    {
        var runtime = new EffectRuntime(Catalog);
        Assert.True(runtime.TryEmit(Request(VisualEffectIds.SwordHit, "hit", 3), out _));
        Assert.True(runtime.TryEmit(new VisualEffectEvent(
            VisualEffectIds.ScreenFlash,
            new VisualEffectEventId("flash"),
            EffectCoordinateSpace.Screen,
            Intensity: 0.4f,
            Seed: 4), out _));

        IReadOnlyList<EffectQuadSnapshot> quads = runtime.BuildQuadDrawData();
        Assert.Equal(2, quads.Count);
        Assert.Equal(EffectPainterLayer.FrontOfActors, quads[0].PainterLayer);
        Assert.Equal(EffectMaterialIds.SoftShockwave, quads[0].MaterialId);
        Assert.Equal(EffectPainterLayer.ScreenFlash, quads[1].PainterLayer);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void NonFiniteParametersAreRejected(float value)
    {
        var runtime = new EffectRuntime(Catalog);
        VisualEffectEvent request = Request(VisualEffectIds.SwordHit, "bad", 1) with { Intensity = value };
        Assert.False(runtime.TryEmit(request, out string? diagnostic));
        Assert.Contains("finite", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownEffectAndWrongCoordinateSpaceAreDiagnosed()
    {
        var runtime = new EffectRuntime(Catalog);
        Assert.False(runtime.TryEmit(Request(new VisualEffectId("missing"), "missing", 0), out string? unknown));
        Assert.Contains("Unknown visual effect", unknown, StringComparison.Ordinal);
        Assert.False(runtime.TryEmit(Request(VisualEffectIds.ScreenFlash, "wrong-space", 0), out string? space));
        Assert.Contains("screen coordinate space", space, StringComparison.Ordinal);
    }

    private static VisualEffectEvent Request(VisualEffectId effectId, string eventId, ulong seed)
        => new(
            effectId,
            new VisualEffectEventId(eventId),
            EffectCoordinateSpace.World,
            Position: new Vector2(12, 9),
            Direction: Vector2.UnitX,
            Seed: seed);
}
