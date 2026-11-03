using System.Numerics;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.World.Scenes;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.NativeComposition;

/// <summary>Projects semantic scene geometry into the existing native 3D renderer's vertices.</summary>
public static class SceneGeometry3D
{
    public static Native3DScene BuildScene(SceneFrame frame, SceneGeometry3DCache? retained = null)
    {
        retained?.Begin();
        var instances = new List<SceneInstance3D>();
        var batches = new List<NativeModel3DBatch>();
        foreach (PlacedSceneModel instance in frame.Models)
        {
            StaticModel asset = instance.Asset.Current;
            foreach (ModelOccurrence occurrence in asset.Occurrences)
            {
                Matrix4x4 world = occurrence.Transform * instance.WorldTransform;
                ModelPrimitive primitive = occurrence.Primitive;
                ModelMaterial selected = instance.Materials.TryGetValue(primitive.Material.Slot, out ModelMaterial? customized)
                    ? customized : primitive.Material;
                if (retained is not null && !selected.AlphaBlend && selected.Transmission == 0)
                {
                    NativeMesh3D mesh = retained.Get(primitive, () => NativeMesh3D.FromPrimitive(primitive));
                    instances.Add(new(instance.Id + "/" + asset.ContentIdentity + "/" + occurrence.Id, mesh, selected, world));
                    continue;
                }
                Matrix4x4 normals = NormalTransform(world);
                var vertices = new NativeModel3DVertex[primitive.Indices.Length];
                for (int index = 0; index < vertices.Length; index++)
                {
                    ModelVertex vertex = primitive.Vertices[primitive.Indices[index]];
                    Vector3 normal = Vector3.Normalize(Vector3.TransformNormal(vertex.Normal, normals));
                    Vector3 tangent = Vector3.TransformNormal(new(vertex.Tangent.X, vertex.Tangent.Y, vertex.Tangent.Z), world);
                    tangent = Vector3.Normalize(tangent - normal * Vector3.Dot(normal, tangent));
                    vertices[index] = new(Vector3.Transform(vertex.Position, world), normal, vertex.Color, vertex.Uv,
                        new(tangent, vertex.Tangent.W));
                }
                batches.Add(new(vertices, selected) { TemporalIdentity = instance.Id + "/" + asset.ContentIdentity + "/" + occurrence.Id });
            }
        }
        string revision = string.Join("|", frame.Boxes.Select(box => box.Id + ":" + box.HalfSize))
            + string.Join("|", frame.Meshes.Select(mesh => mesh.Id + ":" + (mesh.GeometryIdentity ?? SceneCompiler.ComputeGeometryIdentity(mesh.Vertices, mesh.Indices))))
            + string.Join("|", batches.Select(batch => batch.TemporalIdentity))
            + string.Join("|", instances.Select(instance => instance.Identity));
        foreach (PlacedSceneBox box in frame.Boxes.Where(box => box.Material is not null))
        {
            ValidatePrimitiveMaterial(box.Material!);
            var local = new List<Native3DVertex>();
            PrimitiveGeometry3D.AddBox(local, Vector3.Zero, box.HalfSize, box.Color);
            if (retained is not null && !box.Material!.AlphaBlend && box.Material.Transmission == 0)
            {
                var mesh = retained.Get(("box", box.HalfSize, box.Color), () => MeshFromBatch(
                    MaterialBatch(box.Id, local, Matrix4x4.Identity, box.Material)));
                instances.Add(new(box.Id, mesh, box.Material, box.WorldTransform));
            }
            else
            {
                batches.Add(MaterialBatch(box.Id, local, box.WorldTransform, box.Material!));
            }
        }
        foreach (PlacedSceneMesh mesh in frame.Meshes.Where(mesh => mesh.Material is not null))
        {
            ValidatePrimitiveMaterial(mesh.Material!);
            IEnumerable<SceneVertex> local = mesh.Indices.IsEmpty ? mesh.Vertices : mesh.Indices.Select(index => mesh.Vertices[index]);
            if (retained is not null && !mesh.Material!.AlphaBlend && mesh.Material.Transmission == 0)
            {
                var geometry = retained.Get((mesh.Vertices, mesh.Indices), () => MeshFromBatch(MaterialBatch(mesh.Id,
                    local.Select(vertex => new Native3DVertex(vertex.Position, vertex.Normal, vertex.Color)),
                    Matrix4x4.Identity, mesh.Material)));
                instances.Add(new(mesh.Id, geometry, mesh.Material, mesh.WorldTransform));
            }
            else
            {
                batches.Add(MaterialBatch(mesh.Id, local.Select(vertex => new Native3DVertex(vertex.Position, vertex.Normal, vertex.Color)),
                    mesh.WorldTransform, mesh.Material!));
            }
        }
        string lightingRevision = revision
            + string.Join("|", frame.Boxes.Select(box => box.Id + ":" + box.WorldTransform))
            + string.Join("|", frame.Meshes.Select(mesh => mesh.Id + ":" + mesh.WorldTransform))
            + string.Join("|", frame.Models.Select(model => model.Id + ":" + model.WorldTransform));
        retained?.End();
        return new(Build(frame, includeMaterialGeometry: false), batches)
        {
            Instances = instances,
            TemporalRevision = revision,
            LightingRevision = lightingRevision,
        };
    }

    private static NativeMesh3D MeshFromBatch(NativeModel3DBatch batch)
    {
        return new(batch.TemporalIdentity ?? batch.Material.Slot, batch.Vertices,
            Enumerable.Range(0, batch.Vertices.Length).Select(index => (uint)index));
    }

    public static Native3DVertex[] Build(SceneFrame frame) => Build(frame, includeMaterialGeometry: true);

    private static Native3DVertex[] Build(SceneFrame frame, bool includeMaterialGeometry)
    {
        var result = new List<Native3DVertex>();
        foreach (PlacedSceneBox box in frame.Boxes)
        {
            if (!includeMaterialGeometry && box.Material is not null) continue;
            var local = new List<Native3DVertex>();
            PrimitiveGeometry3D.AddBox(local, Vector3.Zero, box.HalfSize, box.Color);
            var normalTransform = NormalTransform(box.WorldTransform);
            foreach (Native3DVertex vertex in local)
            {
                result.Add(Transform(vertex, box.WorldTransform, normalTransform));
            }
        }
        foreach (PlacedSceneMesh mesh in frame.Meshes)
        {
            if (!includeMaterialGeometry && mesh.Material is not null) continue;
            var normalTransform = NormalTransform(mesh.WorldTransform);
            IEnumerable<SceneVertex> vertices = mesh.Indices.IsEmpty
                ? mesh.Vertices
                : mesh.Indices.Select(index => mesh.Vertices[index]);
            foreach (SceneVertex vertex in vertices)
            {
                result.Add(Transform(new(vertex.Position, vertex.Normal, vertex.Color),
                    mesh.WorldTransform, normalTransform));
            }
        }
        return result.ToArray();
    }

    private static NativeModel3DBatch MaterialBatch(string id, IEnumerable<Native3DVertex> local,
        Matrix4x4 world, ModelMaterial material)
    {
        // Primitive authoring supplies planar UVs in local scene metres. Explicit
        // UVs and tangent frames remain available through imported Scene.Model assets.
        ValidatePrimitiveMaterial(material);
        Matrix4x4 normals = NormalTransform(world);
        var vertices = new List<NativeModel3DVertex>();
        foreach (Native3DVertex vertex in local)
        {
            Vector3 normal = Vector3.Normalize(vertex.Normal);
            Vector3 axis = MathF.Abs(normal.Y) < .9f ? Vector3.UnitY : Vector3.UnitZ;
            Vector3 tangent = Vector3.Normalize(Vector3.Cross(axis, normal));
            Vector3 bitangent = Vector3.Cross(normal, tangent);
            Vector2 uv = new(Vector3.Dot(vertex.Position, tangent), Vector3.Dot(vertex.Position, bitangent));
            Native3DVertex transformed = Transform(vertex, world, normals);
            Vector3 worldTangent = Vector3.Normalize(Vector3.TransformNormal(tangent, world));
            worldTangent = Vector3.Normalize(worldTangent - transformed.Normal * Vector3.Dot(transformed.Normal, worldTangent));
            vertices.Add(new(transformed.Position, transformed.Normal, transformed.Color, uv, new(worldTangent, 1)));
        }
        return new(vertices.ToArray(), material) { TemporalIdentity = id };
    }

    private static void ValidatePrimitiveMaterial(ModelMaterial material)
    {
        if (material.NormalTexture is not null)
        {
            throw new InvalidDataException("Primitive normal maps require explicit UV/tangent geometry through Scene.Model.");
        }
    }

    private static Matrix4x4 NormalTransform(Matrix4x4 world)
    {
        SceneTransform.ValidateMatrix(world);
        Matrix4x4.Invert(world, out Matrix4x4 inverse);
        return Matrix4x4.Transpose(inverse);
    }

    private static Native3DVertex Transform(Native3DVertex vertex, Matrix4x4 world, Matrix4x4 normals)
    {
        return new(Vector3.Transform(vertex.Position, world),
            Vector3.Normalize(Vector3.TransformNormal(vertex.Normal, normals)), vertex.Color);
    }
}
