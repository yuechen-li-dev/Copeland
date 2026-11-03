using System.Numerics;
using Aurelian.Games;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingLodProof
{
    private static object CheckBoundaries(VulkanSolid3DRenderer renderer, NativeMesh3D mesh, string output)
    {
        var material = RenderingBindlessProof.Materials(1)[0] with { DoubleSided = true };
        Matrix4x4 shear = Matrix4x4.Identity;
        shear.M21 = .4f;
        var instances = new[]
        {
            new SceneInstance3D("near", mesh, material, Matrix4x4.CreateTranslation(-1.6f, 0, 0)),
            new SceneInstance3D("middle", mesh, material, Matrix4x4.CreateScale(1.3f, .7f, .9f) * shear
                * Matrix4x4.CreateTranslation(2, 0, -12)),
            new SceneInstance3D("far", mesh, material, Matrix4x4.CreateTranslation(-2, 0, -60)),
            new SceneInstance3D("very-far", mesh, material, Matrix4x4.CreateTranslation(6, 0, -140)),
        };
        Native3DScene scene = new([], []) { Instances = new NativeInstanceSet3D(instances) };
        Vector3 eye = new(0, 2, 8);
        var camera = Camera(eye, new(0, 1, -15));
        renderer.LodPixelError = 1;
        renderer.EnableHierarchicalZ = false;
        var actual = renderer.Render(scene, camera, eye, Clear, capture: true);
        var identities = renderer.ReadIdentitiesForQualification().Ids;
        uint[] levels = renderer.ReadVisibilityForQualification().Single().SelectedLods;
        Require(levels.Any(level => level > 0), "Mixed-scale specimen never selected a prepared LOD.");
        int foreground = 0;
        for (int pixel = 0; pixel < Width * Height; pixel++)
        {
            uint instance = identities[pixel * 2];
            if (instance == 0) continue;
            foreground++;
            Require(instance <= instances.Length, "Visibility identity escaped the instance table.");
            var range = mesh.LodRanges[(int)levels[instance - 1]];
            uint triangle = identities[pixel * 2 + 1] / 2;
            Require(triangle >= range.FirstIndex / 3 && triangle < (range.FirstIndex + range.IndexCount) / 3,
                "Visibility identity did not point into the selected prepared index range.");
        }
        Require(foreground > 100, "Mixed-scale identity specimen had too little coverage.");
        var explicitInstances = instances.Select((instance, index) => instance with
        {
            Mesh = new NativeMesh3D("explicit-" + index, mesh.Vertices,
                mesh.PackedIndices.Skip((int)mesh.LodRanges[(int)levels[index]].FirstIndex)
                    .Take((int)mesh.LodRanges[(int)levels[index]].IndexCount)),
        });
        Native3DScene explicitScene = new([], []) { Instances = new NativeInstanceSet3D(explicitInstances) };
        renderer.EnableVisibilityBuffer = false;
        var reference = renderer.Render(explicitScene, camera, eye, Clear, capture: true);
        long difference = actual.Pixels!.Zip(reference.Pixels!, (left, right) => (long)Math.Abs(left - right)).Sum();
        double resolveError = difference / (Width * Height * 4d);
        Require(resolveError < .05, "Visibility resolve disagrees with independent explicit selected-geometry rasterization.");
        NativeGameGraphics.WritePng(Path.Combine(output, "selected-geometry-visibility.png"), Width, Height, actual.Pixels!);
        NativeGameGraphics.WritePng(Path.Combine(output, "selected-geometry-raster.png"), Width, Height, reference.Pixels!);

        renderer.EnableVisibilityBuffer = true;
        var noHzb = renderer.Render(scene, camera, eye, Clear, capture: true);
        renderer.EnableHierarchicalZ = true;
        for (int frame = 0; frame < 8; frame++) renderer.Render(scene, camera, eye, Clear);
        var withHzb = renderer.Render(scene, camera, eye, Clear, capture: true);
        Require(noHzb.PixelSha256 == withHzb.PixelSha256, "HZB changed the selected LOD image.");
        renderer.EnableHierarchicalZ = false;
        renderer.Settings = renderer.Settings with { Shadows = true, AntiAliasing = AntiAliasing3D.Temporal };
        renderer.ResetTemporalHistory();
        var shadowReference = renderer.Render(scene, camera, eye, Clear, capture: true);
        renderer.EnableHierarchicalZ = true;
        renderer.ResetTemporalHistory();
        var shadowActual = renderer.Render(scene, camera, eye, Clear, capture: true);
        Require(shadowReference.PixelSha256 == shadowActual.PixelSha256, "Shadow/TAA LOD path changed under HZB.");
        for (int frame = 0; frame < 6; frame++)
        {
            Vector3 cutEye = frame % 2 == 0 ? new(20, 8, -40) : eye;
            renderer.Render(scene, Camera(cutEye, new(0, 1, -15)), cutEye, Clear);
        }
        renderer.WaitForPendingFrames();
        renderer.Settings = renderer.Settings with { Shadows = false, AntiAliasing = AntiAliasing3D.None };
        renderer.EnableHierarchicalZ = false;

        Native3DScene single = new([], []) { Instances = new NativeInstanceSet3D([instances[0] with
            { Identity = "hysteresis", Transform = Matrix4x4.Identity }]) };
        renderer.LodPixelError = 0;
        renderer.Render(single, camera, eye, Clear, capture: true);
        float entry = 0;
        uint entered = 0;
        for (float budget = .05f; budget <= 16; budget *= 1.03f)
        {
            renderer.LodPixelError = budget;
            renderer.Render(single, camera, eye, Clear, capture: true);
            uint level = renderer.ReadVisibilityForQualification().Single().SelectedLods.Single();
            if (level == 0) continue;
            entry = budget;
            entered = level;
            break;
        }
        Require(entry > 0, "Hysteresis specimen never crossed a LOD threshold.");
        for (int sample = 0; sample < 12; sample++)
        {
            renderer.LodPixelError = entry * (sample % 2 == 0 ? .9f : 1);
            renderer.Render(single, camera, eye, Clear, capture: true);
            Require(renderer.ReadVisibilityForQualification().Single().SelectedLods.Single() == entered,
                "Prepared LOD flickered inside the 20% hysteresis band.");
        }
        renderer.LodPixelError = entry * .7f;
        renderer.Render(single, camera, eye, Clear, capture: true);
        Require(renderer.ReadVisibilityForQualification().Single().SelectedLods.Single() == 0,
            "LOD failed to refine after leaving its hysteresis band.");
        renderer.LodPixelError = 16;
        Vector3 closeEye = new(0, 1.5f, 1);
        renderer.Render(single, Camera(closeEye, new(0, 1.5f, -1)), closeEye, Clear, capture: true);
        Require(renderer.ReadVisibilityForQualification().Single().SelectedLods.Single() == 0,
            "Near-plane-intersecting geometry did not retain LOD0.");
        renderer.LodPixelError = 1;
        return new { MixedLevels = levels, IdentityPixels = foreground, ExplicitRasterMeanError = resolveError,
            HzbExact = true, ShadowsAndTemporalExact = true, QueuedCameraCuts = 6,
            HysteresisEntryBudget = entry, HysteresisLevel = entered, StableJitterSamples = 12, NearPlaneLod = 0 };
    }
}
