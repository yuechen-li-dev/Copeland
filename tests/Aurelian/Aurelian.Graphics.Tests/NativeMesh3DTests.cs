using System.Numerics;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class NativeMesh3DTests
{
    [Fact]
    public void MeshOwnsAnImmutableSnapshotOfValidatedGeometry()
    {
        NativeModel3DVertex[] vertices = Triangle();
        uint[] indices = [0, 1, 2];
        var mesh = new NativeMesh3D("triangle", vertices, indices);
        vertices[0] = vertices[0] with { Position = new(float.NaN, 0, 0) };
        indices[0] = 500;
        Assert.Equal(Vector3.Zero, mesh.Vertices[0].Position);
        Assert.Equal(0u, mesh.Indices[0]);
    }

    [Fact]
    public void InvalidTrianglesAndNonfiniteVerticesAreRejectedBeforeGpuAllocation()
    {
        Assert.Throws<InvalidDataException>(() => new NativeMesh3D("bad-index", Triangle(), [0, 1, 3]));
        Assert.Throws<InvalidDataException>(() => new NativeMesh3D("incomplete", Triangle(), [0, 1]));
        NativeModel3DVertex[] vertices = Triangle();
        vertices[1] = vertices[1] with { Normal = new(float.PositiveInfinity, 0, 0) };
        Assert.Throws<InvalidDataException>(() => new NativeMesh3D("nonfinite", vertices, [0, 1, 2]));
    }

    [Fact]
    public void CompatibilityExpansionUsesTheInstanceTransformAndMeshIndices()
    {
        var mesh = new NativeMesh3D("triangle", Triangle(), [2, 0, 1]);
        var instance = new SceneInstance3D("instance", mesh, new ModelMaterial("material"),
            Matrix4x4.CreateScale(2, 3, 4) * Matrix4x4.CreateTranslation(5, 6, 7));
        NativeModel3DBatch batch = instance.Expand();
        Assert.Equal(new Vector3(5, 9, 7), batch.Vertices[0].Position);
        Assert.Equal(new Vector3(5, 6, 7), batch.Vertices[1].Position);
        Assert.Equal(new Vector3(7, 6, 7), batch.Vertices[2].Position);
        Assert.Equal(Vector3.UnitZ, batch.Vertices[0].Normal);
        Assert.Equal("instance", batch.TemporalIdentity);
    }

    private static NativeModel3DVertex[] Triangle() =>
    [
        new(Vector3.Zero, Vector3.UnitZ, Vector4.One, Vector2.Zero, new(1, 0, 0, 1)),
        new(Vector3.UnitX, Vector3.UnitZ, Vector4.One, Vector2.UnitX, new(1, 0, 0, 1)),
        new(Vector3.UnitY, Vector3.UnitZ, Vector4.One, Vector2.UnitY, new(1, 0, 0, 1)),
    ];
}
