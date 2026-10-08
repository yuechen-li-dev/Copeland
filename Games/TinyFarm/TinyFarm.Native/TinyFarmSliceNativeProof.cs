using System.Diagnostics;
using System.Text.Json;
using Aurelian.GameHost;
using InputMan.Aurelian;
using InputMan.Core;
using TinyFarm.Core;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

/// <summary>Continuous native input walk-through. No fixture loads or coordinate writes during the loop.</summary>
internal static class TinyFarmSliceNativeProof
{
    public static void Run(string root, TinyFarmGame game, AurelianInputAdapter input,
        TinyFarmNativeWindow window, TinyFarmNativeRenderer renderer, AurelianGameHost host)
    {
        string output = Path.Combine(root, "artifacts", "tinyfarm-gate-a");
        if (renderer.Layout.Width != 1920 || renderer.Layout.Height != 1080)
        {
            output = Path.Combine(output, $"{renderer.Layout.Width}x{renderer.Layout.Height}");
        }
        Directory.CreateDirectory(output);
        var timings = new List<double>();
        int frames = 0;
        string movie = Path.Combine(output, "opening-loop.mp4");
        using Process encoder = Process.Start(new ProcessStartInfo("ffmpeg")
        {
            Arguments = $"-y -loglevel error -f rawvideo -pixel_format rgba -video_size {renderer.Layout.Width}x{renderer.Layout.Height} -framerate 10 -i pipe:0 -an -c:v libx264 -preset fast -crf 24 -pix_fmt yuv420p \"{movie}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true
        }) ?? throw new InvalidOperationException("Could not start the recording encoder.");
        void Frame()
        {
            bool record = frames % 6 == 0;
            if (record)
            {
                renderer.CaptureNextFrame();
            }
            long started = Stopwatch.GetTimestamp();
            host.RunFrame(TimeSpan.FromSeconds(1.0 / 60));
            timings.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            if (record)
            {
                encoder.StandardInput.BaseStream.Write(renderer.Last!.NativeFrame.Pixels!);
            }
            frames++;
        }
        void Key(KeyboardKey key)
        {
            window.InjectKey(key, true);
            Frame();
            window.InjectKey(key, false);
            Frame();
        }
        void Hold(int x, int y)
        {
            window.InjectKey(KeyboardKey.A, x < 0);
            window.InjectKey(KeyboardKey.D, x > 0);
            window.InjectKey(KeyboardKey.W, y < 0);
            window.InjectKey(KeyboardKey.S, y > 0);
        }
        void Walk(float x, float y)
        {
            var target = new ScenePosition((int)(x * 1024), (int)(y * 1024));
            for (int step = 0; step < 600; step++)
            {
                ScenePosition current = game.State.ActorScene(TinyFarmIds.Player).WorldPosition;
                int dx = target.XUnits - current.XUnits;
                int dy = target.YUnits - current.YUnits;
                if (Math.Abs(dx) < 65 && Math.Abs(dy) < 65)
                {
                    Hold(0, 0);
                    Frame();
                    return;
                }
                Hold(Math.Abs(dx) < 40 ? 0 : Math.Sign(dx), Math.Abs(dy) < 40 ? 0 : Math.Sign(dy));
                Frame();
            }
            throw new InvalidOperationException($"Walk stalled at {game.State.ActorScene(TinyFarmIds.Player).WorldPosition} toward {x},{y}.");
        }
        void Face(ActorFacing facing)
        {
            (int x, int y) = facing switch
            {
                ActorFacing.Left => (-1, 0),
                ActorFacing.Right => (1, 0),
                ActorFacing.Up => (0, -1),
                _ => (0, 1)
            };
            Hold(x, y);
            Frame();
            Hold(0, 0);
            Frame();
        }
        void Shot(string name)
        {
            TinyFarmM25NativeProof.Capture(Path.Combine(output, name + ".png"), renderer, host);
        }
        void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        Key(KeyboardKey.Enter);
        input.SetContexts(game.Contexts);
        Shot("garden-hud");
        string beforeHud = TinyFarmSemanticHash.Compute(game.State);
        window.InjectKey(KeyboardKey.F9, true);
        host.RunFrame(TimeSpan.Zero);
        window.InjectKey(KeyboardKey.F9, false);
        host.RunFrame(TimeSpan.Zero);
        Require(beforeHud == TinyFarmSemanticHash.Compute(game.State), "HUD changed semantic state.");
        Shot("garden-world");
        Key(KeyboardKey.F9);
        Walk(6.5f, 6.5f);
        Face(ActorFacing.Up);
        Key(KeyboardKey.E);
        Require(game.Dialogue.IsActive, "Opening neighbour conversation did not start.");
        Shot("mara-conversation");
        Key(KeyboardKey.Enter);
        Require(!game.Dialogue.IsActive && !game.State.Facts.Contains(WorldFact.MaraNeedsDelivery),
            "Opening conversation leaked the closed prototype quest.");
        Walk(7.5f, 6.5f);
        Face(ActorFacing.Up);
        Key(KeyboardKey.E);
        Require(game.State.ProductCount(TinyFarmIds.Player, TinyFarmIds.Turnip) > 0, "Starter harvest failed: " + game.Status + " / target=" + TinyFarmSpatialQueries.SelectInteractionTarget(game.State, TinyFarmIds.Player, game.Definitions.Scenes) + " / player=" + game.State.ActorScene(TinyFarmIds.Player));
        Walk(9.5f, 6.5f);
        Face(ActorFacing.Up);
        Key(KeyboardKey.K);
        Key(KeyboardKey.E);
        Require(game.State.FarmPlots.Any(plot => plot.PlantedByPlayer && plot.WateredToday), "Plant and water failed.");
        Walk(5.5f, 4.5f);
        Face(ActorFacing.Left);
        Key(KeyboardKey.E);
        Require(game.State.CurrentScene == TinyFarmSceneIds.Residence, "House doorway failed.");
        Walk(6.5f, 5.5f);
        Face(ActorFacing.Up);
        Key(KeyboardKey.E);
        Require(game.State.ProductCount(TinyFarmIds.Player, new ProductId("turnip-broth")) == 1, "Cooking failed.");
        Shot("hearth-house");
        Walk(3, 6.5f);
        Face(ActorFacing.Up);
        Key(KeyboardKey.E);
        Require(game.State.Slice!.Slept, "Sleep failed.");
        Walk(10.5f, 7.1f);
        Face(ActorFacing.Down);
        Key(KeyboardKey.E);
        Require(game.State.CurrentScene == TinyFarmSceneIds.Farm, "Return to garden failed.");
        Walk(9.5f, 6.5f);
        Face(ActorFacing.Up);
        Key(KeyboardKey.E);
        Require(game.State.Slice!.OwnHarvest, "Own harvest did not complete.");
        Walk(17.5f, 6.5f);
        Face(ActorFacing.Right);
        Key(KeyboardKey.E);
        Require(game.State.CurrentScene == TinyFarmSceneIds.Overworld, "Farm exit failed.");
        Shot("riverwood-approach");
        Walk(6, 8);
        Walk(13, 8);
        Walk(16.5f, 8);
        Walk(18, 6);
        Walk(19.5f, 3.5f);
        Face(ActorFacing.Up);
        Key(KeyboardKey.E);
        Require(game.State.CurrentScene == TinyFarmSceneIds.DungeonEntrance, "Burrow entry failed.");
        Walk(5.5f, 6.5f);
        for (int tick = 0; tick < 240 && game.State.Slice!.SlimePhase != SlimePhase.Windup; tick++)
        {
            Frame();
        }
        Require(game.State.Slice!.SlimePhase == SlimePhase.Windup, "Slime never telegraphed.");
        Shot("slime-telegraph");
        for (int tick = 0; tick < 24; tick++)
        {
            Frame();
        }
        Hold(0, 1);
        Key(KeyboardKey.Space);
        Hold(0, 0);
        for (int tick = 0; tick < 48; tick++)
        {
            Frame();
        }
        TinyFarmSliceState beforeContact = game.State.Slice!;
        Walk(beforeContact.SlimePosition.XUnits / 1024f - .4f, beforeContact.SlimePosition.YUnits / 1024f + .3f);
        for (int tick = 0; tick < 240 && game.State.Slice!.Health == 12; tick++)
        {
            Frame();
        }
        Require(game.State.Slice!.Health < 12, "Contact attack never hurt the player.");
        Key(KeyboardKey.R);
        Require(game.State.Slice!.Health == 12
            && game.State.ProductCount(TinyFarmIds.Player, new ProductId("turnip-broth")) == 0, "Broth did not support adventure.");
        Shot("broth-healing");
        for (int attempt = 0; attempt < 12 && game.State.Slice!.Defeats == 0; attempt++)
        {
            TinyFarmSliceState slice = game.State.Slice!;
            ScenePosition player = game.State.ActorScene(TinyFarmIds.Player).WorldPosition;
            if (slice.SlimePhase == SlimePhase.Windup)
            {
                Shot("slime-telegraph");
                Hold(0, 1);
                Key(KeyboardKey.Space);
                Hold(0, 0);
                for (int tick = 0; tick < 22; tick++)
                {
                    Frame();
                }
            }
            slice = game.State.Slice!;
            if (player.SquaredDistance(slice.SlimePosition) > 1100L * 1100)
            {
                Walk(slice.SlimePosition.XUnits / 1024f - .9f, slice.SlimePosition.YUnits / 1024f);
            }
            player = game.State.ActorScene(TinyFarmIds.Player).WorldPosition;
            int dx = game.State.Slice!.SlimePosition.XUnits - player.XUnits;
            int dy = game.State.Slice.SlimePosition.YUnits - player.YUnits;
            ActorFacing facing;
            if (Math.Abs(dx) > Math.Abs(dy))
            {
                facing = dx < 0 ? ActorFacing.Left : ActorFacing.Right;
            }
            else
            {
                facing = dy < 0 ? ActorFacing.Up : ActorFacing.Down;
            }
            Face(facing);
            Key(KeyboardKey.J);
            for (int tick = 0; tick < 24; tick++)
            {
                Frame();
            }
        }
        Require(game.State.Slice!.Defeats == 1, "Sword encounter did not converge.");
        Key(KeyboardKey.R);
        Shot("burrow-cleared");
        Walk(2, 6.5f);
        Face(ActorFacing.Left);
        Key(KeyboardKey.E);
        Require(game.State.CurrentScene == TinyFarmSceneIds.Overworld, "Retreat route failed.");
        Walk(18, 6);
        Walk(16.5f, 8);
        Walk(13, 8);
        Walk(6, 8);
        Walk(3.5f, 7.5f);
        Face(ActorFacing.Left);
        Key(KeyboardKey.E);
        Require(game.State.CurrentScene == TinyFarmSceneIds.Farm && game.State.Slice!.LoopComplete, "Home loop incomplete.");
        Require(game.Save(), "Native save failed.");
        string saveHash = TinyFarmSemanticHash.Compute(game.State);
        Require(game.Load() && saveHash == TinyFarmSemanticHash.Compute(game.State), "Native load changed state.");
        Key(KeyboardKey.F9);
        Shot("home-after-adventure");
        window.InjectKey(KeyboardKey.F10, true);
        host.RunFrame(TimeSpan.Zero);
        window.InjectKey(KeyboardKey.F10, false);
        host.RunFrame(TimeSpan.Zero);
        Shot("presentation-inspector");
        window.InjectKey(KeyboardKey.F10, true);
        host.RunFrame(TimeSpan.Zero);
        window.InjectKey(KeyboardKey.F10, false);
        host.RunFrame(TimeSpan.Zero);
        encoder.StandardInput.Close();
        encoder.WaitForExit();
        Require(encoder.ExitCode == 0, "Recording encoder failed.");
        timings.Sort();
        using var stream = File.Create(Path.Combine(output, "native-proof.json"));
        using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        json.WriteString("milestone", "TinyFarm MVP Gate A");
        json.WriteString("path", "Default native game / InputMan Silk injected keys / authoritative resolver / Vulkan");
        json.WriteBoolean("coordinateWritesDuringLoop", false);
        json.WriteBoolean("harvestCookPlantWaterSleepOwnHarvestFightRetreatSaveLoad", true);
        json.WriteBoolean("hudSemanticNonInterference", true);
        json.WriteBoolean("humanFreshPlayTest", false);
        json.WriteNumber("width", renderer.Layout.Width);
        json.WriteNumber("height", renderer.Layout.Height);
        json.WriteNumber("frames", frames);
        json.WriteNumber("simulationSeconds", frames / 60.0);
        json.WriteNumber("frameP50MsIncludingPeriodicReadback", timings[timings.Count / 2]);
        json.WriteNumber("frameP95MsIncludingPeriodicReadback", timings[(int)(timings.Count * .95)]);
        json.WriteNumber("frameP99MsIncludingPeriodicReadback", timings[(int)(timings.Count * .99)]);
        json.WriteString("finalHash", saveHash);
        json.WriteEndObject();
        json.Flush();
        MeasureFrames(output, game, renderer, host);
        Console.WriteLine("TINYFARM_GATE_A_NATIVE_LOOP_PASSED");
    }
    private static void MeasureFrames(string output, TinyFarmGame game, TinyFarmNativeRenderer renderer, AurelianGameHost host)
    {
        using var stream = File.Create(Path.Combine(output, "performance.json"));
        using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        json.WriteStartArray();
        foreach (bool hud in new[] { true, false })
        {
            game.Presentation.HudVisible = hud;
            for (int warm = 0; warm < 30; warm++)
            {
                host.RunFrame(TimeSpan.FromSeconds(1.0 / 60));
            }
            renderer.ResetPerformanceMetrics();
            var timings = new List<double>();
            for (int frame = 0; frame < 180; frame++)
            {
                long start = Stopwatch.GetTimestamp();
                host.RunFrame(TimeSpan.FromSeconds(1.0 / 60));
                timings.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }
            timings.Sort();
            json.WriteStartObject();
            json.WriteBoolean("hud", hud);
            json.WriteString("timing", "Complete native host frame, CPU wall clock, synchronous Vulkan, VSync off, no readback or recording");
            json.WriteNumber("p50Ms", timings[90]);
            json.WriteNumber("p95Ms", timings[171]);
            json.WriteNumber("p99Ms", timings[178]);
            json.WriteNumber("drawsPerFrame", renderer.DrawCalls / 180.0);
            json.WriteNumber("textureUploadsTotal", renderer.SpriteTextureUploads);
            json.WriteEndObject();
        }
        json.WriteEndArray();
    }

}
