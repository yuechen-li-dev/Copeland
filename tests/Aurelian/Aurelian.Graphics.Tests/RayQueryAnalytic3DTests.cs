using System.Numerics;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.RayQueries;
using Aurelian.Rendering.Contracts.Models;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class RayQueryAnalytic3DTests
{
    [Theory]
    [InlineData(AnalyticPrimitiveKind3D.UnitSphere)]
    [InlineData(AnalyticPrimitiveKind3D.UnitBox)]
    [InlineData(AnalyticPrimitiveKind3D.Capsule)]
    public void AffineCoverageContainsCanonicalBoundsAndReflectionsRemainAnalytic(AnalyticPrimitiveKind3D kind)
    {
        Matrix4x4 shear = Matrix4x4.Identity;
        shear.M21 = .7f;
        shear.M31 = -.3f;
        Matrix4x4 transform = Matrix4x4.CreateScale(-.12f, 3, .7f) * shear * Matrix4x4.CreateTranslation(120, -4, 17);
        var primitive = new RayQueryAnalytic3D(kind, transform, 2, 4);
        Assert.Equal(2u, primitive.Layer);
        Assert.Equal(4u, primitive.Mask);
        float radius = kind == AnalyticPrimitiveKind3D.Capsule ? .5f : 1;
        foreach (float x in new[] { -radius, radius })
        {
            foreach (float y in new[] { -1f, 1f })
            {
                foreach (float z in new[] { -radius, radius })
                {
                    Vector3 world = Vector3.Transform(new Vector3(x, y, z), transform);
                    Assert.InRange(world.X, primitive.Minimum.X, primitive.Maximum.X);
                    Assert.InRange(world.Y, primitive.Minimum.Y, primitive.Maximum.Y);
                    Assert.InRange(world.Z, primitive.Minimum.Z, primitive.Maximum.Z);
                }
            }
        }
        var mesh = NativeAnalyticPrimitives3D.Create("reflected", kind);
        var instances = new NativeInstanceSet3D([new("reflected", mesh, new("surface"), transform)]);
        Assert.Single(instances);
    }

    [Fact]
    public void InvalidTransformsAreRejectedAndTriangleWindingAdmissionIsPreserved()
    {
        Assert.Throws<ArgumentException>(() => new RayQueryAnalytic3D(AnalyticPrimitiveKind3D.Triangles, Matrix4x4.Identity));
        Assert.Throws<ArgumentException>(() => new RayQueryAnalytic3D(AnalyticPrimitiveKind3D.UnitSphere, Matrix4x4.CreateScale(0)));
        Matrix4x4 projective = Matrix4x4.Identity;
        projective.M14 = .1f;
        Assert.Throws<ArgumentException>(() => new RayQueryAnalytic3D(AnalyticPrimitiveKind3D.UnitSphere, projective));
        var vertices = new[] {
            new NativeModel3DVertex(Vector3.Zero, Vector3.UnitZ, Vector4.One, Vector2.Zero, new(1, 0, 0, 1)),
            new NativeModel3DVertex(Vector3.UnitX, Vector3.UnitZ, Vector4.One, Vector2.Zero, new(1, 0, 0, 1)),
            new NativeModel3DVertex(Vector3.UnitY, Vector3.UnitZ, Vector4.One, Vector2.Zero, new(1, 0, 0, 1)),
        };
        var mesh = new NativeMesh3D("triangle", vertices, [0, 1, 2]);
        Assert.Throws<InvalidDataException>(() => new NativeInstanceSet3D([
            new("reflected-triangle", mesh, new("surface"), Matrix4x4.CreateScale(-1, 1, 1)),
        ]));
    }
}
