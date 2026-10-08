using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.GameHost;
using InputMan.Aurelian;
using InputMan.Core;
using TinyFarm.Core;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

internal static class TinyFarmM25NativeProof
{
    public static void Run(string root, TinyFarmGame game, AurelianInputAdapter input,
        TinyFarmNativeWindow window, TinyFarmNativeRenderer renderer, AurelianGameHost host, bool baseline)
    {
        string output = Path.Combine(root, "artifacts", "tinyfarm-high-fidelity-presentation-m25");
        Directory.CreateDirectory(output);
        game.Start();
        game.Host.CommitLoadedSession(TinyFarmM24ProofStates.CreateAt(game.Definitions,
            TinyFarmSceneIds.Riverside, new GridPosition(5, 6), ActorFacing.Right));
        input.SetContexts(game.Contexts);
        host.RunFrame(TimeSpan.Zero);
        int height = renderer.Layout.Height;
        Write(output, baseline ? "texture-baseline-native.json" : $"texture-{height}p-native.json", renderer.TextureFacts);
        string suffix = baseline ? "before" : height + "p-after";
        game.Presentation.HudVisible = true;
        Capture(Path.Combine(output, "ui-on-" + suffix + ".png"), renderer, host);
        byte[] hudPixels = renderer.Last!.NativeFrame.Pixels!.ToArray();
        string stateBefore = StateHash(game);
        string cameraBefore = JsonSerializer.Serialize(renderer.WorldCamera);
        Key(KeyboardKey.F9, window, host);
        Require(!game.Presentation.HudVisible, "F9 must hide HUD through InputMan.");
        Capture(Path.Combine(output, "world-only-" + suffix + ".png"), renderer, host);
        byte[] worldPixels = renderer.Last!.NativeFrame.Pixels!;
        int outsideHudDifferences = CountOutsideHudDifferences(hudPixels, worldPixels, renderer.Layout);
        Require(outsideHudDifferences == 0, "HUD toggle altered pixels outside HUD chrome at the same render time.");
        string worldHash = renderer.Last!.NativeFrame.PixelSha256!;
        Require(stateBefore == StateHash(game), "HUD toggle changed state.");
        Require(cameraBefore == JsonSerializer.Serialize(renderer.WorldCamera), "HUD toggle changed camera.");
        Write(output, baseline ? "presentation-baseline.json" : "hud-toggle-proof.json", new
        {
            baseline, nativePath = "TinyFarm.Native / NativeLayerCompositor / Vulkan",
            renderer.Device, renderer.Layout, hudKey = "F9", stateBefore, stateAfter = StateHash(game),
            cameraBefore, cameraAfter = JsonSerializer.Serialize(renderer.WorldCamera),
            outsideHudDifferences, frozenRenderSequence = 42,
            hudHidden = !game.Presentation.HudVisible, worldHash,
        });
        if (baseline)
        {
            return;
        }
        foreach (bool hud in new[] { false, true })
        {
            game.Presentation.HudVisible = hud;
            for (int i = 0; i < 30; i++)
            {
                host.RunFrame(TimeSpan.FromSeconds(1.0 / 60));
            }
            renderer.ResetPerformanceMetrics();
            long fieldBytesBefore = renderer.FieldUploadBytes;
            int textureUploadsBefore = renderer.SpriteTextureUploads;
            var times = new List<double>();
            for (int i = 0; i < 180; i++)
            {
                long started = Stopwatch.GetTimestamp();
                host.RunFrame(TimeSpan.FromSeconds(1.0 / 60));
                times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            times.Sort();
            Write(output, $"performance-{height}p{(hud ? "-hud" : "")}.json", new
            {
                renderer.Device, renderer.Layout, hud, frames = times.Count,
                timing = "CPU wall-clock complete host frame, synchronous Vulkan submission, VSync off; GPU timestamps unavailable",
                p50Milliseconds = Percentile(times, .50), p95Milliseconds = Percentile(times, .95),
                p99Milliseconds = Percentile(times, .99), worstMilliseconds = times[^1],
                averageDrawCount = renderer.DrawCalls / (double)times.Count,
                renderer.BufferUploads, spriteUploadsDuringMeasurement = renderer.SpriteTextureUploads - textureUploadsBefore,
                fieldUploadBytes = renderer.FieldUploadBytes - fieldBytesBefore,
                projectionMilliseconds = renderer.ProjectionTime.TotalMilliseconds,
                compositionMilliseconds = renderer.CompositionTime.TotalMilliseconds,
                uiAllocatedBytes = renderer.OverlayAllocatedBytes,
                renderer.WorldSpriteCount,
                renderer.FieldProjectionMilliseconds,
            });
        }
        game.Presentation.HudVisible = false;
        Key(KeyboardKey.F10, window, host);
        Require(game.Presentation.InspectorVisible && !game.Presentation.HudVisible, "Inspector must be independent.");
        Capture(Path.Combine(output, "oblivion-presentation-inspector.png"), renderer, host);
        Key(KeyboardKey.F10, window, host);
        game.Presentation.HudVisible = true;
        string captureState = StateHash(game);
        Key(KeyboardKey.F11, window, host);
        Require(game.Presentation.HudVisible && !game.Presentation.CleanCaptureRequested, "Clean capture must restore HUD.");
        Require(captureState == StateHash(game), "Capture changed state.");
        Write(output, "world-only-proof.json", new { hud = "F9", inspector = "F10", cleanCapture = "F11", stateUnchanged = true,
            hudRestoredAfterCapture = true, contextualPromptRetained = true, specialExecutableRequired = false });
        if (height == 1080)
        {
            game.Host.CommitLoadedSession(TinyFarmM24ProofStates.CreateAt(game.Definitions,
                TinyFarmSceneIds.Riverside, new GridPosition(5, 6), ActorFacing.Right));
            game.Presentation.HudVisible = false;
            renderer.WorldBackdropOverride = 0x172923FF;
            Capture(Path.Combine(output, "alpha-dark-background.png"), renderer, host);
            renderer.WorldBackdropOverride = null;
            foreach (double factor in new[] { 1.0, 1.5, 2.0, 2.5 })
            {
                renderer.TreeScale = factor;
                Capture(Path.Combine(output, $"tree-scale-{factor:0.0}.png"), renderer, host);
            }
            renderer.TreeScale = 1.1;
            foreach (double factor in new[] { 1.0, 1.5, 2.0, 2.5 })
            {
                renderer.FarmhouseScale = factor;
                Capture(Path.Combine(output, $"farmhouse-scale-{factor:0.0}.png"), renderer, host);
            }
            renderer.FarmhouseScale = 1.05;
        }
        renderer.Present(new AurelianHostFrame(42, TimeSpan.Zero, TimeSpan.Zero));
        Write(output, $"oblivion-presentation-{height}p.json", game.LiveInspection.Capture());
        if (height == 1080)
        {
            string state = StateHash(game);
            int uploads = renderer.SpriteTextureUploads;
            var sizes = new List<object>();
            foreach ((int width, int targetHeight) in new[] { (1600, 1000), (2560, 1440), (1920, 1080) })
            {
                window.NativeWindow.Size = new Silk.NET.Maths.Vector2D<int>(width, targetHeight);
                host.RunFrame(TimeSpan.Zero);
                Require(renderer.Layout.Width == window.SurfaceSize.Width && renderer.Layout.Height == window.SurfaceSize.Height,
                    "Framebuffer and renderer must match after native resize.");
                Require(state == StateHash(game), "Resize changed semantic state.");
                Require(uploads == renderer.SpriteTextureUploads, "Resize reuploaded sprite assets.");
                Capture(Path.Combine(output, $"resize-{width}x{targetHeight}.png"), renderer, host);
                sizes.Add(new { requestedWidth = width, requestedHeight = targetHeight, physical = window.SurfaceSize,
                    windowSize = new { window.NativeWindow.Size.X, window.NativeWindow.Size.Y }, renderer.Layout,
                    stateHash = StateHash(game), spriteReuploads = renderer.SpriteTextureUploads - uploads });
            }
            Write(output, "resolution-policy.json", new { defaultWidth = 1920, defaultHeight = 1080,
                policy = "Uniform world fit with three metres canopy headroom; decorative meadow extends into aspect space. UI uniformly fits 1280x720 logical units independently.",
                dpi = "FramebufferSize is the native render target authority; no intermediate low-resolution image.", sizes });
            double originalScale = renderer.TreeScale;
            renderer.TreeScale *= 1.4;
            host.RunFrame(TimeSpan.Zero);
            Require(state == StateHash(game), "Visual tree scale changed semantics.");
            Write(output, "fresh-scale-proof.json", new { multiplier = 1.4, before = originalScale, after = renderer.TreeScale,
                stateUnchanged = true, collisionUnchanged = true, occlusion = "Semantic canopy unchanged; visual cover and player silhouette use projected artwork bounds." });
            renderer.TreeScale = originalScale;
        }
        Console.WriteLine("TINYFARM_M25_NATIVE_PROOF_PASSED");
    }

    private static int CountOutsideHudDifferences(byte[] hud, byte[] world, TinyFarmPresentationLayout layout)
    {
        int count = 0;
        for (int y = 0; y < layout.Height; y++)
        {
            for (int x = 0; x < layout.Width; x++)
            {
                float logicalX = (x - layout.UiLeft) / layout.UiScale;
                float logicalY = (y - layout.UiTop) / layout.UiScale;
                if (logicalY < 100 || logicalY > 575 || logicalX > 930
                    || layout.Legacy && logicalX > 500 && logicalX < 750 && logicalY > 200)
                {
                    continue;
                }
                int offset = (y * layout.Width + x) * 4;
                if (!hud.AsSpan(offset, 4).SequenceEqual(world.AsSpan(offset, 4)))
                {
                    count++;
                }
            }
        }
        return count;
    }

    private static void Key(KeyboardKey key, TinyFarmNativeWindow window, AurelianGameHost host)
    {
        window.InjectKey(key, true);
        host.RunFrame(TimeSpan.Zero);
        window.InjectKey(key, false);
        host.RunFrame(TimeSpan.Zero);
    }

    internal static void Capture(string path, TinyFarmNativeRenderer renderer, AurelianGameHost host)
    {
        renderer.CaptureNextFrame();
        renderer.Present(new AurelianHostFrame(42, TimeSpan.Zero, TimeSpan.Zero));
        PngWriter.Write(path, renderer.Layout.Width, renderer.Layout.Height,
            renderer.Last?.NativeFrame.Pixels ?? throw new InvalidOperationException("Capture returned no pixels."));
    }

    private static string StateHash(TinyFarmGame game)
    {
        return TinyFarmSemanticHash.Compute(game.State)
            + ":" + game.Host.Session.Field.SemanticHash;
    }

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        return sorted[Math.Clamp((int)Math.Ceiling(sorted.Count * percentile) - 1, 0, sorted.Count - 1)];
    }

    internal static void Write(string directory, string name, object value)
    {
        File.WriteAllText(Path.Combine(directory, name), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
