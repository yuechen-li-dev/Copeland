using System.Numerics;
using Aurelian.Rendering.Contracts.Models;
using Xunit;

namespace Aurelian.Rendering.Contracts.Tests;

public sealed class WetSurfaceTests
{
    [Fact]
    public void ResponsePreservesDryIdentityAndSharesTexturesWithoutChangingAlphaOrMetal()
    {
        var dry = new ModelMaterial("stone") { BaseColor = new(.8f, .6f, .4f, .7f), Roughness = .8f };
        var profile = new WetSurface3D();
        Assert.Same(dry, profile.Apply(dry, 0));
        Assert.Same(dry, profile.Apply(dry, 1, exposure: 0));
        var wet = profile.Apply(dry, 1);
        Assert.Equal(.52f, wet.BaseColor.X, 5);
        Assert.Equal(.24f, wet.Roughness, 5);
        Assert.Equal(.55f, wet.NormalScale, 5);
        Assert.Equal(dry.Metallic, wet.Metallic);
        Assert.Equal(dry.BaseColor.W, wet.BaseColor.W);
        Assert.Equal(.8f, dry.Roughness);
        wet.Validate();
    }

    [Fact]
    public void ExposureSeparatesOrientationFromShelterAndRejectsInvalidInput()
    {
        Assert.Equal(1, WetSurface3D.RainExposure(Vector3.UnitY));
        Assert.Equal(0, WetSurface3D.RainExposure(-Vector3.UnitY));
        Assert.Equal(0, WetSurface3D.RainExposure(Vector3.UnitY, shelter: 1));
        Assert.Throws<ArgumentException>(() => WetSurface3D.RainExposure(Vector3.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WetSurface3D().Apply(new("stone"), float.NaN));
        Assert.Throws<ArgumentException>(() => new WetSurface3D().Apply(new("glass") { Metallic = 0, Transmission = 1 }, 1));
    }
}
