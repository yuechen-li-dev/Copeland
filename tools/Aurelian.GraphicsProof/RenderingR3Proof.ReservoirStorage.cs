using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingR3Proof
{
    public static void RunReservoirStorage(string output)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, EnableRayQueries: true, ApplicationName: "ReSTIR history comparison"));
        Require(initialized.Success, "ReSTIR storage proof needs Vulkan ray queries.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        var scene = Scene(Box(), NativeAnalyticPrimitives3D.Create("storage-sphere", AnalyticPrimitiveKind3D.UnitSphere), 0);
        var results = new List<object>();
        foreach (var (width, height) in new[] { (1280, 720), (1920, 1080) })
        {
            foreach (var mode in RestirModes.Skip(1))
            {
                using var target = new VulkanNativeFrameTarget(plant, (uint)width, (uint)height);
                using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target, enableCulling: true, rayShadows: true);
                renderer.EnableVisibilityBuffer = true;
                renderer.EnableRayShadows = true;
                renderer.FrameMode = Native3DFrameMode.Interactive;
                renderer.Settings = Settings();
                renderer.LocalLights = Lights();
                // An odd quota alternates three and four slots across pixels and frames.
                // The maximum supported history and spatial neighbor counts stress the packed mass.
                int pixels = width * height;
                renderer.LocalLightSampling = RestirOptions(mode, pixels) with
                {
                    FrameRayBudget = pixels * 3 + pixels / 2,
                    HistoryLimit = 1024,
                    SpatialNeighbors = 8,
                };
                var clip = Matrix(width, height);
                const int warmup = 24;
                const int measured = 48;
                var gpu = new List<double>(measured);
                for (int index = 0; index < warmup + measured + 2; index++)
                {
                    var frame = renderer.Render(scene, clip, Camera.Eye, Clear);
                    if (frame.CompletedGpuFrame is { Sequence: >= warmup } interval && interval.Sequence < warmup + measured)
                        gpu.Add(interval.ElapsedMilliseconds);
                }
                Require(gpu.Count == measured, "ReSTIR GPU frame timing samples were missing.");
                renderer.WaitForPendingFrames();
                var capture = renderer.Render(scene, clip, Camera.Eye, Clear, capture: true);
                var storage = renderer.MeasureReservoirStorageForQualification();
                var samples = renderer.ReadLightSamplesForQualification();
                Require(SumLane(samples, 9) == renderer.LocalLightSampling.FrameRayBudget
                    && SumLane(samples, 8) <= renderer.LocalLightSampling.FrameRayBudget, "Reservoir storage changed the ray quota.");
                var sampleHash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(samples.AsSpan())));
                var hdr = renderer.ReadLightingForQualification().LinearRgb;
                var hdrHash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(hdr.AsSpan())));
                double[] sorted = gpu.Order().ToArray();
                double median = Median(gpu);
                results.Add(new
                {
                    Mode = mode.ToString(), Width = width, Height = height, Storage = storage,
                    MedianGpuMilliseconds = median, P95GpuMilliseconds = sorted[(int)Math.Ceiling(.95 * measured) - 1],
                    FrameGpuMilliseconds = gpu, capture.PixelSha256, SampleSha256 = sampleHash, HdrSha256 = hdrHash,
                    Queries = SumLane(samples, 8), Reused = SumLane(samples, 10), Spatial = SumLane(samples, 13),
                    RejectedTemporal = SumLane(samples, 12), RejectedSpatial = SumLane(samples, 15),
                });
                Console.WriteLine($"{mode} {width}x{height}: {median:F3} ms, history/candidate allocated " +
                    $"{storage.HistoryAllocationBytes}/{storage.CandidateAllocationBytes} bytes.");
            }
        }
        CheckReservoirModeSwitch(plant, assets, scene);
        validation.RequireClean();
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Accepted = true, Device = plant.Facts.PhysicalDeviceName, Results = results, ModeSwitchQualified = true,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_RESTIR_STORAGE_PASSED " + output);
    }

    private static void CheckReservoirModeSwitch(AurelianVulkanPlant plant, GameAssets assets, Native3DScene scene)
    {
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target, enableCulling: true, rayShadows: true);
        renderer.EnableVisibilityBuffer = true;
        renderer.EnableRayShadows = true;
        renderer.FrameMode = Native3DFrameMode.Interactive;
        renderer.Settings = Settings();
        renderer.LocalLights = Lights();
        var clip = Matrix(Width, Height);
        var spatial = RestirOptions(LocalLightSamplingMode3D.SpatialReservoir);
        renderer.LocalLightSampling = spatial;
        renderer.Render(scene, clip, Camera.Eye, Clear, capture: true);
        var firstSpatial = renderer.ReadLightSamplesForQualification();
        var initialStorage = renderer.MeasureReservoirStorageForQualification();
        Require(initialStorage.HistoryBytes == 0 && initialStorage.CandidateBytes > 0,
            "Fresh spatial-only ReSTIR allocated temporal history.");
        renderer.Render(scene, clip, Camera.Eye, Clear);
        renderer.Render(scene, clip, Camera.Eye, Clear);

        renderer.LocalLightSampling = spatial with { Mode = LocalLightSamplingMode3D.SpatiotemporalReservoir };
        renderer.Render(scene, clip, Camera.Eye, Clear, capture: true);
        Require(SumLane(renderer.ReadLightSamplesForQualification(), 10) == 0, "Mode switch retained temporal history.");
        var temporalStorage = renderer.MeasureReservoirStorageForQualification();
        Require(temporalStorage.HistoryBytes == 2 * initialStorage.CandidateBytes
            && temporalStorage.CandidateBytes == initialStorage.CandidateBytes, "Temporal history or candidate allocation was invalid.");
        renderer.Render(scene, clip, Camera.Eye, Clear, capture: true);
        Require(SumLane(renderer.ReadLightSamplesForQualification(), 10) > 0, "Temporal history did not begin recording after the mode switch.");
        renderer.Render(scene, clip, Camera.Eye, Clear);
        renderer.Render(scene, clip, Camera.Eye, Clear);

        renderer.LocalLightSampling = spatial;
        renderer.Render(scene, clip, Camera.Eye, Clear, capture: true);
        Require(firstSpatial.SequenceEqual(renderer.ReadLightSamplesForQualification()),
            "Returning to spatial-only ReSTIR changed its reset sample sequence.");
    }
}
