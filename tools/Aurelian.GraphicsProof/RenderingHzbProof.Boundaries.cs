using System.Numerics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Aurelian.Games;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingHzbProof
{
    private static object CheckBoundaries(AurelianVulkanPlant plant, GameAssets assets, string output)
    {
        using var referenceTarget = new VulkanNativeFrameTarget(plant, 641, 359);
        using var hzbTarget = new VulkanNativeFrameTarget(plant, 641, 359);
        using var reference = RenderingBindlessProof.CreateRenderer(plant, assets, referenceTarget, enableCulling: true);
        using var hzb = RenderingBindlessProof.CreateRenderer(plant, assets, hzbTarget, enableCulling: true);
        reference.EnableVisibilityBuffer = hzb.EnableVisibilityBuffer = true;
        hzb.EnableHierarchicalZ = true;
        reference.Settings = hzb.Settings = Graphics3DSettings.Default with
        {
            Shadows = false, AntiAliasing = AntiAliasing3D.None, BloomIntensity = 0, AmbientOcclusionStrength = 0,
        };
        reference.FrameMode = hzb.FrameMode = Native3DFrameMode.Interactive;
        var scene = Forest(true);
        var records = new List<BoundaryResult>();
        var eyes = new[] { new Vector3(0, 3, 8), new Vector3(28, 4, 8), new Vector3(-30, 2, -8),
            new Vector3(0, 2, 2), new Vector3(0, 3, 8), new Vector3(0, 1, -20), new Vector3(35, 5, 12) };
        foreach (var eye in eyes)
        {
            Matrix4x4 camera = RenderingBindlessProof.Camera(eye, new(0, 1.4f, -30));
            records.Add(Compare(reference, hzb, scene, camera, eye, "camera-" + records.Count));
        }
        var masked = Forest(true, mask: true);
        Vector3 stationary = new(0, 3, 8);
        var stationaryCamera = RenderingBindlessProof.Camera(stationary, new(0, 1.4f, -20));
        for (int frame = 0; frame < 3; frame++)
            records.Add(Compare(reference, hzb, masked, stationaryCamera, stationary, "mask-holes-" + frame));
        // A scene edit invalidates temporal occlusion state and can reveal every previously hidden object.
        records.Add(Compare(reference, hzb, Forest(false), stationaryCamera, stationary, "occluder-removed"));
        var mesh = new NativeMesh3D("tie", [
            new(new(-1, -1, 0), Vector3.UnitZ, Vector4.One, Vector2.Zero, new(1, 0, 0, 1)),
            new(new(1, -1, 0), Vector3.UnitZ, Vector4.One, Vector2.UnitX, new(1, 0, 0, 1)),
            new(new(0, 1, 0), Vector3.UnitZ, Vector4.One, Vector2.UnitY, new(1, 0, 0, 1)),
        ], [0, 1, 2]);
        var material = new ModelMaterial("tie-first") { DoubleSided = true, Unlit = true, BaseColor = new(1, .2f, .1f, 1) };
        Native3DScene tied = new([], []) { Instances = new NativeInstanceSet3D([
            new("first", mesh, material, Matrix4x4.Identity),
            new("second", mesh, material with { Slot = "tie-second", BaseColor = new(.1f, .2f, 1, 1) }, Matrix4x4.Identity),
        ]) };
        for (int frame = 0; frame < 3; frame++)
            records.Add(Compare(reference, hzb, tied, stationaryCamera, stationary, "coplanar-order-" + frame));
        Require(records.Skip(1).Take(6).Any(record => record.FinalDraws > record.SeedDraws),
            "Camera movement did not exercise current-frame rescue.");
        hzb.Render(scene, stationaryCamera, stationary, Clear, capture: true);
        hzb.Render(scene, stationaryCamera, stationary, Clear, capture: true);
        hzb.Render(scene, stationaryCamera, stationary, Clear);
        var cutEye = new Vector3(28, 4, 8);
        var cutCamera = RenderingBindlessProof.Camera(cutEye, new(0, 1.4f, -30));
        hzb.Render(scene, cutCamera, cutEye, Clear);
        var queued = hzb.Render(scene, stationaryCamera, stationary, Clear, capture: true);
        var queuedReference = reference.Render(scene, stationaryCamera, stationary, Clear, capture: true);
        Require(queued.PixelSha256 == queuedReference.PixelSha256, "Queued camera cuts used stale HZB state.");
        reference.Settings = hzb.Settings = Graphics3DSettings.Default with { BloomIntensity = 0, AmbientOcclusionStrength = 0 };
        reference.ResetTemporalHistory();
        hzb.ResetTemporalHistory();
        records.Add(Compare(reference, hzb, tied, stationaryCamera, stationary, "shadows-temporal"));
        hzb.Render(scene, stationaryCamera, stationary, Clear);
        hzb.Render(masked, stationaryCamera, stationary, Clear);
        var actual = hzb.Render(tied, stationaryCamera, stationary, Clear, capture: true);
        reference.ResetTemporalHistory();
        var reset = reference.Render(tied, stationaryCamera, stationary, Clear, capture: true);
        Require(actual.PixelSha256 == reset.PixelSha256, "In-flight HZB revision retirement changed pixels.");
        NativeGameGraphics.WritePng(Path.Combine(output, "coplanar.png"), 641, 359, actual.Pixels!);
        return new { Cases = records, QueuedCameraCuts = queued.PixelSha256, InFlightRevision = actual.PixelSha256 };
    }

    private sealed record BoundaryResult(string Name, string? PixelSha256, string IdentitySha256,
        long FinalDraws, int SeedDraws, object Classification);

    private static BoundaryResult Compare(VulkanSolid3DRenderer reference, VulkanSolid3DRenderer hzb,
        Native3DScene scene, Matrix4x4 camera, Vector3 eye, string name)
    {
        var expected = reference.Render(scene, camera, eye, Clear, capture: true);
        var actual = hzb.Render(scene, camera, eye, Clear, capture: true);
        var expectedIds = reference.ReadIdentitiesForQualification();
        var actualIds = hzb.ReadIdentitiesForQualification();
        Require(expectedIds.Ids.SequenceEqual(actualIds.Ids), name + ": HZB lost or changed a visible triangle.");
        Require(expected.PixelSha256 == actual.PixelSha256, name + ": HZB changed final pixels.");
        var pyramid = hzb.ReadHzbForQualification();
        CheckPyramid(pyramid);
        var groups = hzb.ReadVisibilityForQualification();
        var classification = CheckOcclusion(groups, pyramid, camera);
        Console.WriteLine(name + " passed");
        return new(name, actual.PixelSha256, Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(actualIds.Ids.AsSpan()))),
            groups.Sum(group => group.DrawCount), groups.Sum(group => group.SeedMask.Count(value => value != 0)), classification);
    }

    private static object CheckOcclusion(IReadOnlyList<ClusterVisibilityReadback> groups, HzbPyramidReadback pyramid, Matrix4x4 clip)
    {
        int checkedCandidates = 0;
        int rejected = 0;
        foreach (var group in groups)
        {
            for (int index = 0; index < group.Candidates; index++)
            {
                if (!group.CpuVisible[index]) continue;
                checkedCandidates++;
                if (group.Ranks[index] != 0) continue;
                Require(group.SeedMask[index] == 0, "A seed candidate was lost from the final draw set.");
                Require(OccludedByBaseDepth(group.CandidateSpheres[index], clip, pyramid),
                    "GPU occlusion rejected a candidate not hidden by the independent CPU base-depth reference.");
                rejected++;
            }
        }
        return new { CheckedCandidates = checkedCandidates, Rejected = rejected, FalseNegatives = 0 };
    }

    private static bool OccludedByBaseDepth(Vector4 sphere, Matrix4x4 m, HzbPyramidReadback pyramid)
    {
        double left = double.PositiveInfinity;
        double right = double.NegativeInfinity;
        double top = double.PositiveInfinity;
        double bottom = double.NegativeInfinity;
        double nearest = 1;
        for (int corner = 0; corner < 8; corner++)
        {
            double x = sphere.X + ((corner & 1) == 0 ? -(double)sphere.W : sphere.W);
            double y = sphere.Y + ((corner & 2) == 0 ? -(double)sphere.W : sphere.W);
            double z = sphere.Z + ((corner & 4) == 0 ? -(double)sphere.W : sphere.W);
            double w = x * m.M14 + y * m.M24 + z * m.M34 + m.M44;
            double depth = x * m.M13 + y * m.M23 + z * m.M33 + m.M43;
            if (w <= 0 || depth <= 0) return false;
            double px = ((x * m.M11 + y * m.M21 + z * m.M31 + m.M41) / w * .5 + .5) * pyramid.Width;
            double py = ((x * m.M12 + y * m.M22 + z * m.M32 + m.M42) / w * .5 + .5) * pyramid.Height;
            left = Math.Min(left, px);
            right = Math.Max(right, px);
            top = Math.Min(top, py);
            bottom = Math.Max(bottom, py);
            nearest = Math.Min(nearest, depth / w);
        }
        if (right < 0 || bottom < 0 || left >= pyramid.Width || top >= pyramid.Height) return false;
        int x0 = (int)Math.Max(0, Math.Floor(left - 2));
        int x1 = (int)Math.Min(pyramid.Width - 1, Math.Floor(right + 2));
        int y0 = (int)Math.Max(0, Math.Floor(top - 2));
        int y1 = (int)Math.Min(pyramid.Height - 1, Math.Floor(bottom + 2));
        float maximum = 0;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                maximum = Math.Max(maximum, pyramid.Seed[y * pyramid.Width + x]);
        return nearest > maximum + .000009;
    }
}
