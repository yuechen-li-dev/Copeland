using System.Numerics;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.World.Scenes;

namespace Aurelian.NativeComposition;

/// <summary>Projects semantic scene geometry into the existing native 3D renderer's vertices.</summary>
public static class SceneGeometry3D
{
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
            foreach (SceneVertex vertex in mesh.Vertices)
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
