using System.Numerics;
using Aurelian.Games;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingHybridProof
{
    private static object CheckAffineReferenceCapture(AurelianVulkanPlant plant, GameAssets assets, string output)
    {
        byte[] program = assets.ComputeShader("ReferenceIntegrator3D.v.ts");
        var material = new ReferenceMaterial3D(new(.7f), Dielectric: 0);
        var triangles = new List<ReferenceTriangle3D>();
        AddQuad(new(-1.5f, 0, -1.5f), new(3, 0, 0), new(0, 0, 3));
        AddQuad(new(-1.5f, 0, -1.5f), new(0, 2.5f, 0), new(3, 0, 0));
        AddQuad(new(-1.5f, 0, -1.5f), new(0, 0, 3), new(0, 2.5f, 0));
        AddQuad(new(1.5f, 0, -1.5f), new(0, 2.5f, 0), new(0, 0, 3));
        Matrix4x4 shear = Matrix4x4.Identity;
        shear.M21 = .5f;
        ReferenceAnalytic3D[] shapes = [
            new(AnalyticPrimitiveKind3D.UnitSphere, Matrix4x4.CreateScale(.35f, .65f, .24f) * Matrix4x4.CreateRotationY(.4f)
                * Matrix4x4.CreateTranslation(-.65f, .65f, 0), material with { BaseColor = new(.7f, .07f, .04f) }),
            new(AnalyticPrimitiveKind3D.UnitBox, Matrix4x4.CreateScale(.28f, .4f, .3f) * shear
                * Matrix4x4.CreateTranslation(.1f, .4f, .05f), material with { BaseColor = new(.07f, .6f, .08f) }),
            new(AnalyticPrimitiveKind3D.Capsule, Matrix4x4.CreateScale(-.5f, .7f, .35f) * Matrix4x4.CreateRotationZ(-.2f)
                * Matrix4x4.CreateTranslation(.7f, .7f, -.35f), material with { BaseColor = new(.06f, .15f, .7f) }),
        ];
        var light = new ReferenceRectLight3D(new(-.5f, 2.4f, -.5f), Vector3.UnitX, Vector3.UnitZ, new(8));
        var camera = new ReferenceCamera3D(new(0, 1.6f, 4), new(0, .7f, 0), Vector3.UnitY, .7f);
        var settings = new ReferenceCapture3D(64, 64, 8, 123);
        var images = new Dictionary<int, ReferenceImage3D>();
        using (var integrator = new ReferenceIntegrator3D(plant, program, settings, camera, triangles, [], light, shapes))
        {
            foreach (int samples in new[] { 16, 64, 256, 1024 })
            {
                RenderingReferenceProof.AccumulateTo(integrator, samples);
                images.Add(samples, integrator.Capture());
                RenderingReferenceProof.WriteImage(output, "affine-path-" + samples, images[samples]);
            }
        }
        ReferenceImage3D reference;
        using (var integrator = new ReferenceIntegrator3D(plant, program, settings with { Seed = 491 }, camera, triangles, [], light, shapes))
        {
            RenderingReferenceProof.AccumulateTo(integrator, 4096);
            reference = integrator.Capture();
            RenderingReferenceProof.WriteImage(output, "affine-path-reference", reference);
        }
        var errors = images.Select(item => new { Samples = item.Key, Rmse = RenderingReferenceProof.Rmse(item.Value, reference) }).ToArray();
        Require(errors[^1].Rmse < errors[0].Rmse * .4, "Affine path capture failed sample-count convergence.");
        using (var repeated = new ReferenceIntegrator3D(plant, program, settings, camera, triangles, [], light, shapes))
        {
            repeated.Accumulate(31);
            repeated.Accumulate(225);
            Require(repeated.Capture().LinearRgb.SequenceEqual(images[256].LinearRgb), "Affine capture depends on batch partition.");
        }
        using (var preview = new ReferenceIntegrator3D(plant, program, settings with { Width = 192, Height = 192 }, camera, triangles, [], light, shapes))
        {
            RenderingReferenceProof.AccumulateTo(preview, 1024);
            RenderingReferenceProof.WriteImage(output, "affine-path-preview", preview.Capture());
        }
        object shadows = CheckAffineSecondaryVisibility(plant, program, output);
        Console.WriteLine($"Affine path convergence: {errors[0].Rmse:F6} at 16 spp to {errors[^1].Rmse:F6} at 1024 spp");
        return new { Capture = settings, Camera = camera, Light = light, Shapes = shapes, Convergence = errors,
            BatchPartitionByteIdentical = true, SecondaryVisibility = shadows };

        void AddQuad(Vector3 corner, Vector3 u, Vector3 v)
        {
            triangles.Add(new(corner, corner + u, corner + v, material));
            triangles.Add(new(corner + u, corner + u + v, corner + v, material));
        }
    }

    private static object CheckAffineSecondaryVisibility(AurelianVulkanPlant plant, byte[] program, string output)
    {
        var floorMaterial = new ReferenceMaterial3D(new(.7f), Dielectric: 0);
        ReferenceTriangle3D[] floor = [new(new(-2, 0, -2), new(2, 0, 2), new(2, 0, -2), floorMaterial),
            new(new(-2, 0, -2), new(-2, 0, 2), new(2, 0, 2), floorMaterial)];
        var camera = new ReferenceCamera3D(new(0, .01f, 0), new(0, -1, 0), -Vector3.UnitZ, 1, 2);
        var light = new ReferenceRectLight3D(new(-.4f, 2, -.4f), new(.8f, 0, 0), new(0, 0, .8f), new(4));
        var settings = new ReferenceCapture3D(24, 24, 1, 73, PixelJitter: false);
        ReferenceImage3D unoccluded;
        using (var control = new ReferenceIntegrator3D(plant, program, settings, camera, floor, [], light))
        {
            RenderingReferenceProof.AccumulateTo(control, 1024);
            unoccluded = control.Capture();
            RenderingReferenceProof.WriteImage(output, "secondary-unoccluded", unoccluded);
        }
        var rows = new List<object>();
        foreach (var kind in new[] { AnalyticPrimitiveKind3D.UnitSphere, AnalyticPrimitiveKind3D.UnitBox, AnalyticPrimitiveKind3D.Capsule })
        {
            ReferenceAnalytic3D[] blocker = [new(kind, Matrix4x4.CreateScale(.65f, .4f, .65f) * Matrix4x4.CreateTranslation(0, 1, 0),
                floorMaterial with { BaseColor = Vector3.Zero })];
            using var capture = new ReferenceIntegrator3D(plant, program, settings, camera, floor, [], light, blocker);
            RenderingReferenceProof.AccumulateTo(capture, 1024);
            ReferenceImage3D occluded = capture.Capture();
            double controlEnergy = 0;
            double blockedEnergy = 0;
            for (int y = 8; y < 16; y++)
            {
                for (int x = 8; x < 16; x++)
                {
                    int offset = (y * 24 + x) * 3;
                    for (int channel = 0; channel < 3; channel++)
                    {
                        controlEnergy += unoccluded.LinearRgb[offset + channel];
                        blockedEnergy += occluded.LinearRgb[offset + channel];
                    }
                }
            }
            double fraction = blockedEnergy / controlEnergy;
            Require(controlEnergy > 1 && fraction < .7, "Off-camera analytic primitive failed to shadow the receiver: " + kind);
            RenderingReferenceProof.WriteImage(output, "secondary-" + kind, occluded);
            rows.Add(new { Kind = kind.ToString(), CentralEnergyFraction = fraction, PrimarySurface = "Unchanged floor below every analytic blocker" });
        }
        return rows;
    }
}
