using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingHybridProof
{
    private static object CheckBoundaries(VulkanSolid3DRenderer renderer, Native3DScene original, string output)
    {
        var source = original.Instances.ToList();
        var triangle = new NativeMesh3D("intersecting-triangles", [
            new(new(-5, -.5f, .5f), Vector3.UnitZ, Vector4.One, Vector2.Zero, new(1, 0, 0, 1)),
            new(new(5, -.5f, -.5f), Vector3.UnitZ, Vector4.One, Vector2.UnitX, new(1, 0, 0, 1)),
            new(new(5, .5f, -.5f), Vector3.UnitZ, Vector4.One, Vector2.One, new(1, 0, 0, 1)),
            new(new(-5, .5f, .5f), Vector3.UnitZ, Vector4.One, Vector2.UnitY, new(1, 0, 0, 1)),
        ], [0, 1, 2, 0, 2, 3]);
        source.Add(new("triangle-surface", triangle, new("triangle") { BaseColor = new(.2f, .6f, .8f, 1), DoubleSided = true }, Matrix4x4.Identity));
        Matrix4x4 shear = Matrix4x4.Identity;
        shear.M21 = .35f;
        source[0] = source[0] with { Transform = Matrix4x4.CreateScale(1.2f, .8f, .7f) * shear * source[0].Transform };
        source[1] = source[1] with { Transform = Matrix4x4.CreateRotationY(.4f) * source[1].Transform };
        Native3DScene scene = new([], []) { Instances = new NativeInstanceSet3D(source) };
        var observations = new List<object>();
        var poses = new[]
        {
            (Eye: new Vector3(0, 2, 8), Target: Vector3.Zero),
            (Eye: new Vector3(1, -.3f, -6), Target: Vector3.Zero),
            (Eye: new Vector3(-2.6f, 0, 0), Target: new Vector3(-2.6f, 0, -2)),
            (Eye: new Vector3(-2.6f, 0, .74f), Target: new Vector3(-2.6f, 0, -2)),
        };
        int index = 0;
        foreach (var pose in poses)
        {
            foreach (bool orthographic in new[] { false, true })
            {
                var projection = orthographic ? Matrix4x4.CreateOrthographic(10, 10f * Height / Width, .1f, 100)
                    : Matrix4x4.CreatePerspectiveFieldOfView(.85f, (float)Width / Height, .1f, 100);
                projection.M22 *= -1;
                var camera = Matrix4x4.CreateLookAt(pose.Eye, pose.Target, Vector3.UnitY) * projection;
                renderer.EnableHierarchicalZ = false;
                renderer.ResetTemporalHistory();
                var reference = renderer.Render(scene, camera, pose.Eye, Clear, capture: true);
                var before = renderer.ReadIdentitiesForQualification();
                NativeGameGraphics.WritePng(Path.Combine(output, "oracle-" + index + ".png"), Width, Height, reference.Pixels!);
                renderer.EnableHierarchicalZ = true;
                renderer.ResetTemporalHistory();
                var actual = renderer.Render(scene, camera, pose.Eye, Clear, capture: true);
                var after = renderer.ReadIdentitiesForQualification();
                Require(reference.PixelSha256 == actual.PixelSha256 && before.Ids.SequenceEqual(after.Ids), "HZB changed mixed analytic/triangle hits.");
                object oracle = CheckOracle(scene, camera, after, renderer.ReadHzbForQualification().History);
                observations.Add(new { Case = index++, Orthographic = orthographic, Eye = pose.Eye, Oracle = oracle, HzbExact = true });
            }
        }
        renderer.EnableHierarchicalZ = false;
        Vector3 eye = new(0, 2, 8);
        var view = RenderingBindlessProof.Camera(eye, Vector3.Zero);
        var material = RenderingBindlessProof.Materials(1)[0] with { AlphaMask = false, DoubleSided = true };
        Native3DScene mapped = original with { Instances = new NativeInstanceSet3D(original.Instances.Select(instance => instance with { Material = material })) };
        object appearance = CompareMeshes(renderer, mapped, view, eye, output, "mapped-surfaces", 96);
        renderer.Settings = renderer.Settings with { AntiAliasing = AntiAliasing3D.Temporal };
        for (int frame = 0; frame < 8; frame++)
        {
            Vector3 movingEye = eye + new Vector3(frame * .04f, 0, 0);
            renderer.Render(scene, RenderingBindlessProof.Camera(movingEye, Vector3.Zero), movingEye, Clear);
        }
        renderer.WaitForPendingFrames();
        renderer.Settings = renderer.Settings with { AntiAliasing = AntiAliasing3D.None };
        renderer.Settings = renderer.Settings with { Shadows = true };
        bool shadowRejected = false;
        try { renderer.Render(scene, view, eye, Clear); }
        catch (NotSupportedException error) when (error.Message.Contains("AUR-HYBRID-001", StringComparison.Ordinal)) { shadowRejected = true; }
        renderer.Settings = renderer.Settings with { Shadows = false };
        Require(shadowRejected, "Analytic shadow proxy was not rejected.");
        return new { Oracles = observations, Appearance = appearance, QueuedTemporalFrames = 8, ShadowProxyRejected = shadowRejected };
    }

    private static object CompareMeshes(VulkanSolid3DRenderer renderer, Native3DScene scene, Matrix4x4 camera,
        Vector3 eye, string output, string name, int slices)
    {
        var meshes = new Dictionary<AnalyticPrimitiveKind3D, NativeMesh3D>();
        Native3DScene meshed = scene with { Instances = new NativeInstanceSet3D(scene.Instances.Select(instance =>
        {
            if (instance.Mesh.PrimitiveKind == AnalyticPrimitiveKind3D.Triangles) return instance;
            if (!meshes.TryGetValue(instance.Mesh.PrimitiveKind, out var mesh))
                meshes.Add(instance.Mesh.PrimitiveKind, mesh = Mesh(instance.Mesh.PrimitiveKind, slices));
            return instance with { Mesh = mesh };
        })) };
        renderer.EnableHierarchicalZ = false;
        renderer.ResetTemporalHistory();
        var reference = renderer.Render(meshed, camera, eye, Clear, capture: true);
        var referenceIds = renderer.ReadIdentitiesForQualification().Ids;
        renderer.ResetTemporalHistory();
        var actual = renderer.Render(scene, camera, eye, Clear, capture: true);
        var actualIds = renderer.ReadIdentitiesForQualification().Ids;
        long difference = 0;
        int coverage = 0;
        for (int pixel = 0; pixel < Width * Height; pixel++)
        {
            for (int channel = 0; channel < 3; channel++)
                difference += Math.Abs(reference.Pixels![pixel * 4 + channel] - actual.Pixels![pixel * 4 + channel]);
            if ((referenceIds[pixel * 2] == 0) != (actualIds[pixel * 2] == 0)) coverage++;
        }
        double error = difference / (Width * Height * 3d);
        double coverageFraction = coverage / (Width * Height * 1d);
        NativeGameGraphics.WritePng(Path.Combine(output, name + "-mesh.png"), Width, Height, reference.Pixels!);
        NativeGameGraphics.WritePng(Path.Combine(output, name + "-analytic.png"), Width, Height, actual.Pixels!);
        var result = new { Name = name, Slices = slices, MeanRgbError = error, ChangedCoverage = coverageFraction };
        File.WriteAllText(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(result));
        Require(error < 1.2 && coverageFraction < .005, name + ": mesh comparison exceeded fixed image-quality gates: " + error);
        return result;
    }
}
