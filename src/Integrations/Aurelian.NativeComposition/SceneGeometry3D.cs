using System.Numerics;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.World.Scenes;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.NativeComposition;

/// <summary>Projects semantic scene geometry into the existing native 3D renderer's vertices.</summary>
public static class SceneGeometry3D
{
    public static Native3DScene BuildScene(SceneFrame frame)
    {
        var batches = new List<NativeModel3DBatch>();
        foreach (PlacedSceneModel instance in frame.Models)
        {
            StaticModel asset = instance.Asset.Current;
            foreach (ModelOccurrence occurrence in asset.Occurrences)
            {
                Matrix4x4 world = occurrence.Transform * instance.WorldTransform;
                Matrix4x4 normals = NormalTransform(world);
                ModelPrimitive primitive = occurrence.Primitive;
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
                ModelMaterial material = instance.Materials.TryGetValue(primitive.Material.Slot, out ModelMaterial? customized)
                    ? customized : primitive.Material;
                batches.Add(new(vertices, material));
            }
        }
        return new(Build(frame), batches);
    }

    public static Native3DVertex[] Build(SceneFrame frame)
    {
        var result = new List<Native3DVertex>();
        foreach (PlacedSceneBox box in frame.Boxes)
        {
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
