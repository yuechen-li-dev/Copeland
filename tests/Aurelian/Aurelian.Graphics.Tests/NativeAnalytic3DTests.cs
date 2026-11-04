using System.Numerics;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class NativeAnalytic3DTests
{
    [Theory]
    [InlineData(AnalyticPrimitiveKind3D.UnitSphere)]
    [InlineData(AnalyticPrimitiveKind3D.UnitBox)]
    [InlineData(AnalyticPrimitiveKind3D.Capsule)]
    public void BoundsDescribeTheSurfaceAndProxyCannotBecomeForwardGeometry(AnalyticPrimitiveKind3D kind)
    {
        var mesh = NativeAnalyticPrimitives3D.Create("analytic", kind);
        Assert.Equal(kind, mesh.PrimitiveKind);
        Assert.Equal(6, mesh.Indices.Length);
        var cluster = Assert.Single(NativeMeshClusters3D.Build(mesh));
        Assert.Equal(-2, cluster.Cone.W);
        float radius = kind == AnalyticPrimitiveKind3D.UnitBox ? MathF.Sqrt(3) : 1;
        Assert.InRange(cluster.Sphere.W, radius, radius + .001f);
        var instance = new SceneInstance3D("analytic", mesh, new("surface"), Matrix4x4.Identity);
        Assert.Throws<NotSupportedException>(() => instance.Expand());
        Assert.Throws<NotSupportedException>(() => new NativeInstanceSet3D([instance with
        {
            Material = instance.Material with { AlphaMask = true },
        }]));
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeAnalyticPrimitives3D.Create("bad", AnalyticPrimitiveKind3D.Triangles));
    }
}
