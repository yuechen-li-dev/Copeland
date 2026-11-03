using System.Numerics;
using Aurelian.Graphics.FramePlans;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class ClusterVisibilityTests
{
    [Fact]
    public void ClustersPreserveEveryTriangleAndBoundEveryReferencedVertex()
    {
        var vertices = Enumerable.Range(0, 513).Select(index => new NativeModel3DVertex(
            new(index % 17, index / 17, index % 3 * .1f), Vector3.UnitZ, Vector4.One, Vector2.Zero, new(1, 0, 0, 1))).ToArray();
        var mesh = new NativeMesh3D("partition", vertices, Enumerable.Range(0, vertices.Length).Select(index => (uint)index));
        var clusters = NativeMeshClusters3D.Build(mesh);
        Assert.Equal(2, clusters.Length);
        Assert.Equal(0u, clusters[0].FirstIndex);
        Assert.Equal(clusters[0].IndexCount, clusters[1].FirstIndex);
        Assert.Equal(mesh.Indices.Length, clusters.Sum(cluster => (int)cluster.IndexCount));
        foreach (var cluster in clusters)
        {
            Assert.InRange(cluster.IndexCount, 3u, 384u);
            Vector3 center = new(cluster.Sphere.X, cluster.Sphere.Y, cluster.Sphere.Z);
            foreach (uint index in mesh.Indices.Skip((int)cluster.FirstIndex).Take((int)cluster.IndexCount))
            {
                Assert.True(Vector3.Distance(mesh.Vertices[(int)index].Position, center) <= cluster.Sphere.W);
            }
        }
        Assert.Equal(clusters.ToArray(), NativeMeshClusters3D.Build(mesh).ToArray());
    }

    [Fact]
    public void ResidentAdmissionSnapshotsTheListAndRejectsInvalidStaticContracts()
    {
        var mesh = Triangle();
        var material = new ModelMaterial("opaque");
        var source = new[] { new SceneInstance3D("one", mesh, material, Matrix4x4.Identity) };
        var resident = new NativeInstanceSet3D(source);
        source[0] = source[0] with { Identity = "replaced" };
        Assert.Equal("one", resident[0].Identity);
        Assert.Throws<InvalidDataException>(() => new NativeInstanceSet3D([source[0], source[0]]));
        Assert.Throws<InvalidDataException>(() => new NativeInstanceSet3D([source[0] with { Transform = Matrix4x4.CreateScale(0) }]));
        Assert.Throws<NotSupportedException>(() => new NativeInstanceSet3D([source[0] with { PreviousTransform = Matrix4x4.Identity }]));
    }

    [Fact]
    public void ComputeToIndirectHazardIsRepresentedAndWrongQueueIsRejected()
    {
        var graph = new FrameGraphBuilder();
        var arguments = graph.Resource("arguments", "buffer", 16, 1);
        graph.Pass("cull", 0, FrameQueue.Compute).Write(arguments, FrameAccess.ComputeWrite);
        graph.Pass("draw", 1, sideEffect: true).Read(arguments, FrameAccess.Indirect);
        FramePlan plan = FramePlanCompiler.Compile(graph.Build());
        Assert.Contains(plan.Tape, instruction => instruction.Op == FrameTapeOp.Barrier
            && instruction.Before == FrameAccess.ComputeWrite && instruction.After == FrameAccess.Indirect);
        graph = new();
        arguments = graph.Resource("arguments", "buffer", 16, 1, FrameLifetime.Imported, initialized: true);
        graph.Pass("invalid", 0, FrameQueue.Compute, sideEffect: true).Read(arguments, FrameAccess.Indirect);
        Assert.Equal("AUR-PLAN-005", Assert.Throws<FramePlanException>(() => FramePlanCompiler.Compile(graph.Build())).Code);
    }

    [Fact]
    public void SphereTransformBoundsShearNonuniformScaleAndRotation()
    {
        Vector4 sphere = new(.2f, -.3f, .4f, 2);
        Matrix4x4 shear = new(1, .6f, -.2f, 0, -.3f, 1, .5f, 0, .2f, 0, 1, 0, 4, -7, 2, 1);
        foreach (Matrix4x4 transform in new[] { shear, Matrix4x4.CreateScale(.2f, 3, .7f) * Matrix4x4.CreateRotationY(.7f) })
        {
            Vector4 bound = NativeMeshClusters3D.TransformSphere(sphere, transform);
            Vector3 center = new(sphere.X, sphere.Y, sphere.Z);
            Vector3 transformedCenter = new(bound.X, bound.Y, bound.Z);
            for (int index = 0; index < 512; index++)
            {
                Vector3 direction = Vector3.Normalize(new(MathF.Sin(index * 1.7f), MathF.Cos(index * .9f), MathF.Sin(index * 2.3f)));
                Vector3 point = Vector3.Transform(center + direction * sphere.W, transform);
                Assert.True(Vector3.Distance(point, transformedCenter) <= bound.W);
            }
        }
    }

    [Fact]
    public void ConstantClipPlaneRemainsConservativelyVisible()
    {
        Matrix4x4 camera = Matrix4x4.Identity;
        camera.M14 = -1;
        Assert.True(Matrix4x4.Invert(camera, out _));
        Vector4[] planes = VulkanClusterVisibility3D.Planes(camera);
        Assert.Equal(new Vector4(0, 0, 0, 1), planes[0]);
        Assert.All(planes, plane => Assert.True(float.IsFinite(plane.X) && float.IsFinite(plane.Y)
            && float.IsFinite(plane.Z) && float.IsFinite(plane.W)));
    }

    private static NativeMesh3D Triangle() => new("triangle", [
        new(new(0, 0, 0), Vector3.UnitZ, Vector4.One, Vector2.Zero, new(1, 0, 0, 1)),
        new(new(1, 0, 0), Vector3.UnitZ, Vector4.One, Vector2.Zero, new(1, 0, 0, 1)),
        new(new(0, 1, 0), Vector3.UnitZ, Vector4.One, Vector2.Zero, new(1, 0, 0, 1)),
    ], [0, 1, 2]);
}
