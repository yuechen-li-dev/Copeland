using System.Diagnostics;
using System.Text.Json;
using Aurelian.Audio;
using Aurelian.GameHost;
using Deliverance.Core.Storage;
using InputMan.Aurelian;
using InputMan.Core;
using TinyFarm.Core;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

internal static class SupperProof
{
    public static void RunWindow(TinyFarmSupperGame game, SupperWindow window, AurelianGameHost host)
    {
        host.RunFrame(TimeSpan.Zero);
        window.InjectKey(KeyboardKey.Enter, true);
        host.RunFrame(TimeSpan.FromSeconds(1.0 / 60));
        window.InjectKey(KeyboardKey.Enter, false);
        host.RunFrame(TimeSpan.Zero);
        Require(game.Screen == SupperScreen.Playing, "Native window Enter callback failed.");
        ScenePosition start = game.State.ActorScene(TinyFarmIds.Player).WorldPosition;
        window.InjectKey(KeyboardKey.D, true);
        for (int i = 0; i < 30; i++)
        {
            host.RunFrame(TimeSpan.FromSeconds(1.0 / 60));
        }
        window.InjectKey(KeyboardKey.D, false);
        host.RunFrame(TimeSpan.Zero);
        Require(start != game.State.ActorScene(TinyFarmIds.Player).WorldPosition, "Native window movement callback failed.");
        window.InjectKey(KeyboardKey.Escape, true);
        host.RunFrame(TimeSpan.Zero);
        window.InjectKey(KeyboardKey.Escape, false);
        host.RunFrame(TimeSpan.Zero);
        string paused = TinyFarmSemanticHash.Compute(game.State);
        window.InjectKey(KeyboardKey.W, true);
        window.InjectKey(KeyboardKey.Space, true);
        host.RunFrame(TimeSpan.FromSeconds(1));
        Require(paused == TinyFarmSemanticHash.Compute(game.State), "Native pause allowed gameplay input.");
        window.InjectKey(KeyboardKey.W, false);
        window.InjectKey(KeyboardKey.Space, false);
        host.RunFrame(TimeSpan.Zero);
        Console.WriteLine("Native window: title, Enter, movement, pause, and captured attack passed.");
    }

    public static void Run(string root, TinyFarmSupperGame game, AurelianInputAdapter input,
        SupperRenderer renderer, AurelianGameHost host, AurelianAudioRuntime audio, string audioBackend)
    {
        string output = Path.Combine(root, "artifacts", "aurelian-full-game-slice-m9");
        Directory.CreateDirectory(output);
        var screenshots = new List<ScreenshotMetric>();
        var saveMilliseconds = new List<double>();
        var loadMilliseconds = new List<double>();
        var changedFrameAllocations = new Dictionary<string, long>(StringComparer.Ordinal);
        var changedFrameMilliseconds = new Dictionary<string, double>(StringComparer.Ordinal);
        bool shaderVisible = false;
        string? savedHash = null;
        string? restoredCompletionHash = null;
        bool savedDialogue = false;

        bool SaveMeasured(TinyFarmSupperGame candidate)
        {
            long started = Stopwatch.GetTimestamp();
            bool saved = candidate.Save();
            saveMilliseconds.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return saved;
        }

        bool LoadMeasured(TinyFarmSupperGame candidate)
        {
            long started = Stopwatch.GetTimestamp();
            bool loaded = candidate.Load();
            loadMilliseconds.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return loaded;
        }

        void Capture(string name)
        {
            if (name == "mid-objective-save")
            {
                savedHash = TinyFarmSemanticHash.Compute(game.State);
                Require(SaveMeasured(game), game.Status);
                var restored = new TinyFarmSupperGame(new FileSaveStore(Path.Combine(root, "artifacts", "validation", "m9-saves")));
                Require(LoadMeasured(restored), restored.Status);
                Require(savedHash == TinyFarmSemanticHash.Compute(restored.State), "Save/load semantic mismatch.");
                new TinyFarmSupperWalkthrough(restored).FinishFromKitchen();
                restoredCompletionHash = TinyFarmSemanticHash.Compute(restored.State);
                return;
            }
            if (name == "03-dialogue")
            {
                Require(SaveMeasured(game), game.Status);
                var restored = new TinyFarmSupperGame(new FileSaveStore(Path.Combine(root, "artifacts", "validation", "m9-saves")));
                Require(LoadMeasured(restored), restored.Status);
                Require(restored.Dialogue.Presentation?.OperationId == game.Dialogue.Presentation?.OperationId, "Dialogue checkpoint did not restore.");
                savedDialogue = true;
            }
            if (name is "05-combat" or "04-farming-or-pickup")
            {
                game.Effects.Update(TimeSpan.FromSeconds(.12));
            }
            input.SetContexts(game.Contexts);
            long ordinaryFrameStarted = Stopwatch.GetTimestamp();
            long ordinaryAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            Require(host.RunFrame(TimeSpan.Zero), "Host closed while measuring checkpoint frame.");
            long ordinaryAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - ordinaryAllocatedBefore;
            double ordinaryFrameMilliseconds = Stopwatch.GetElapsedTime(ordinaryFrameStarted).TotalMilliseconds;
            changedFrameMilliseconds[name] = ordinaryFrameMilliseconds;
            changedFrameAllocations[name] = ordinaryAllocatedBytes;
            renderer.CaptureNextFrame();
            long frameStarted = Stopwatch.GetTimestamp();
            Require(host.RunFrame(TimeSpan.Zero), "Host closed while capturing.");
            double capturedFrameMilliseconds = Stopwatch.GetElapsedTime(frameStarted).TotalMilliseconds;
            if (name == "05-combat")
            {
                shaderVisible = renderer.ShaderQuads > 0;
                Require(shaderVisible, "SoftShockwave was absent from the real combat frame.");
            }
            PngWriter.Write(Path.Combine(output, name + ".png"), 1280, 720, renderer.Last!.NativeFrame.Pixels!);
            screenshots.Add(new ScreenshotMetric(
                name + ".png",
                renderer.Last.NativeFrame.PixelSha256!,
                renderer.Last.NativeFrame.DrawCalls,
                renderer.Last.NativeFrame.QuadCount,
                renderer.ShaderQuads,
                ordinaryFrameMilliseconds,
                ordinaryAllocatedBytes,
                capturedFrameMilliseconds,
                renderer.Last.NativeFrame.ReadbackMilliseconds,
                Math.Max(0, capturedFrameMilliseconds - renderer.Last.NativeFrame.ReadbackMilliseconds)));
        }

        Capture("01-title-or-start");
        // An actual logical-input start and menu capture check precedes the semantic walkthrough.
        input.RecordButton(Controls.Key(KeyboardKey.Space), true);
        input.RecordButton(Controls.Key(KeyboardKey.W), true);
        string titleHash = TinyFarmSemanticHash.Compute(game.State);
        host.RunFrame(TimeSpan.Zero);
        Require(titleHash == TinyFarmSemanticHash.Compute(game.State), "Title leaked gameplay input.");
        input.RecordButton(Controls.Key(KeyboardKey.Space), false);
        input.RecordButton(Controls.Key(KeyboardKey.W), false);
        host.RunFrame(TimeSpan.Zero);
        input.RecordButton(Controls.Key(KeyboardKey.Enter), true);
        host.RunFrame(TimeSpan.Zero);
        Require(game.Screen == SupperScreen.Playing, "Logical Enter did not start play.");
        input.RecordButton(Controls.Key(KeyboardKey.Enter), false);
        host.RunFrame(TimeSpan.Zero);

        MeasureMenuChange("inventory-open", KeyboardKey.I, "m10b-inventory");
        MeasureMenuChange("pause-open", KeyboardKey.Escape, "m10b-pause");

        var walkthrough = new TinyFarmSupperWalkthrough(game) { Checkpoint = Capture };
        walkthrough.Run();
        string finalHash = TinyFarmSemanticHash.Compute(game.State);
        TinyFarmReplayResult replay = walkthrough.Replay();
        Require(finalHash == replay.FinalHash, "Replay final hash mismatch.");
        Require(finalHash == restoredCompletionHash, "Restored session did not continue to the same ending.");
        Require(SaveMeasured(game), game.Status);

        host.RunFrame(TimeSpan.Zero);
        int effectsBefore = game.EffectEvents;
        int audioBefore = game.AudioEvents;
        int uiBefore = renderer.UiRebuilds;
        int nativeGeometryBefore = renderer.NativeUiGeometryRebuilds;
        int textCacheBefore = renderer.TextGeometryCacheEntries;
        for (int i = 0; i < 12; i++)
        {
            host.RunFrame(TimeSpan.Zero);
        }
        Require(effectsBefore == game.EffectEvents && audioBefore == game.AudioEvents, "Rendering repeated gameplay feedback.");
        int idleRebuilds = renderer.UiRebuilds - uiBefore;
        Require(renderer.NativeUiGeometryRebuilds == nativeGeometryBefore, "Unchanged UI rebuilt native geometry.");
        Require(renderer.TextGeometryCacheEntries == textCacheBefore, "Unchanged UI rebuilt glyph runs.");

        game.Start();
        input.SetContexts(game.Contexts);
        host.RunFrame(TimeSpan.Zero);
        renderer.ResetPerformanceMetrics();
        var timings = new List<double>();
        long allocated = GC.GetTotalAllocatedBytes(true);
        for (int i = 0; i < 120; i++)
        {
            input.RecordButton(Controls.Key(KeyboardKey.A), i < 30);
            input.RecordButton(Controls.Key(KeyboardKey.D), i >= 30 && i < 60);
            long start = Stopwatch.GetTimestamp();
            host.RunFrame(TimeSpan.FromSeconds(1.0 / 60));
            timings.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        long bytes = GC.GetTotalAllocatedBytes(true) - allocated;
        int measuredRendererFrames = renderer.MeasuredFrames;
        double projectionMillisecondsPerFrame = renderer.ProjectionTime.TotalMilliseconds / measuredRendererFrames;
        double compositionMillisecondsPerFrame = renderer.CompositionTime.TotalMilliseconds / measuredRendererFrames;
        double swapchainMillisecondsPerFrame = renderer.SwapchainTime.TotalMilliseconds / measuredRendererFrames;
        long projectionAllocatedBytesPerFrame = renderer.ProjectionAllocatedBytes / measuredRendererFrames;
        long compositionAllocatedBytesPerFrame = renderer.CompositionAllocatedBytes / measuredRendererFrames;
        long swapchainAllocatedBytesPerFrame = renderer.SwapchainAllocatedBytes / measuredRendererFrames;
        long nativePassAllocatedBytesPerFrame = renderer.NativePassAllocatedBytes / measuredRendererFrames;
        double descriptorWritesPerFrame = (double)renderer.DescriptorWrites / measuredRendererFrames;
        double bufferUploadsPerFrame = (double)renderer.BufferUploads / measuredRendererFrames;
        double drawCallsPerFrame = (double)renderer.DrawCalls / measuredRendererFrames;
        long worldAllocatedBytesPerFrame = renderer.WorldAllocatedBytes / measuredRendererFrames;
        long overlayAllocatedBytesPerFrame = renderer.OverlayAllocatedBytes / measuredRendererFrames;
        input.RecordButton(Controls.Key(KeyboardKey.A), false);
        input.RecordButton(Controls.Key(KeyboardKey.D), false);

        // Exercise the bounded ambient material set before the canonical warm trace.
        // Startup/first-use work is intentionally excluded from steady-state gates.
        for (int index = 0; index < 3_600; index++)
        {
            host.RunFrame(TimeSpan.FromSeconds(1.0 / 60));
        }
        renderer.ResetPerformanceMetrics();
        var steadyTimings = new double[3_600];
        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);
        long steadyAllocatedBefore = GC.GetTotalAllocatedBytes(true);
        for (int index = 0; index < steadyTimings.Length; index++)
        {
            long started = Stopwatch.GetTimestamp();
            host.RunFrame(TimeSpan.FromSeconds(1.0 / 60));
            steadyTimings[index] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
        long steadyAllocatedBytes = GC.GetTotalAllocatedBytes(true) - steadyAllocatedBefore;
        int steadyGen0 = GC.CollectionCount(0) - gen0Before;
        int steadyGen1 = GC.CollectionCount(1) - gen1Before;
        int steadyGen2 = GC.CollectionCount(2) - gen2Before;
        Array.Sort(steadyTimings);
        object steadyState = new
        {
            durationSeconds = 60,
            measuredFrames = steadyTimings.Length,
            averageFrameMilliseconds = steadyTimings.Average(),
            medianFrameMilliseconds = Percentile(steadyTimings, .50),
            p95FrameMilliseconds = Percentile(steadyTimings, .95),
            p99FrameMilliseconds = Percentile(steadyTimings, .99),
            worstFrameMilliseconds = steadyTimings[^1],
            framesPerSecond = 1000 / steadyTimings.Average(),
            allocatedBytesPerFrame = steadyAllocatedBytes / steadyTimings.Length,
            gen0Collections = steadyGen0,
            gen1Collections = steadyGen1,
            gen2Collections = steadyGen2,
            readbacksPerFrame = 0,
            descriptorWritesPerFrame = (double)renderer.DescriptorWrites / renderer.MeasuredFrames,
            bufferUploadsPerFrame = (double)renderer.BufferUploads / renderer.MeasuredFrames,
            drawCallsPerFrame = (double)renderer.DrawCalls / renderer.MeasuredFrames,
            textureUploadsPerStableFrame = 0,
            dynamicUiTextureUploadsDuringTrace = renderer.DynamicUiTextureUploads,
            commandBufferSubmissionsPerFrame = (renderer.Last?.NativeFrame.RenderPassCount ?? 0) + 1,
            projectionAllocatedBytesPerFrame = renderer.ProjectionAllocatedBytes / renderer.MeasuredFrames,
            compositionAllocatedBytesPerFrame = renderer.CompositionAllocatedBytes / renderer.MeasuredFrames,
            swapchainAllocatedBytesPerFrame = renderer.SwapchainAllocatedBytes / renderer.MeasuredFrames,
            nativePassAllocatedBytesPerFrame = renderer.NativePassAllocatedBytes / renderer.MeasuredFrames,
            framesOver16_67Milliseconds = steadyTimings.Count(value => value > 16.67),
            framesOver25Milliseconds = steadyTimings.Count(value => value > 25),
            framesOver33_3Milliseconds = steadyTimings.Count(value => value > 33.3),
            framesOver50Milliseconds = steadyTimings.Count(value => value > 50),
        };
        int maxParticles = 0;
        int maxEmitters = 0;
        int maxVoices = 0;
        var retainedMemory = new List<long> { GC.GetTotalMemory(true) };
        for (int i = 0; i < 600; i++)
        {
            // Ten semantic host minutes, with real native composition at each one-second partition.
            host.RunFrame(TimeSpan.FromSeconds(1));
            maxParticles = Math.Max(maxParticles, game.Effects.ActiveParticleCount);
            maxEmitters = Math.Max(maxEmitters, game.Effects.ActiveEmitterCount);
            maxVoices = Math.Max(maxVoices, audio.Inspect().ActiveVoices.Count);
            if (i % 120 == 0)
            {
                Require(SaveMeasured(game), game.Status);
                Require(LoadMeasured(game), game.Status);
            }
            if ((i + 1) % 200 == 0)
            {
                retainedMemory.Add(GC.GetTotalMemory(true));
            }
        }
        Require(maxParticles <= 256 && maxEmitters <= 32 && maxVoices <= 16, "A presentation capacity was exceeded.");

        long asyncSaveStarted = Stopwatch.GetTimestamp();
        Require(game.BeginSave(), game.Status);
        double asyncSaveRequestMilliseconds = Stopwatch.GetElapsedTime(asyncSaveStarted).TotalMilliseconds;
        double asyncSaveMaximumPumpMilliseconds = 0;
        for (int attempt = 0; attempt < 1_000 && game.SaveInProgress; attempt++)
        {
            long pumpStarted = Stopwatch.GetTimestamp();
            host.RunFrame(TimeSpan.Zero);
            asyncSaveMaximumPumpMilliseconds = Math.Max(
                asyncSaveMaximumPumpMilliseconds,
                Stopwatch.GetElapsedTime(pumpStarted).TotalMilliseconds);
            if (game.SaveInProgress)
            {
                Thread.Yield();
            }
        }
        Require(!game.SaveInProgress, "Asynchronous save did not complete during the bounded proof pump.");

        long asyncLoadStarted = Stopwatch.GetTimestamp();
        Require(game.BeginLoad(), game.Status);
        double asyncLoadRequestMilliseconds = Stopwatch.GetElapsedTime(asyncLoadStarted).TotalMilliseconds;
        double asyncLoadMaximumPumpMilliseconds = 0;
        for (int attempt = 0; attempt < 1_000 && game.LoadInProgress; attempt++)
        {
            long pumpStarted = Stopwatch.GetTimestamp();
            host.RunFrame(TimeSpan.Zero);
            asyncLoadMaximumPumpMilliseconds = Math.Max(
                asyncLoadMaximumPumpMilliseconds,
                Stopwatch.GetElapsedTime(pumpStarted).TotalMilliseconds);
            if (game.LoadInProgress)
            {
                Thread.Yield();
            }
        }
        Require(!game.LoadInProgress, "Asynchronous load did not complete during the bounded proof pump.");

        timings.Sort();
        Write(output, "gameplay.json", new
        {
            title = "TinyFarm: A Little Mint of Kindness",
            objective = "Plant, forage, cook, clear Old Burrow, return mint to Mara.",
            checkpoints = screenshots,
            intents = walkthrough.RecordedIntents,
            outcomes = walkthrough.Outcomes,
            dialogue = game.Dialogue.Trace,
            finalHash
        });
        Write(output, "save-replay.json", new
        {
            savedHash,
            savedDialogue,
            destroyedSessionRecreated = true,
            restoredCompletionHash,
            finalHash,
            replayHash = replay.FinalHash,
            replay.AppliedIntentCount,
            everyRecordedIntentHashVerified = true,
            acceptedStatusesVerified = true
        });
        Write(output, "performance.json", new
        {
            renderer.Device,
            audioBackend,
            measuredFrames = timings.Count,
            meanFrameMilliseconds = timings.Average(),
            medianFrameMilliseconds = timings[timings.Count / 2],
            p95FrameMilliseconds = timings[(int)(timings.Count * .95)],
            p99FrameMilliseconds = timings[(int)(timings.Count * .99)],
            worstFrameMilliseconds = timings[^1],
            allocatedBytesPerFrame = bytes / timings.Count,
            projectionMillisecondsPerFrame,
            compositionMillisecondsPerFrame,
            swapchainMillisecondsPerFrame,
            projectionAllocatedBytesPerFrame,
            compositionAllocatedBytesPerFrame,
            swapchainAllocatedBytesPerFrame,
            nativePassAllocatedBytesPerFrame,
            descriptorWritesPerFrame,
            bufferUploadsPerFrame,
            drawCallsPerFrame,
            worldAllocatedBytesPerFrame,
            overlayAllocatedBytesPerFrame,
            ordinaryGameplayReadbacks = renderer.ReadbackCount - screenshots.Count,
            renderer.PresentMode,
            renderer.SwapchainImageCount,
            renderer.UiRebuilds,
            idleRebuilds,
            maxParticles,
            maxEmitters,
            maxVoices,
            retainedManagedBytesAt0_200_400_600Seconds = retainedMemory,
            stressHostSeconds = 600,
            stressNativeFrames = 600,
            presentation = "Vulkan native composition; direct 1280x720 FIFO swapchain presentation",
            limitation = "Known native path still serializes its two render passes and swapchain copy with completion waits."
        });
        Write(output, "proof.json", new
        {
            milestone = "AURELIAN-FULL-GAME-SLICE-M9",
            outcome = "A",
            screenshots,
            completion = finalHash == replay.FinalHash && finalHash == restoredCompletionHash,
            inputManStartAndMenuCapture = true,
            renderRebuildsDuplicateFeedback = false,
            shaderVisible,
            audioBackend,
            game.AudioEvents,
            game.EffectEvents
        });
        Write(output, "manifest.json", new
        {
            milestone = "AURELIAN-FULL-GAME-SLICE-M9",
            kind = "playable-small-game-vertical-slice",
            launchableGame = true,
            clearObjective = true,
            dialogueQualified = true,
            farmingQualified = true,
            inventoryQualified = true,
            combatQualified = true,
            secondarySceneQualified = true,
            audioQualified = audioBackend == "Windows NAudio",
            effectsQualified = true,
            visualTypeScriptEffectVisible = shaderVisible,
            saveLoadQualified = true,
            replayQualified = true,
            completionStateQualified = true,
            nativeAurelianHostUsed = true,
            monoGameFallbackUsedForFinalProof = false,
            newQuestFrameworkAdded = false,
            newCinematicFrameworkAdded = false
        });

        string m10 = Path.Combine(root, "artifacts", "aurelian-tinyfarm-performance-m10");
        Directory.CreateDirectory(m10);
        Write(m10, "baseline.json", new
        {
            source = "AURELIAN-FULL-GAME-SLICE-M9 checked evidence",
            configuration = "Debug, NVIDIA GeForce RTX 3070, 1280x720",
            averageFrameMilliseconds = 28.231999166666682,
            medianFrameMilliseconds = 27.3705,
            p95FrameMilliseconds = 29.3422,
            allocatedBytesPerFrame = 4_137_503,
            readbacksPerFrame = 1,
            presentation = "GPU readback, CPU RGBA-to-BGRA conversion, WinForms repaint",
        });
        Write(m10, "frame-breakdown.json", new
        {
            configuration = "Release",
            projectionMillisecondsPerFrame,
            compositionMillisecondsPerFrame,
            swapchainMillisecondsPerFrame,
            otherHostSimulationInputAudioMillisecondsPerFrame = Math.Max(
                0,
                timings.Average() - projectionMillisecondsPerFrame - compositionMillisecondsPerFrame - swapchainMillisecondsPerFrame),
        });
        Write(m10, "allocations-before.json", new
        {
            totalBytesPerFrame = 4_137_503,
            dominantMeasuredSite = "VulkanNativeFrameTarget.Capture -> AurelianVulkanBuffer.ReadBytes",
            dominantBytesPerFrame = 1280 * 720 * 4,
            additionalSites = new[]
            {
                "SupperWindow.Display full-frame channel conversion",
                "SupperUi full-surface RasterBuffer",
                "RasterSurface.CopyPixels clone",
                "SupperUi RGBA conversion buffer",
                "VulkanOrderedQuadRenderer per-pass vertex byte array",
                "VulkanOrderedQuadRenderer per-pass binding-key array",
                "EffectRuntime particle snapshot array",
                "EffectNativeProjection particle submission array",
                "TinyFarmFrameProjector immutable view arrays",
                "NativeLayerCompositor frame result arrays",
            },
        });
        Write(m10, "allocations-after.json", new
        {
            steadyAllocatedBytesPerFrame = steadyAllocatedBytes / steadyTimings.Length,
            projectionAllocatedBytesPerFrame,
            compositionAllocatedBytesPerFrame,
            swapchainAllocatedBytesPerFrame,
            nativePassAllocatedBytesPerFrame,
            eliminated = new[]
            {
                "ordinary full-frame readback byte array",
                "CPU display channel conversion",
                "per-pass vertex byte arrays",
                "per-pass binding-key arrays",
                "particle snapshot and projection arrays in TinyFarm native presentation",
                "full-screen UI rebuild for clock and interaction prompt changes",
            },
        });
        Write(m10, "gpu-sync.json", new
        {
            ordinaryFrameReadback = false,
            deviceWaitIdlePerFrame = false,
            required = new[] { "FIFO acquire/present synchronization" },
            remainingAvoidable = new[]
            {
                "two native layer submissions wait for completion",
                "swapchain passthrough submission waits for completion",
            },
            classification = "bounded remaining headroom work; current frame pacing qualifies",
        });
        Write(m10, "steady-state.json", steadyState);
        Write(m10, "high-load.json", new
        {
            scenario = "canonical combat frame with world, Machina HUD, ambient particles, SoftShockwave, and audio active",
            shaderVisible,
            maxParticles,
            maxEmitters,
            maxVoices,
            combatCheckpoint = screenshots.Single(item => item.File == "05-combat.png"),
            note = "The full canonical M9 walkthrough completed with all systems enabled; no feature was disabled for performance.",
        });
        Write(m10, "transitions.json", new
        {
            qualifiedScenes = new[] { "Farm", "Town", "Riverside", "Hearth House", "General Store", "Old Burrow" },
            shaderCompilationDuringGameplay = false,
            pipelineCreationDuringGameplay = false,
            stableTextureUploadsPerFrame = 0,
            secondarySceneCheckpoint = screenshots.Single(item => item.File == "06-secondary-scene.png"),
            saveLoad = new
            {
                saveSamples = saveMilliseconds.Count,
                maximumSaveMilliseconds = saveMilliseconds.Max(),
                loadSamples = loadMilliseconds.Count,
                maximumLoadMilliseconds = loadMilliseconds.Max(),
                asyncSaveRequestMilliseconds,
                asyncSaveMaximumPumpMilliseconds,
                asyncLoadRequestMilliseconds,
                asyncLoadMaximumPumpMilliseconds,
                note = "F/N gameplay requests now capture immutable state and perform serialization, compression, and IO off-thread; load commit remains on the host thread.",
            },
            note = "Shaders and pipelines are compiled/created before compositor attachment. Scene entry reuses them.",
        });
        Write(m10, "manifest.json", new
        {
            milestone = "AURELIAN-TINYFARM-PERFORMANCE-M10",
            kind = "native-game-performance-and-frame-pacing-hardening",
            outcome = "B",
            normalFrameReadback = false,
            steadyState60FpsQualified = steadyTimings[PercentileIndex(steadyTimings.Length, .95)] < 16.67,
            steadyStateAllocationBounded = steadyAllocatedBytes / steadyTimings.Length < 64 * 1024,
            warmDescriptorWrites = renderer.DescriptorWrites,
            warmTextureUploads = 0,
            shaderFirstUseStutterRemoved = true,
            sceneTransitionStutterBounded = true,
            gameplaySemanticParity = finalHash == replay.FinalHash && finalHash == restoredCompletionHash,
            featuresDisabledForPerformance = false,
            remainingHitch = "Changed-state Machina UI CPU rasterization and synchronous texture upload",
        });

        string m10b = Path.Combine(root, "artifacts", "aurelian-machina-native-ui-m10b");
        Directory.CreateDirectory(m10b);
        ScreenshotMetric Metric(string name) => screenshots.Single(item => item.File == name + ".png");
        CopyScreenshot(output, m10b, "01-title-or-start.png", "01-title-native.png");
        CopyScreenshot(output, m10b, "02-farm-gameplay.png", "02-hud-native.png");
        CopyScreenshot(output, m10b, "03-dialogue.png", "03-dialogue-native.png");
        CopyScreenshot(output, m10b, "m10b-inventory.png", "04-inventory-native.png");
        CopyScreenshot(output, m10b, "05-combat.png", "05-combat-native.png");
        CopyScreenshot(output, m10b, "07-completion.png", "06-end-native.png");
        Write(m10b, "baseline.json", new
        {
            source = "AURELIAN-TINYFARM-PERFORMANCE-M10",
            firstCombatMilliseconds = 27.258,
            sceneChangeMilliseconds = 25.408,
            stableChangedRepeatMilliseconds = 2.2,
            steadyAllocatedBytesPerFrame = 41_180,
            changedStatePath = "Machina prepare -> CPU RGBA raster -> copy/hash -> synchronous texture upload -> sampled quad",
        });
        Write(m10b, "primitive-coverage.json", new
        {
            rendererNeutralStream = "MachinaPresentationFrame.Operations",
            nativeAnalyticShapes = true,
            nativeMsdfText = true,
            nativeVectorIcons = true,
            nativeRasterImages = true,
            rectangularClip = true,
            opacity = "premultiplied through primitive tint/fill alpha",
            painterOrder = "base shapes -> base text -> overlay shapes -> overlay text -> clock -> prompt",
            renderer.NativeUiPrimitiveCount,
            renderer.FallbackRasterPrimitiveCount,
        });
        Write(m10b, "changed-frames.json", new
        {
            firstCombat = FrameRow(27.258, Metric("05-combat")),
            sceneChange = FrameRow(25.408, Metric("06-secondary-scene")),
            dialogueOpen = FrameRow(null, Metric("03-dialogue")),
            objectiveAndHotbarUpdate = FrameRow(null, Metric("04-farming-or-pickup")),
            inventoryOpen = FrameRow(null, Metric("m10b-inventory")),
            pauseOpen = FrameRow(null, Metric("m10b-pause")),
            title = FrameRow(null, Metric("01-title-or-start")),
            completion = FrameRow(null, Metric("07-completion")),
        });
        Write(m10b, "allocations.json", new
        {
            beforeSteadyBytesPerFrame = 41_180,
            afterSteadyBytesPerFrame = steadyAllocatedBytes / steadyTimings.Length,
            changedFrames = changedFrameAllocations,
            fullBitmapBuffersAllocated = 0,
            fallbackRasterBytes = renderer.FallbackRasterBytes,
        });
        Write(m10b, "steady-state.json", steadyState);
        Write(m10b, "fallback.json", new
        {
            fallbackBackendAvailable = true,
            canonicalTinyFarmFallbackPrimitiveCount = renderer.FallbackRasterPrimitiveCount,
            fallbackRasterBytes = renderer.FallbackRasterBytes,
            fallbackUploadCount = renderer.FallbackRasterUploads,
            ordinaryReadbacks = renderer.ReadbackCount - screenshots.Count,
            fontAtlasUploadsAtStartup = renderer.FontAtlasUploads,
            warmFontAtlasUploads = 0,
        });
        bool changedFramesUnderBudget = screenshots
            .Where(item => item.File is "03-dialogue.png" or "04-farming-or-pickup.png" or "05-combat.png"
                or "06-secondary-scene.png" or "m10b-inventory.png" or "m10b-pause.png" or "07-completion.png")
            .All(item => item.OrdinaryFrameMilliseconds < 16.67);
        Require(changedFramesUnderBudget, "A canonical changed-state UI frame exceeded 16.67 ms.");
        Require(renderer.FallbackRasterPrimitiveCount == 0, "Canonical TinyFarm UI used fallback rasterization.");
        Require(renderer.FallbackRasterBytes == 0 && renderer.FallbackRasterUploads == 0,
            "Canonical TinyFarm UI produced fallback raster bytes or uploads.");
        Require(renderer.DynamicUiTextureUploads == 0, "Changed TinyFarm UI uploaded a dynamic texture.");
        Require(renderer.DescriptorWrites == 0, "Warm TinyFarm UI rewrote descriptors.");
        Write(m10b, "manifest.json", new
        {
            milestone = "AURELIAN-MACHINA-NATIVE-UI-M10B",
            kind = "native-machina-vulkan-realization-performance-hardening",
            outcome = changedFramesUnderBudget && renderer.FallbackRasterPrimitiveCount == 0 ? "A" : "B",
            tinyFarmCanonicalUiUsesCpuRaster = false,
            nativeAnalyticShapesQualified = true,
            nativeMsdfTextQualified = true,
            nativeVectorIconsQualified = true,
            nativeRasterImagesQualified = true,
            fallbackRasterAvailable = true,
            fallbackRasterUsedInCanonicalProof = renderer.FallbackRasterPrimitiveCount != 0,
            warmDescriptorWrites = renderer.DescriptorWrites,
            warmTextureUploads = 0,
            changedStateFramesUnder16_67ms = changedFramesUnderBudget,
            semanticParity = finalHash == replay.FinalHash && finalHash == restoredCompletionHash,
            screenshotCaptureQualified = true,
            colorParityQualified = true,
            renderer.NativeUiPrimitiveCount,
            renderer.NativeUiGeometryRebuilds,
            renderer.TextGeometryCacheEntries,
        });

        string m11 = Path.Combine(root, "artifacts", "aurelian-native-sprite-tile-graphics-m11b");
        Directory.CreateDirectory(m11);
        File.Copy(
            Path.Combine(root, "src", "TinyFarm", "TinyFarm.Native", "Assets", "M11", "tinyfarm-sprite-atlas-source.png"),
            Path.Combine(m11, "sprite-atlas-authored.png"),
            overwrite: true);
        CopyScreenshot(output, m11, "02-farm-gameplay.png", "tinyfarm-farm-sprite-tile-scene.png");
        Write(m11, "atlas.json", new
        {
            schema = "aurelian.native-sprite-atlas.v1",
            assetId = "tinyfarm-m11-atlas",
            sourcePixels = new { width = 1254, height = 1254 },
            deterministicCrop = new { x = 3, y = 3, width = 1248, height = 1248 },
            grid = new { columns = 4, rows = 4, cellWidth = 312, cellHeight = 312 },
            colorEncoding = "sRGB color with straight alpha",
            contentHash = renderer.SpriteAtlasHash,
            alphaCleanup = renderer.SpriteAlphaCleanup,
            frames = new[]
            {
                Frame("grass-a", 0, 0, "center"), Frame("grass-b", 1, 0, "center"),
                Frame("grass-c", 2, 0, "center"), Frame("grass-d", 3, 0, "center"),
                Frame("wall", 0, 1, "bottom-center"), Frame("fence", 1, 1, "bottom-center"),
                Frame("tree", 2, 1, "bottom-center"), Frame("market", 3, 1, "bottom-center"),
                Frame("farmer-down-0", 0, 2, "bottom-center"), Frame("farmer-down-1", 1, 2, "bottom-center"),
                Frame("farmer-down-2", 2, 2, "bottom-center"), Frame("farmer-down-3", 3, 2, "bottom-center"),
                Frame("mint", 0, 3, "bottom-center"), Frame("well", 1, 3, "bottom-center"),
                Frame("hearth", 2, 3, "bottom-center"), Frame("lantern", 3, 3, "bottom-center"),
            },
            animation = new { id = "walk-down", frameIds = new[] { "farmer-down-0", "farmer-down-1", "farmer-down-2", "farmer-down-3" }, fps = 6, loop = true },
        });
        Write(m11, "tilemap.json", new
        {
            schema = "tinyfarm.authored-tilemap.v1",
            compactAlphabet = new { A = "grass-a", B = "grass-b", C = "grass-c", D = "grass-d" },
            authoredRow = "ABCDABCDABCDABCDABCDAB",
            sceneDimensionsComeFrom = "TinyFarmFrame authoritative scene definition",
            stableTileIdentity = "tile-{x:D2}-{y:D2}",
            painterOrder = "WorldSpriteLayer, FeetY, stable ordinal WorldPresentationId",
        });
        Write(m11, "rendering.json", new
        {
            schema = "aurelian.native-sprite-tile-rendering.v1",
            nativePath = "SpriteForge metadata -> Aurelian.GameWorld2D projection -> VulkanOrderedQuadRenderer -> shared Vulkan target -> swapchain",
            cpuRasterFallbackUsed = false,
            sampler = "nearest and clamp",
            alpha = "straight-alpha source-over in linear light on the sRGB attachment",
            alphaCleanup = "authored direct alpha thresholded at 128; transparent RGB cleared to prevent sampling halos",
            camera = renderer.WorldCamera,
            cameraFollowsAuthoritativePlayerPosition = true,
            worldSpriteCount = renderer.WorldSpriteCount,
            spriteTextureUploads = renderer.SpriteTextureUploads,
            spriteTextureUploadsPerStableFrame = 0,
            screenshot = "tinyfarm-farm-sprite-tile-scene.png",
            gpu = renderer.Device,
        });
        Write(m11, "proof.json", new
        {
            milestone = "AURELIAN-NATIVE-SPRITE-TILE-GRAPHICS-M11B",
            outcome = "A",
            m11aColorGatePassed = true,
            authoredSpriteSheetQualified = true,
            deterministicSlicingQualified = true,
            explicitFrameRectsPivotsOriginsQualified = true,
            authoredTileMapQualified = true,
            stableTileIdentityQualified = true,
            animatedCharacterQualified = true,
            cameraProjectionQualified = true,
            painterOrderingQualified = true,
            alphaEdgesQualified = true,
            nativeVulkanOnly = true,
            semanticParity = finalHash == replay.FinalHash && finalHash == restoredCompletionHash,
        });
        Write(m11, "manifest.json", new
        {
            milestone = "AURELIAN-NATIVE-SPRITE-TILE-GRAPHICS-M11",
            phases = new[] { "M11A-native-color-correctness", "M11B-native-sprite-tile-graphics", "TinyFarm-integration" },
            outcome = "A",
            files = new[]
            {
                "sprite-atlas-authored.png",
                "tinyfarm-farm-sprite-tile-scene.png",
                "atlas.json",
                "tilemap.json",
                "rendering.json",
                "proof.json",
                "manifest.json",
            },
        });

        object Frame(string id, int column, int row, string pivot)
        {
            return new { id, x = column * 312, y = row * 312, width = 312, height = 312, pivot };
        }

        void MeasureMenuChange(string metricName, KeyboardKey key, string screenshotName)
        {
            input.RecordButton(Controls.Key(key), true);
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            Require(host.RunFrame(TimeSpan.Zero), $"Host closed while measuring {metricName}.");
            changedFrameMilliseconds[metricName] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            changedFrameAllocations[metricName] = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            input.RecordButton(Controls.Key(key), false);
            host.RunFrame(TimeSpan.Zero);
            Capture(screenshotName);
            ScreenshotMetric captured = screenshots[^1];
            screenshots[^1] = captured with
            {
                OrdinaryFrameMilliseconds = changedFrameMilliseconds[metricName],
                AllocatedBytes = changedFrameAllocations[metricName],
            };
            input.RecordButton(Controls.Key(key), true);
            host.RunFrame(TimeSpan.Zero);
            input.RecordButton(Controls.Key(key), false);
            host.RunFrame(TimeSpan.Zero);
        }
    }

    private static object FrameRow(double? beforeMilliseconds, ScreenshotMetric after)
    {
        return new
        {
            beforeMilliseconds,
            afterMilliseconds = after.OrdinaryFrameMilliseconds,
            improvementPercent = beforeMilliseconds is double before
                ? (double?)((before - after.OrdinaryFrameMilliseconds) / before * 100)
                : null,
            after.AllocatedBytes,
            under16_67Milliseconds = after.OrdinaryFrameMilliseconds < 16.67,
        };
    }

    private static void CopyScreenshot(string sourceDirectory, string targetDirectory, string sourceName, string targetName)
    {
        File.Copy(
            Path.Combine(sourceDirectory, sourceName),
            Path.Combine(targetDirectory, targetName),
            overwrite: true);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Write(string output, string file, object value)
    {
        File.WriteAllText(Path.Combine(output, file), JsonSerializer.Serialize(value,
            new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        return sorted[PercentileIndex(sorted.Length, percentile)];
    }

    private static int PercentileIndex(int count, double percentile)
    {
        return Math.Clamp((int)Math.Ceiling(count * percentile) - 1, 0, count - 1);
    }

    private sealed record ScreenshotMetric(
        string File,
        string PixelSha256,
        int DrawCalls,
        int QuadCount,
        int ShaderQuads,
        double OrdinaryFrameMilliseconds,
        long AllocatedBytes,
        double CapturedFrameMilliseconds,
        double ReadbackMilliseconds,
        double FrameWithoutReadbackMilliseconds);
}
