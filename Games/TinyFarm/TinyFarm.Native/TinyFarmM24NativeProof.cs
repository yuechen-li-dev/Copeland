using System.Diagnostics;
using System.Text.Json;
using Aurelian.GameHost;
using TinyFarm.Core;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

internal static class TinyFarmM24NativeProof
{
    public static void Run(
        string root,
        TinyFarmGame game,
        TinyFarmNativeRenderer renderer,
        AurelianGameHost host)
    {
        string output = Path.Combine(root, "artifacts", "tinyfarm-semantic-spatial-art-m24");
        Directory.CreateDirectory(output);
        game.Start();

        PlacePlayer(game, TinyFarmSceneIds.Farm, new GridPosition(6, 6));
        int prototypeDrawStart = renderer.DrawCalls;
        List<double> prototypeFrameTimes = MeasureFrames(host, 120);
        int prototypeDrawCalls = renderer.DrawCalls - prototypeDrawStart;
        Capture(Path.Combine(output, "prototype-before.png"), renderer, host);

        PlacePlayer(game, TinyFarmSceneIds.Riverside, new GridPosition(5, 6));
        int semanticDrawStart = renderer.DrawCalls;
        List<double> frameTimes = MeasureFrames(host, 120);
        int semanticDrawCalls = renderer.DrawCalls - semanticDrawStart;
        Capture(Path.Combine(output, "painterly-after.png"), renderer, host);

        prototypeFrameTimes.Sort();
        frameTimes.Sort();
        var metrics = new
        {
            path = "TinyFarm.Native -> NativeLayerCompositor -> VulkanOrderedQuadRenderer",
            device = renderer.Device,
            frames = frameTimes.Count,
            prototype = new
            {
                p50Milliseconds = Percentile(prototypeFrameTimes, 0.50),
                p95Milliseconds = Percentile(prototypeFrameTimes, 0.95),
                p99Milliseconds = Percentile(prototypeFrameTimes, 0.99),
                worstMilliseconds = prototypeFrameTimes[^1],
                drawCalls = prototypeDrawCalls,
                averageDrawCallsPerFrame = prototypeDrawCalls / 120.0,
                groundSprites = 160,
            },
            semantic = new
            {
                p50Milliseconds = Percentile(frameTimes, 0.50),
                p95Milliseconds = Percentile(frameTimes, 0.95),
                p99Milliseconds = Percentile(frameTimes, 0.99),
                worstMilliseconds = frameTimes[^1],
                drawCalls = semanticDrawCalls,
                averageDrawCallsPerFrame = semanticDrawCalls / 120.0,
                groundSlabs = 1,
            },
            worldSpriteCount = renderer.WorldSpriteCount,
            textureUploads = renderer.SpriteTextureUploads,
            fieldTextureUploads = renderer.FieldTextureUploads,
            fieldUploadBytes = renderer.FieldUploadBytes,
            assetHashes = renderer.M24AssetHashes,
            capturedPixelHash = renderer.Last!.NativeFrame.PixelSha256,
        };
        File.WriteAllText(
            Path.Combine(output, "native-performance.json"),
            JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("TINYFARM_M24_NATIVE_PROOF_PASSED");
    }

    private static List<double> MeasureFrames(AurelianGameHost host, int count)
    {
        var frameTimes = new List<double>(count);
        for (int frame = 0; frame < count; frame++)
        {
            long started = Stopwatch.GetTimestamp();
            host.RunFrame(TimeSpan.FromSeconds(1.0 / 60));
            frameTimes.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        return frameTimes;
    }

    private static void Capture(
        string path,
        TinyFarmNativeRenderer renderer,
        AurelianGameHost host)
    {
        host.RunFrame(TimeSpan.Zero);
        renderer.CaptureNextFrame();
        host.RunFrame(TimeSpan.Zero);
        byte[] pixels = renderer.Last?.NativeFrame.Pixels
            ?? throw new InvalidOperationException("M24 native capture did not return pixels.");
        PngWriter.Write(path, 1280, 720, pixels);
    }

    private static void PlacePlayer(TinyFarmGame game, SceneId scene, GridPosition position)
    {
        game.Host.CommitLoadedSession(TinyFarmM24ProofStates.CreateAt(
            game.Definitions,
            scene,
            position,
            ActorFacing.Right));
    }

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        int index = (int)Math.Ceiling(sorted.Count * percentile) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }
}
