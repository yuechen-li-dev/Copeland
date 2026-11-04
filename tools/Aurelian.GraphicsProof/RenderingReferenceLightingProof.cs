using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingReferenceLightingProof
{
    private sealed record MetricPair(string Name, string Reference, string Test, string Group);

    internal static void Run(string output, string fixture)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "evidence.json"), "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, EnableRayQueries: true, ApplicationName: "Sun environment and temporal reference"));
        Require(initialized.Success, "Reference lighting plant initialization failed.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        byte[] program = assets.ComputeShader("ReferenceIntegrator3D.v.ts");
        var pairs = new List<MetricPair>();
        object sun = CheckSun(plant, assets, program, output, pairs);
        object environment = CheckEnvironment(plant, assets, program, output, pairs);
        object compiled = RenderingReferenceProof.QualifyCompiledReceiver(plant, program, output, fixture, sunBasis: true);
        object temporal = CheckTemporal(plant, assets, program, output, pairs);
        validation.RequireClean();
        File.WriteAllText(Path.Combine(output, "metrics-manifest.json"), JsonSerializer.Serialize(new
        {
            Ppd = 67, DynamicRange = "LDR", InputColor = "sRGB", Exposure = 1,
            OutputShader = "ToneMap3D.v.ts: filmic, sRGB, zero bloom, zero color retention",
            Pairs = pairs,
        }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
        {
            Accepted = true, Device = plant.Facts.PhysicalDeviceName, Sun = sun, Environment = environment,
            CompiledSunReceiver = compiled, Temporal = temporal,
            FullR3aComplete = false,
            Limits = "Opaque constant materials. Finite depth. Remaining textured/coverage and full standing scene suite qualification.",
        }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        Console.WriteLine("AURELIAN_REFERENCE_LIGHTING_PASSED " + output);
    }

    private static ReferenceTriangle3D[] Floor(ReferenceMaterial3D material)
    {
        return [new(new(-4, 0, -4), new(4, 0, 4), new(4, 0, -4), material),
            new(new(-4, 0, -4), new(-4, 0, 4), new(4, 0, 4), material)];
    }

    private static ReferenceImage3D Capture(AurelianVulkanPlant plant, byte[] program, ReferenceCapture3D settings,
        ReferenceCamera3D camera, ReferenceTriangle3D[] triangles, ReferenceLighting3D lighting, int samples,
        ReferenceAnalytic3D[]? shapes = null)
    {
        using var capture = new ReferenceIntegrator3D(plant, program, settings, camera, triangles, [], analytic: shapes, lighting: lighting);
        RenderingReferenceProof.AccumulateTo(capture, samples);
        return capture.Capture();
    }

    private static string Save(AurelianVulkanPlant plant, GameAssets assets, string output, string name, ReferenceImage3D image)
    {
        RenderingReferenceProof.WriteImage(output, name, image);
        string file = name + "-display.png";
        byte[] pixels = VulkanReferenceOutput3D.ToneMap(plant, assets.Shader("ToneMap3D.v.ts"), image);
        NativeGameGraphics.WritePng(Path.Combine(output, file), image.Width, image.Height, pixels);
        return file;
    }

    private static object CheckSun(AurelianVulkanPlant plant, GameAssets assets, byte[] program, string output, List<MetricPair> pairs)
    {
        var material = new ReferenceMaterial3D(new(.7f, .5f, .3f), Dielectric: 0);
        var settings = new ReferenceCapture3D(24, 24, 1, 491, PixelJitter: false);
        var camera = new ReferenceCamera3D(new(0, .01f, 0), -Vector3.UnitY, -Vector3.UnitZ, 1, 2);
        var lighting = new ReferenceLighting3D(new(Vector3.Normalize(new(.2f, 1, .1f)), new(3, 2, 1)));
        ReferenceImage3D image = Capture(plant, program, settings, camera, Floor(material), lighting, 16);
        Vector3 expected = material.BaseColor * lighting.Sun!.Irradiance * lighting.Sun.Direction.Y / MathF.PI;
        double maximumError = 0;
        for (int pixel = 0; pixel < image.Width * image.Height; pixel++)
        {
            Vector3 actual = new(image.LinearRgb[pixel * 3], image.LinearRgb[pixel * 3 + 1], image.LinearRgb[pixel * 3 + 2]);
            maximumError = Math.Max(maximumError, Vector3.Distance(actual, expected));
        }
        Require(maximumError < 2e-6, "Directional sun differs from independent Lambert law: " + maximumError);
        ReferenceImage3D black = Capture(plant, program, settings, camera, Floor(material), new(), 1);
        Require(black.LinearRgb.All(value => value == 0), "Zero-light scene retained stale radiance.");
        ReferenceImage3D below = Capture(plant, program, settings, camera, Floor(material),
            lighting with { Sun = lighting.Sun with { Direction = -Vector3.UnitY } }, 1);
        Require(below.LinearRgb.All(value => value == 0), "Sun below the surface contributed light.");
        ReferenceAnalytic3D[] blocker = [new(AnalyticPrimitiveKind3D.UnitBox, Matrix4x4.CreateScale(.65f, .4f, .65f)
            * Matrix4x4.CreateTranslation(0, 1, 0), material with { BaseColor = Vector3.Zero })];
        ReferenceImage3D shadow = Capture(plant, program, settings, camera, Floor(material), lighting, 16, blocker);
        Require(shadow.LinearRgb.Sum() < image.LinearRgb.Sum() * .8f, "Analytic blocker did not shadow directional sun.");
        string display = Save(plant, assets, output, "sun-unoccluded", image);
        Save(plant, assets, output, "sun-shadow", shadow);
        pairs.Add(new("identical-output", display, display, "identity"));
        Console.WriteLine($"Directional sun: maximum Lambert error {maximumError:G5}");
        return new { MaximumLambertError = maximumError, ShadowEnergyFraction = shadow.LinearRgb.Sum() / image.LinearRgb.Sum(),
            DeltaDirection = true, ZeroLightAndBelowHorizonBlack = true };
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
