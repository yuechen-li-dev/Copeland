using System.Globalization;
using System.Numerics;
using Aurelian.Assets.Tensors;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Models;

/// <summary>Exposed voxel faces for inspecting actual occupancy. This is a
/// visualization of decoder output, not the learned mesh decoder or materials.</summary>
internal static class Trellis2OccupancyPreview
{
    public static void Render(AurelianVulkanPlant plant, GameAssets assets, SparseVoxelGrid grid, string output)
    {
        var vertices = Surface(grid);
        var material = new ModelMaterial("occupancy-preview")
        {
            BaseColor = new(.65f, .28f, .1f, 1), Roughness = .85f, DoubleSided = true,
        };
        NativeModel3DBatch[] batches = vertices.Chunk(60000).Select(chunk => new NativeModel3DBatch(chunk, material)).ToArray();
        var scene = new Native3DScene([], batches);
        WriteObj(vertices, Path.Combine(output, "occupancy-preview.obj"));
        using var target = new VulkanNativeFrameTarget(plant, 960, 720);
        using var renderer = new VulkanSolid3DRenderer(plant, assets.Shader("Solid3D.v.ts"), target,
            modelProgram: assets.Shader("StaticModel3D.v.ts"), shadowProgram: assets.Shader("Shadow3D.v.ts"),
            outputProgram: assets.Shader("ToneMap3D.v.ts"));
        renderer.Settings = Graphics3DSettings.Default with
        {
            AntiAliasing = AntiAliasing3D.None, BloomIntensity = 0, Shadows = false,
        };
        Vector3[] eyes = [new(1.4f, .7f, 1.8f), new(-1.4f, .7f, -1.8f), new(0, .15f, 2.3f)];
        string[] names = ["occupancy-preview", "occupancy-back", "occupancy-front"];
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(.7f, 960f / 720, .01f, 10);
        projection.M22 *= -1;
        for (int index = 0; index < eyes.Length; index++)
        {
            Vector3 eye = eyes[index];
            Matrix4x4 camera = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY) * projection;
            var frame = renderer.Render(scene, camera, eye, new(.035f, .04f, .055f, 1), capture: true);
            NativeGameGraphics.WritePng(Path.Combine(output, names[index] + ".png"), 960, 720, frame.Pixels!);
        }
    }

    private static NativeModel3DVertex[] Surface(SparseVoxelGrid grid)
    {
        var vertices = new List<NativeModel3DVertex>();
        Vector3[] normals = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
        int[] neighborSlots = [22, 4, 16, 10, 14, 12];
        float cell = 1f / grid.Resolution;
        for (int index = 0; index < grid.Coordinates.Length; index++)
        {
            VoxelCoordinate coordinate = grid.Coordinates[index];
            Vector3 center = new((coordinate.X + .5f) * cell - .5f,
                (coordinate.Y + .5f) * cell - .5f, (coordinate.Z + .5f) * cell - .5f);
            for (int face = 0; face < normals.Length; face++)
            {
                if (grid.Neighbors27[index * 27 + neighborSlots[face]] >= 0)
                {
                    continue;
                }
                Vector3 normal = normals[face];
                Vector3 reference = MathF.Abs(normal.Y) > .5f ? Vector3.UnitZ : Vector3.UnitY;
                Vector3 tangent = Vector3.Cross(reference, normal);
                Vector3 up = Vector3.Cross(normal, tangent);
                Vector3 faceCenter = center + normal * (cell * .5f);
                Vector3 a = faceCenter + (-tangent - up) * (cell * .5f);
                Vector3 b = faceCenter + (tangent - up) * (cell * .5f);
                Vector3 c = faceCenter + (tangent + up) * (cell * .5f);
                Vector3 d = faceCenter + (-tangent + up) * (cell * .5f);
                foreach (Vector3 position in new[] { a, b, c, a, c, d })
                {
                    // TRELLIS render_utils uses Z-up. Preserve raw XYZ topology;
                    // only preview/export crosses to Aurelian's Y-up basis.
                    vertices.Add(new(ToAurelian(position), ToAurelian(normal), Vector4.One,
                        Vector2.Zero, new(ToAurelian(tangent), 1)));
                }
            }
        }
        return vertices.ToArray();
    }

    private static Vector3 ToAurelian(Vector3 value) => new(value.X, value.Z, -value.Y);

    private static void WriteObj(NativeModel3DVertex[] vertices, string path)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("# Actual decoded occupancy exposed faces, XYZ -> XZ(-Y) Y-up; not learned mesh/material output.");
        foreach (NativeModel3DVertex vertex in vertices)
        {
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"v {vertex.Position.X:R} {vertex.Position.Y:R} {vertex.Position.Z:R}"));
        }
        for (int index = 0; index < vertices.Length; index += 3)
        {
            writer.WriteLine($"f {index + 1} {index + 2} {index + 3}");
        }
    }
}
