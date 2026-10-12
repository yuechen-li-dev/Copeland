using System.Numerics;
using System.Collections.Immutable;
using Aurelian.Rendering.Contracts.Models;
using Xunit;

namespace Aurelian.Rendering.Contracts.Tests;

public sealed class WaterSurfaceTests
{
    [Fact]
    public void DefaultMaterialIsPhysicalWaterWithExplicitOpticalFactors()
    {
        var material = new WaterSurface3D().Material();
        material.Validate();
        Assert.Equal(1.333f, material.IndexOfRefraction);
        Assert.Equal(1, material.Transmission);
        Assert.Equal(0, material.Thickness);
        Assert.Throws<InvalidDataException>(() => (material with { Thickness = .1f }).Validate());
        Assert.Throws<InvalidDataException>(() => (material with { DoubleSided = true }).Validate());
        Assert.Throws<InvalidDataException>(() => (material with { Transmission = .5f }).Validate());
    }

    [Fact]
    public void WindProfilesRejectFoldedAndUndefinedSurfaces()
    {
        var water = new WaterSurface3D();
        Assert.Throws<InvalidDataException>(() => (water with { Waves = [new(Vector2.Zero, .1f, 1, 1)] }).Validate());
        Assert.Throws<InvalidDataException>(() => (water with { Waves = [
            new(Vector2.UnitX, .1f, 1, 1, .5f), new(Vector2.UnitY, .1f, 1, 1, .5f)] }).Validate());
        Assert.Throws<InvalidDataException>(() => (water with { MaximumDepth = float.NaN }).Validate());
        (water with { Waves = [] }).Validate();
    }

    [Fact]
    public void EventsAndReflectionHaveAuthoredBounds()
    {
        new WaterFrame3D(1) { Impulses = [new(Vector2.Zero, 0)] }.Validate();
        Assert.Throws<InvalidDataException>(() => new WaterFrame3D(-1).Validate());
        Assert.Throws<InvalidDataException>(() => new WaterFrame3D(1)
        { Impulses = Enumerable.Repeat(new WaterImpulse3D(Vector2.Zero, 0), 9).ToImmutableArray() }.Validate());
        Assert.Throws<InvalidDataException>(() => new WaterFrame3D(1)
        { Impulses = [new(Vector2.Zero, 0, Wavelength: 0)] }.Validate());
        Assert.Throws<InvalidDataException>(() => new PlanarWaterReflection3D(0) { ResolutionDivisor = 1 }.Validate());
        new PlanarWaterReflection3D(0).Validate();
    }
}
