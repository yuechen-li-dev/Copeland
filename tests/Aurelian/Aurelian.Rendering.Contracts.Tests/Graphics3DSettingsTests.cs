using System.Numerics;
using Aurelian.Rendering.Contracts.Models;
using Xunit;

namespace Aurelian.Rendering.Contracts.Tests;

public sealed class Graphics3DSettingsTests
{
    [Fact]
    public void DefaultUsesTemporalAaAndHdrEmissionHasAnExplicitFiniteRange()
    {
        Assert.Equal(AntiAliasing3D.Temporal, Graphics3DSettings.Default.AntiAliasing);
        Assert.Equal(DirectDiffuseModel3D.PathDiffuse, Graphics3DSettings.Default.DirectDiffuseModel);
        Assert.Equal(DirectDiffuseModel3D.PathDiffuse, new Graphics3DSettings().DirectDiffuseModel);
        Assert.Equal(DirectDiffuseModel3D.Lambert, Graphics3DSettings.Basic.DirectDiffuseModel);
        foreach (DirectDiffuseModel3D model in Enum.GetValues<DirectDiffuseModel3D>())
        {
            (Graphics3DSettings.Default with { DirectDiffuseModel = model }).Validate();
        }
        new ModelMaterial("lamp") { Emissive = new(20, 5, 1) }.Validate();
        Assert.Throws<InvalidDataException>(() => new ModelMaterial("lamp") { Emissive = new(float.NaN, 0, 0) }.Validate());
        Assert.Throws<InvalidDataException>(() => new ModelMaterial("lamp") { Emissive = new(-1, 0, 0) }.Validate());
    }

    [Fact]
    public void SettingsRejectNonfiniteOrDegenerateLightingBeforeRendering()
    {
        Graphics3DSettings.Default.Validate();
        Graphics3DSettings.Basic.Validate();
        Graphics3DSettings[] invalid =
        [
            Graphics3DSettings.Default with { SunDirection = Vector3.Zero },
            Graphics3DSettings.Default with { SunDirection = new(float.MaxValue, 1, 1) },
            Graphics3DSettings.Default with { SkyAmbient = new(float.NaN, 0, 0) },
            Graphics3DSettings.Default with { SunIntensity = -1 },
            Graphics3DSettings.Default with { Exposure = 0 },
            Graphics3DSettings.Default with { ToneMapColorRetention = float.NaN },
            Graphics3DSettings.Default with { ToneMapColorRetention = 1.01f },
            Graphics3DSettings.Default with { ToneMapColorRetention = -.01f },
            Graphics3DSettings.Default with { ShadowRadius = float.PositiveInfinity },
            Graphics3DSettings.Default with { ShadowBias = -.1f },
            Graphics3DSettings.Default with { SolidRoughness = 0 },
            Graphics3DSettings.Default with { TemporalHistoryWeight = 1 },
            Graphics3DSettings.Default with { BloomIntensity = float.NaN },
            Graphics3DSettings.Default with { DirectDiffuseModel = (DirectDiffuseModel3D)99 },
        ];
        foreach (var settings in invalid)
        {
            Assert.Throws<ArgumentOutOfRangeException>(settings.Validate);
        }
    }
}
