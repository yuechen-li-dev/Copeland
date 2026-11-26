using System.Diagnostics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Aurelian.Beacon3D;

internal static class BeaconGraphicsBenchmark
{
    public static void Run(string[] args, long started)
    {
        if (args.Length is < 2 or > 3 || args[0] is not ("source" or "bake" or "packed" or "replay"))
        {
            throw new ArgumentException("Expected --graphics-benchmark source|bake|packed|replay PACK_DIRECTORY [SAMPLES].");
        }
        string mode = args[0];
        int samples = args.Length == 3 ? int.Parse(args[2]) : 120;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(samples);
        var assets = mode == "source" ? new GameAssets() : new GameAssets(args[1], bakeGraphicsPack: mode == "bake");

        var options = WindowOptions.DefaultVulkan;
        options.Title = "Beacon graphics benchmark";
        options.Size = new Vector2D<int>(960, 600);
        options.IsVisible = false;
        options.VSync = false;
        using IWindow window = Window.Create(options);
        window.Initialize();
        long setupStarted = Stopwatch.GetTimestamp();
        using var graphics = new NativeGameGraphics(window, options.Title, visible: false, assets,
            vSync: false, enableFramesInFlight: false);
        double graphicsSetupMs = Stopwatch.GetElapsedTime(setupStarted).TotalMilliseconds;

        using var application = new BeaconShellGame();
        var view = application.View(new GameViewRequest(graphics.Width, graphics.Height) { RetainedGeometry = true });
        var clear = new NativeFrameClearColor(.055f, .095f, .15f, 1);
        var renderer = graphics.Renderer;
        var settings = renderer.Settings with { AntiAliasing = AntiAliasing3D.None, BloomIntensity = 0 };
        renderer.Settings = settings;
        renderer.MeasureOutputRecording = true;
        renderer.ReplayOutputCommands = mode == "replay";

        var first = Render(capture: false);
        if (view.Viewmodel is { } viewmodel)
        {
            _ = graphics.RenderViewmodel(viewmodel);
        }
        if (!graphics.Present(1))
        {
            throw new InvalidOperationException("Beacon benchmark could not present the first frame.");
        }
        double firstFrameMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var reference = Render(capture: view.Viewmodel is null);
        if (view.Viewmodel is { } captureViewmodel)
        {
            reference = graphics.RenderViewmodel(captureViewmodel, capture: true);
        }

        for (int frame = 0; frame < 12; frame++)
        {
            _ = Render(capture: false);
        }
        var timings = new FrameSample[samples];
        for (int frame = 0; frame < samples; frame++)
        {
            timings[frame] = Measure(renderer.ReplayOutputCommands);
        }

        var pairedTimings = new List<FramePair>();
        if (mode == "replay")
        {
            for (int pair = 0; pair < samples; pair++)
            {
                FrameSample inline;
                FrameSample replayed;
                if (pair % 2 == 0)
                {
                    inline = Measure(replay: false);
                    replayed = Measure(replay: true);
                }
                else
                {
                    replayed = Measure(replay: true);
                    inline = Measure(replay: false);
                }
                pairedTimings.Add(new(inline, replayed));
            }
            renderer.ReplayOutputCommands = true;
        }

        var pixelHashes = new List<string>();
        foreach (float exposure in new[] { 1f, .7f, 1.3f, 1f })
        {
            renderer.Settings = settings with { Exposure = exposure };
            pixelHashes.Add(Render(capture: true).PixelSha256!);
        }
        if (pixelHashes[0] != pixelHashes[3] || pixelHashes.Take(3).Distinct().Count() != 3)
        {
            throw new InvalidDataException("Beacon output did not track the exposure changes repeatably.");
        }
        if (mode == "replay" && renderer.ReplayedOutputDraws < samples)
        {
            throw new InvalidOperationException("The warm frames did not reuse the output draw commands.");
        }

        var textureChangeHashes = new List<string>();
        long replaysBeforeChange = renderer.ReplayedOutputDraws;
        foreach (float bloom in new[] { .25f, 0f })
        {
            renderer.Settings = settings with { BloomIntensity = bloom };
            for (int frame = 0; frame < 3; frame++)
            {
                textureChangeHashes.Add(Render(capture: true).PixelSha256!);
            }
        }
        long replaysAcrossTextureChanges = renderer.ReplayedOutputDraws - replaysBeforeChange;
        if (mode == "replay" && replaysAcrossTextureChanges != 2)
        {
            throw new InvalidOperationException("The output commands were not recaptured once per slot and texture change.");
        }

        if (mode == "replay")
        {
            renderer.FrameMode = Native3DFrameMode.Interactive;
            long replaysBeforeInteractive = renderer.ReplayedOutputDraws;
            for (int frame = 0; frame < 8; frame++)
            {
                _ = Render(capture: false);
            }
            if (Render(capture: true).PixelSha256 != pixelHashes[0]
                || renderer.ReplayedOutputDraws - replaysBeforeInteractive != 9)
            {
                throw new InvalidOperationException("Interactive frames did not preserve the output across in-flight slots.");
            }
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Mode = mode,
            Device = graphics.Plant.Facts.PhysicalDeviceName,
            ValidationLayers = graphics.Plant.Facts.EnabledValidationLayers,
            graphics.Width,
            graphics.Height,
            first.TriangleCount,
            GraphicsSetupMs = graphicsSetupMs,
            FirstFrameMs = firstFrameMs,
            FirstFrameHash = reference.PixelSha256,
            renderer.ReplayedOutputDraws,
            PixelHashes = pixelHashes,
            TextureChangeHashes = textureChangeHashes,
            ReplaysAcrossTextureChanges = replaysAcrossTextureChanges,
            Samples = timings,
            PairedSamples = pairedTimings,
        }));

        Native3DFrameResult Render(bool capture) => renderer.Render(view.Scene, view.Camera, view.Eye, clear, capture);

        FrameSample Measure(bool replay)
        {
            renderer.ReplayOutputCommands = replay;
            var result = Render(capture: false);
            return new(renderer.OutputRecordingMilliseconds,
                result.CpuTimings!.PrepareRecordMilliseconds,
                result.GpuPassTimes.Single(time => time.Pass == "tone-map-output").Milliseconds);
        }
    }

    private sealed record FrameSample(double OutputRecordMs, double FramePrepareRecordMs, double OutputGpuMs);
    private sealed record FramePair(FrameSample Inline, FrameSample Replayed);
}
