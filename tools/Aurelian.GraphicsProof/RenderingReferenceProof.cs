using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;
using SkiaSharp;

internal static partial class RenderingReferenceProof
{
    internal static void Run(string output, string lightingFixture)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        var assets = new GameAssets();
        byte[] program = assets.ComputeShader("ReferenceIntegrator3D.v.ts");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian reference capture", EnableRayQueries: true));
        Require(initialized.Success, string.Join("; ", initialized.Diagnostics.Select(item => item.Message)));
        using var plant = initialized.Plant!;
        var white = new ReferenceMaterial3D(new(.7f), Dielectric: 0);
        var red = white with { BaseColor = new(.7f, .06f, .04f) };
        var green = white with { BaseColor = new(.04f, .65f, .08f) };
        var room = new List<ReferenceTriangle3D>();
        Quad(room, new(-1, 0, -1), new(2, 0, 0), new(0, 0, 2), white);
        Quad(room, new(-1, 2, -1), new(0, 0, 2), new(2, 0, 0), white);
        Quad(room, new(-1, 0, -1), new(0, 2, 0), new(2, 0, 0), white);
        Quad(room, new(-1, 0, -1), new(0, 0, 2), new(0, 2, 0), red);
        Quad(room, new(1, 0, -1), new(0, 2, 0), new(0, 0, 2), green);
        ReferenceSphere3D[] spheres = [new(new(-.4f, .45f, .05f), .45f, white)];
        var light = new ReferenceRectLight3D(new(-.35f, 1.98f, -.3f), new(.7f, 0, 0), new(0, 0, .6f), new(12));
        var camera = new ReferenceCamera3D(new(0, 1, 3.3f), new(0, 1, 0), Vector3.UnitY, .67f);
        var settings = new ReferenceCapture3D(64, 64, 8, 123);
        var checkpoints = new Dictionary<int, ReferenceImage3D>();
        using (var integrator = new ReferenceIntegrator3D(plant, program, settings, camera, room, spheres, light))
        {
            foreach (int count in new[] { 16, 64, 256, 1024 })
            {
                AccumulateTo(integrator, count);
                checkpoints.Add(count, integrator.Capture());
                WriteImage(output, $"cornell-{count}", checkpoints[count]);
                Console.WriteLine($"Cornell {count} spp; last batch {integrator.LastBatchGpuMilliseconds:F3} ms GPU");
            }
        }
        ReferenceImage3D reference;
        using (var integrator = new ReferenceIntegrator3D(plant, program, settings with { Seed = 987 }, camera, room, spheres, light))
        {
            AccumulateTo(integrator, 4096);
            reference = integrator.Capture();
            WriteImage(output, "cornell-reference", reference);
        }
        double depthError;
        using (var deeper = new ReferenceIntegrator3D(plant, program, settings with { Seed = 987, MaximumBounces = 16 }, camera, room, spheres, light))
        {
            AccumulateTo(deeper, 4096);
            ReferenceImage3D image = deeper.Capture();
            WriteImage(output, "cornell-reference-16-bounces", image);
            depthError = Rmse(reference, image);
        }
        var convergence = checkpoints.Select(item => new { Samples = item.Key, Rmse = Rmse(item.Value, reference) }).ToArray();
        Require(convergence[^1].Rmse < convergence[0].Rmse * .4, "Cornell failed sample-count convergence.");
        ReferenceImage3D bsdfOnly;
        using (var integrator = new ReferenceIntegrator3D(plant, program, settings with { NextEventEstimation = false }, camera, room, spheres, light))
        {
            AccumulateTo(integrator, 64);
            bsdfOnly = integrator.Capture();
            WriteImage(output, "cornell-bsdf-only-64", bsdfOnly);
        }
        double misError = Rmse(checkpoints[64], reference);
        double bsdfError = Rmse(bsdfOnly, reference);
        Require(misError < bsdfError * .5, "NEE/MIS did not reduce equal-sample error.");
        using (var repeated = new ReferenceIntegrator3D(plant, program, settings, camera, room, spheres, light))
        {
            repeated.Accumulate(17);
            repeated.Accumulate(239);
            Require(repeated.Capture().LinearRgb.SequenceEqual(checkpoints[256].LinearRgb), "Batch partition changed seeded samples.");
        }
        var floor = QualifyFloor(plant, program, output);
        var furnace = QualifyFurnace(plant, assets);
        var compiledReceiver = QualifyCompiledReceiver(plant, program, output, lightingFixture);
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Accepted = true,
            FullR3aComplete = false,
            Device = plant.Facts.PhysicalDeviceName,
            ShaderSha256 = Convert.ToHexString(SHA256.HashData(program)),
            Capture = settings,
            Scene = new { Camera = camera, Light = light, Triangles = room, Spheres = spheres },
            Convergence = convergence,
            DepthTruncation = new { Samples = 4096, Seed = 987, Rmse8Versus16Bounces = depthError,
                Scope = "Common seeded paths measure this finite depth change, not error against infinite transport." },
            EqualSampleAblation = new { Samples = 64, MisRmse = misError, BsdfOnlyRmse = bsdfError, ErrorReduction = bsdfError / misError },
            BatchPartitionByteIdentical = true,
            Floor = floor,
            Furnace = furnace,
            CompiledReceiver = compiledReceiver,
            Limits = new[] { "Opaque constant materials; triangles and nonemissive affine sphere/box/capsule surfaces; at most 256 emissive triangles; directional sun and source HDR environment.",
                "Finite 1..16 scattering vertices, no denoiser or radiance clamp; cosine sampler is inefficient for glossy lobes.",
                "Remaining: textured/coverage materials and the full standing scene suite; bounded sun/environment, FLIP and temporal qualification is available." }
        }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        Console.WriteLine($"AURELIAN_REFERENCE_PASSED: MIS error {misError:F6} versus BSDF-only {bsdfError:F6}");
    }

    private static object QualifyFloor(AurelianVulkanPlant plant, byte[] program, string output)
    {
        var rows = new List<object>();
        var camera = new ReferenceCamera3D(new(0, 1, 0), Vector3.Zero, -Vector3.UnitZ, MathF.PI / 3);
        var light = new ReferenceRectLight3D(new(-.5f, 2, -.5f), Vector3.UnitX, Vector3.UnitZ, new(4));
        foreach (DirectDiffuseModel3D model in Enum.GetValues<DirectDiffuseModel3D>())
        {
            var material = new ReferenceMaterial3D(new(.7f, .5f, .3f), .65f, Dielectric: 0, DiffuseModel: model);
            var floor = new List<ReferenceTriangle3D>();
            Quad(floor, new(-4, 0, -4), new(8, 0, 0), new(0, 0, 8), material);
            using var integrator = new ReferenceIntegrator3D(plant, program, new(24, 24, 1, 41), camera, floor, [], light);
            AccumulateTo(integrator, 1024);
            ReferenceImage3D image = integrator.Capture();
            WriteImage(output, $"floor-{model}", image);
            double squaredError = 0;
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    Vector3 point = new(((x + .5f) / image.Width * 2 - 1) / MathF.Sqrt(3), 0,
                        -((1 - (y + .5f) / image.Height * 2) / MathF.Sqrt(3)));
                    Vector3 view = Vector3.Normalize(camera.Eye - point);
                    double[] expected = new double[3];
                    const int divisions = 48;
                    for (int v = 0; v < divisions; v++)
                    {
                        for (int u = 0; u < divisions; u++)
                        {
                            Vector3 sample = light.Corner + light.EdgeU * ((u + .5f) / divisions) + light.EdgeV * ((v + .5f) / divisions);
                            Vector3 delta = sample - point;
                            Vector3 incoming = Vector3.Normalize(delta);
                            double geometry = incoming.Y * incoming.Y / delta.LengthSquared();
                            double[] albedos = [material.BaseColor.X, material.BaseColor.Y, material.BaseColor.Z];
                            for (int channel = 0; channel < 3; channel++)
                            {
                                expected[channel] += DiffuseReference.Response(model, albedos[channel], incoming.Y,
                                    view.Y, Vector3.Dot(incoming, view), material.Roughness) * 4 / Math.PI * geometry / (divisions * divisions);
                            }
                        }
                    }
                    for (int channel = 0; channel < 3; channel++)
                    {
                        double delta = image.LinearRgb[(y * image.Width + x) * 3 + channel] - expected[channel];
                        squaredError += delta * delta;
                    }
                }
            }
            double error = Math.Sqrt(squaredError / image.LinearRgb.Length);
            Require(error < .002, $"{model} floor differs from independent double-precision area quadrature: {error}.");
            rows.Add(new { Model = model.ToString(), LinearHdrRmse = error, Samples = image.Samples });
            Console.WriteLine($"Floor {model}: linear HDR RMSE {error:F7}");
        }
        return rows;
    }

    internal static void AccumulateTo(ReferenceIntegrator3D integrator, int count)
    {
        while (integrator.Samples < count)
        {
            integrator.Accumulate(Math.Min(128, count - integrator.Samples));
        }
    }

    private static void Quad(List<ReferenceTriangle3D> triangles, Vector3 corner, Vector3 u, Vector3 v, ReferenceMaterial3D material)
    {
        triangles.Add(new(corner, corner + u, corner + v, material));
        triangles.Add(new(corner + u, corner + u + v, corner + v, material));
    }

    internal static double Rmse(ReferenceImage3D first, ReferenceImage3D second)
    {
        Require(first.Width == second.Width && first.Height == second.Height, "Image dimensions differ.");
        double sum = 0;
        for (int index = 0; index < first.LinearRgb.Length; index++)
        {
            double difference = first.LinearRgb[index] - second.LinearRgb[index];
            sum += difference * difference;
        }
        return Math.Sqrt(sum / first.LinearRgb.Length);
    }

    internal static void WriteImage(string output, string name, ReferenceImage3D image)
    {
        File.WriteAllBytes(Path.Combine(output, name + ".rgb32f"), MemoryMarshal.AsBytes(image.LinearRgb.AsSpan()).ToArray());
        using var bitmap = new SKBitmap(image.Width, image.Height);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                int offset = (y * image.Width + x) * 3;
                bitmap.SetPixel(x, y, new SKColor(Display(image.LinearRgb[offset]), Display(image.LinearRgb[offset + 1]), Display(image.LinearRgb[offset + 2])));
            }
        }
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(output, name + ".png"), png.ToArray());
    }

    private static byte Display(float value)
    {
        return (byte)Math.Clamp(MathF.Round(MathF.Pow(value / (1 + value), 1 / 2.2f) * 255), 0, 255);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
