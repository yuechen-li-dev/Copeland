using System.Numerics;
using Aurelian.Games;
using Aurelian.Graphics.Vulkan.Native3D;
using Xunit;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.Beacon3D.Tests;

public sealed class BeaconResidentSceneTests
{
    [Fact]
    public void WholeArenaProjectionPreservesAuthoredGeometryAndKeepsResidentMeshesUnderMotion()
    {
        using var shell = new BeaconShellGame();
        var game = shell.World;
        var presenter = new BeaconResidentScene3D();
        var raw = BeaconScene.Build(game);
        var first = presenter.Build(game);
        var expanded = first.Instances.SelectMany(instance => instance.Expand().Vertices.Select(vertex =>
            new Native3DVertex(vertex.Position, vertex.Normal, vertex.Color * instance.Material.BaseColor))).ToArray();
        Assert.Equal(raw.Length, expanded.Length);
        // Resident groups may reorder triangles and fix winding; compare all authored vertex attributes.
        string VectorKey(Vector3 vector) => $"{(int)MathF.Round(vector.X * 10000)},{(int)MathF.Round(vector.Y * 10000)},{(int)MathF.Round(vector.Z * 10000)}";
        string Key(Native3DVertex vertex) => $"{VectorKey(vertex.Position)}:{VectorKey(vertex.Normal)}:{vertex.Color}";
        Assert.Equal(raw.Select(Key).Distinct().Order(), expanded.Select(Key).Distinct().Order());
        var emitter = Assert.Single(first.Instances, instance => instance.Material.Emissive != Vector3.Zero);
        Assert.Equal(new Vector3(12, 7, 3), emitter.Material.Emissive);
        var old = Assert.IsType<NativeInstanceSet3D>(first.Instances);
        var still = Assert.IsType<NativeInstanceSet3D>(presenter.Build(game).Instances);
        Assert.True(still.ContinuesFrom(old));
        Assert.False(still.HasRaySceneChanges);
        game.Step(new(0, 0, .2f, 0), 1f / 60);
        var moved = Assert.IsType<NativeInstanceSet3D>(presenter.Build(game).Instances);
        Assert.True(moved.ContinuesFrom(still));
        Assert.True(moved.HasRaySceneChanges);
        Assert.Equal(old.Select(instance => instance.Mesh), moved.Select(instance => instance.Mesh));
        Assert.All(moved, instance =>
        {
            var mesh = instance.Mesh;
            for (int offset = 0; offset < mesh.Indices.Length; offset += 3)
            {
                var a = mesh.Vertices[(int)mesh.Indices[offset]];
                var b = mesh.Vertices[(int)mesh.Indices[offset + 1]];
                var c = mesh.Vertices[(int)mesh.Indices[offset + 2]];
                Assert.True(Vector3.Dot(Vector3.Cross(b.Position - a.Position, c.Position - a.Position), a.Normal) > 0);
            }
        });
    }

    [Fact]
    public void GiOptionsUseTheSameResidentArenaAndAuthoredPhysicalLighting()
    {
        using var full = new BeaconShellGame(dynamicGi: true);
        using var regional = new BeaconShellGame(regionalGi: true);
        Assert.Equal(DirectDiffuseModel3D.Lambert, full.Manifest.Graphics.DirectDiffuseModel);
        Assert.Equal(BeaconDiffuseLighting3D.Settings, regional.Manifest.Graphics);
        Assert.IsType<NativeInstanceSet3D>(full.View(new(960, 600)).Scene.Instances);
        Assert.IsType<NativeInstanceSet3D>(regional.View(new(960, 600)).Scene.Instances);
        BeaconDiffuseLighting3D.Runtime.Validate();
        Assert.Equal(432, BeaconDiffuseLighting3D.Grid.Count);
    }

    [Fact]
    public void GameplayTopologyChangesPublishANewStaticRevision()
    {
        using var game = new BeaconGame();
        var presenter = new BeaconResidentScene3D();
        var first = Assert.IsType<NativeInstanceSet3D>(presenter.Build(game).Instances);
        for (int frame = 0; frame < 100; frame++) game.Step(default, 1f / 60);
        var spawned = Assert.IsType<NativeInstanceSet3D>(presenter.Build(game).Instances);
        Assert.True(game.Creatures.Count > 0);
        Assert.False(spawned.ContinuesFrom(first));
        Assert.True(spawned.Count > first.Count);
    }
}
