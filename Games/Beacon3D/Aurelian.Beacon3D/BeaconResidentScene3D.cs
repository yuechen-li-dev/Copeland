using Aurelian.Runtime;
using System.Numerics;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Scenes;

namespace Aurelian.Beacon3D;

internal abstract class BeaconGeometry
{
    public abstract void Box(string identity, Vector3 center, Vector3 halfSize, Vector4 color, Matrix4x4? world = null, ModelMaterial? material = null);
    public abstract void Diamond(string identity, Vector3 center, float rotation, Vector4 color, float scale = 1);
    public abstract void ViewBox(string identity, Vector3 center, Vector3 right, Vector3 up, Vector3 forward, Vector3 halfSize, Vector4 color);
    public abstract void Mesh(PlacedSceneMesh mesh);
    public abstract void Deformed(Native3DVertex[] vertices);
}

internal sealed class BeaconVertexGeometry : BeaconGeometry
{
    public List<Native3DVertex> Vertices { get; } = [];

    public override void Box(string identity, Vector3 center, Vector3 halfSize, Vector4 color, Matrix4x4? world = null, ModelMaterial? material = null)
    {
        if (world is null)
        {
            BeaconScene.AddBox(Vertices, center, halfSize, color);
            return;
        }
        var local = new List<Native3DVertex>();
        BeaconScene.AddBox(local, center, halfSize, color);
        Matrix4x4.Invert(world.Value, out var inverse);
        var normals = Matrix4x4.Transpose(inverse);
        foreach (var vertex in local)
        {
            Vertices.Add(vertex with { Position = Vector3.Transform(vertex.Position, world.Value),
                Normal = Vector3.Normalize(Vector3.TransformNormal(vertex.Normal, normals)) });
        }
    }

    public override void Diamond(string identity, Vector3 center, float rotation, Vector4 color, float scale = 1) =>
        BeaconScene.AddDiamond(Vertices, center, rotation, color, scale);

    public override void ViewBox(string identity, Vector3 center, Vector3 right, Vector3 up, Vector3 forward, Vector3 halfSize, Vector4 color) =>
        BeaconScene.AddViewBox(Vertices, center, right, up, forward, halfSize, color);

    public override void Mesh(PlacedSceneMesh mesh) => Vertices.AddRange(SceneGeometry3D.Build(new([], [mesh])));
    public override void Deformed(Native3DVertex[] vertices) => Vertices.AddRange(vertices);
}

/// <summary>Resident projection of the same Beacon authoring used by the legacy vertex path.
/// Topology/material changes publish a static revision; unchanged keys retain explicit motion.</summary>
public sealed class BeaconResidentScene3D
{
    private readonly ResidentGeometry geometry = new();
    private NativeInstanceSet3D? previous;

    public Native3DScene Build(BeaconGame game, FirstPersonPose3D? pose = null)
    {
        geometry.Items.Clear();
        BeaconScene.Describe(game, geometry, pose);
        var items = geometry.Items;
        bool continues = previous is not null && previous.Count == items.Count;
        if (continues)
        {
            for (int index = 0; index < items.Count; index++)
            {
                var current = items[index];
                var prior = previous![index];
                if (current.Identity != prior.Identity || !ReferenceEquals(current.Mesh, prior.Mesh) || current.Material != prior.Material)
                {
                    continues = false;
                    break;
                }
            }
        }
        previous = new NativeInstanceSet3D(items, continues ? previous : null);
        return new([], []) { Instances = previous };
    }

    private sealed class ResidentGeometry : BeaconGeometry
    {
        private readonly NativeMesh3D box;
        private readonly NativeMesh3D diamond;
        private readonly Dictionary<string, List<(NativeMesh3D Mesh, ModelMaterial Material)>> meshGroups = new();
        private readonly Dictionary<(Vector4 Color, ModelMaterial? Authored), ModelMaterial> materials = new();
        public List<SceneInstance3D> Items { get; } = [];

        public ResidentGeometry()
        {
            var vertices = new List<Native3DVertex>();
            BeaconScene.AddBox(vertices, Vector3.Zero, Vector3.One, Vector4.One);
            box = MakeMesh("beacon/unit-box", vertices);
            vertices.Clear();
            BeaconScene.AddDiamond(vertices, Vector3.Zero, 0, Vector4.One);
            diamond = MakeMesh("beacon/unit-diamond", vertices);
        }

        public override void Box(string identity, Vector3 center, Vector3 halfSize, Vector4 color, Matrix4x4? world = null, ModelMaterial? material = null)
        {
            var transform = Matrix4x4.CreateScale(halfSize) * Matrix4x4.CreateTranslation(center);
            if (world.HasValue) transform *= world.Value;
            Items.Add(new(identity, box, Material(color, material), transform));
        }

        public override void Diamond(string identity, Vector3 center, float rotation, Vector4 color, float scale = 1)
        {
            var transform = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationY(-rotation) * Matrix4x4.CreateTranslation(center);
            Items.Add(new(identity, diamond, Material(color), transform));
        }

        public override void ViewBox(string identity, Vector3 center, Vector3 right, Vector3 up, Vector3 forward, Vector3 halfSize, Vector4 color)
        {
            var basis = new Matrix4x4(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0,
                -forward.X, -forward.Y, -forward.Z, 0, center.X, center.Y, center.Z, 1);
            Items.Add(new(identity, box, Material(color), Matrix4x4.CreateScale(halfSize) * basis));
        }

        public override void Mesh(PlacedSceneMesh mesh)
        {
            if (mesh.Material is not null)
                throw new NotSupportedException("BEACON-GI-001: This projection expects Beacon's authored vertex-color meshes.");
            string key = mesh.GeometryIdentity ?? SceneCompiler.ComputeGeometryIdentity(mesh.Vertices, mesh.Indices);
            if (meshGroups.TryGetValue(key, out var cached))
            {
                AddMeshGroups(mesh.Id, mesh.WorldTransform, cached);
                return;
            }
            var source = mesh.Indices.IsEmpty ? mesh.Vertices.ToArray() : mesh.Indices.Select(index => mesh.Vertices[index]).ToArray();
            var colors = new Dictionary<Vector4, List<Native3DVertex>>();
            for (int offset = 0; offset < source.Length; offset += 3)
            {
                Vector4 color = source[offset].Color;
                if (source[offset + 1].Color != color || source[offset + 2].Color != color)
                    throw new NotSupportedException("BEACON-GI-001: Beacon's reference-qualified meshes require flat triangle colors.");
                if (!colors.TryGetValue(color, out var vertices))
                {
                    vertices = [];
                    colors.Add(color, vertices);
                }
                for (int lane = 0; lane < 3; lane++)
                {
                    var vertex = source[offset + lane];
                    vertices.Add(new(vertex.Position, vertex.Normal, Vector4.One));
                }
            }
            var groups = new List<(NativeMesh3D Mesh, ModelMaterial Material)>();
            foreach (var pair in colors)
            {
                var geometry = MakeMesh("beacon/" + key + "/" + groups.Count, pair.Value);
                groups.Add((geometry, Material(pair.Key)));
            }
            meshGroups.Add(key, groups);
            AddMeshGroups(mesh.Id, mesh.WorldTransform, groups);
        }

        private void AddMeshGroups(string identity, Matrix4x4 world, List<(NativeMesh3D Mesh, ModelMaterial Material)> groups)
        {
            for (int group = 0; group < groups.Count; group++)
            {
                var item = groups[group];
                Items.Add(new("arena/" + identity + "/" + group, item.Mesh, item.Material, world));
            }
        }

        public override void Deformed(Native3DVertex[] vertices) =>
            throw new NotSupportedException("BEACON-GI-002: Deforming humanoid geometry needs shared ray-scene skinning admission before GI.");

        private ModelMaterial Material(Vector4 color, ModelMaterial? authored = null)
        {
            if (!materials.TryGetValue((color, authored), out var material))
            {
                if (authored is null)
                {
                    material = new("beacon/color/" + materials.Count)
                    {
                        BaseColor = color, Metallic = 0, Roughness = 1, DoubleSided = true,
                    };
                }
                else
                {
                    material = authored with { BaseColor = authored.BaseColor * color };
                }
                materials.Add((color, authored), material);
            }
            return material;
        }

        private static NativeMesh3D MakeMesh(string identity, List<Native3DVertex> source)
        {
            // PrimitiveGeometry's raster-compatible faces can have opposite winding to their normals.
            // Shared ray visibility needs the authored outward normal and winding to agree.
            for (int offset = 0; offset < source.Count; offset += 3)
            {
                if (Vector3.Dot(Vector3.Cross(source[offset + 1].Position - source[offset].Position,
                    source[offset + 2].Position - source[offset].Position), source[offset].Normal) < 0)
                    (source[offset + 1], source[offset + 2]) = (source[offset + 2], source[offset + 1]);
            }
            return new(identity, source.Select(vertex => new NativeModel3DVertex(vertex.Position, vertex.Normal,
                Vector4.One, Vector2.Zero, Tangent(vertex.Normal))), Enumerable.Range(0, source.Count).Select(index => (uint)index));
        }

        private static Vector4 Tangent(Vector3 normal)
        {
            Vector3 axis = MathF.Abs(normal.X) < .8f ? Vector3.UnitX : Vector3.UnitZ;
            Vector3 tangent = Vector3.Normalize(axis - normal * Vector3.Dot(axis, normal));
            return new(tangent, 1);
        }
    }
}
