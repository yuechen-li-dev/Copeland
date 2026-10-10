using System.Numerics;
using Aurelian.Rendering.Contracts.Models;
using Xunit;

namespace Aurelian.Rendering.Contracts.Tests;

public sealed class SurfaceLightingContractsTests
{
    [Fact]
    public void PresentationMaterialsRejectAmbiguousOrNonphysicalCombinations()
    {
        ModelMaterial skin = new("skin") { Metallic = 0, SubsurfaceStrength = .8f };
        skin.Validate();
        (skin with { SubsurfaceStrength = 0, AlphaBlend = true }).Validate();
        Assert.Throws<InvalidDataException>(() => (skin with { AlphaBlend = true }).Validate());
        Assert.Throws<InvalidDataException>(() => (skin with { Metallic = .1f }).Validate());
        Assert.Throws<InvalidDataException>(() => (skin with { Unlit = true }).Validate());
        Assert.Throws<InvalidDataException>(() => (skin with { SubsurfaceRadius = float.NaN }).Validate());
        Assert.Throws<InvalidDataException>(() => (skin with { SubsurfaceColor = new(2) }).Validate());
        Assert.Throws<InvalidDataException>(() => new ModelMaterial("ambiguous") { AlphaBlend = true, AlphaMask = true }.Validate());
    }

    [Fact]
    public void FogAndShadowSettingsAdmitFiniteWorldUnits()
    {
        (Graphics3DSettings.Default with { Fog = new() { Density = .03f }, ShadowDistance = 250 }).Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() => (Graphics3DSettings.Default with { ShadowDistance = float.PositiveInfinity }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Graphics3DSettings.Default with { ShadowCasterPadding = 0 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeightFog3D { Density = -1 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeightFog3D { HeightFalloff = float.NaN }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeightFog3D { StartDistance = 100, MaximumDistance = 100 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeightFog3D { Color = new(-1, 0, 0) }.Validate());
    }

    [Fact]
    public void LightAndProbeContractsRejectInvalidDomains()
    {
        var point = new LocalLight3D(Vector3.Zero, Vector3.One, 10, 4);
        point.Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() => (point with { Range = float.NaN }).Validate());
        Assert.Throws<NotSupportedException>(() => (point with { CastShadows = true }).Validate());
        (point with { Kind = LocalLightKind.Spot, CastShadows = true }).Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReflectionProbe3D(Vector3.Zero, Vector3.Zero).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Graphics3DSettings.Default with { LocalShadowBudget = 3 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Graphics3DSettings.Default with { SurfaceDebugView = (SurfaceDebugView3D)99 }).Validate());
    }
}
