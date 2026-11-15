using System.Numerics;
using Xunit;

namespace Aurelian.Spatial3D.Tests;

public sealed class FirstPersonWeaponProbeTests
{
    [Fact]
    public void VolumeProbeFindsThinWallsAndHipSideObstructionsWithoutChangingTheWorld()
    {
        var wall = new Collider3D("wall", CollisionMesh3D.Box(new(2, 2, .05f)), Matrix4x4.CreateTranslation(0, 1, -.6f));
        var world = new SpatialWorld3D([wall]);
        Vector3 eye = new(0, 1.6f, 0);
        float distance = FirstPersonWeaponProbe3D.Measure(world, eye, -Vector3.UnitZ);
        Assert.InRange(distance, .463f, .467f);
        Assert.Equal(1.05f, FirstPersonWeaponProbe3D.Measure(world, eye, Vector3.UnitZ));
        Assert.Equal(wall, Assert.Single(world.Colliders));

        var side = new SpatialWorld3D([new("side", CollisionMesh3D.Box(new(.05f, 2, 2)),
            Matrix4x4.CreateTranslation(.3f, 1, 0))]);
        Assert.Equal(0, FirstPersonWeaponProbe3D.Measure(side, eye, -Vector3.UnitZ));
        Assert.Equal(1.05f, FirstPersonWeaponProbe3D.Measure(side, eye, -Vector3.UnitZ, aim: 1));
        Assert.Throws<ArgumentException>(() => FirstPersonWeaponProbe3D.Measure(world, eye, Vector3.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => FirstPersonWeaponProbe3D.Measure(world, eye, -Vector3.UnitZ, float.NaN));
    }
}
