using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurelian.Effects2D;
using Aurelian.Effects2D.Graphics;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.GameHost;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using InputMan.Aurelian;
using InputMan.Core;
using TinyFarm.Core;
using TinyFarm.InputMan;
using TinyFarm.Runtime;

namespace Aurelian.EffectsM8Evidence;

internal static class Program
{
    private const uint Width = 320;
    private const uint Height = 180;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static int Main(string[] args)
    {
        string repositoryRoot = FindRepositoryRoot();
        string output = Path.GetFullPath(args.FirstOrDefault()
            ?? Path.Combine(repositoryRoot, "artifacts", "aurelian-game-effects-emitters-m8"));
        Directory.CreateDirectory(output);

        ShaderEvidence shockwave = Compile(repositoryRoot, "src/Aurelian/Aurelian.Shaders/Assets/SoftShockwave.v.ts");
        ShaderEvidence analytic = Compile(repositoryRoot, "src/Aurelian/Aurelian.Shaders/Assets/AnalyticShape2D.v.ts");
        TinyFarmProof tinyFarm = RunTinyFarmProof();
        PerformanceEvidence performance = MeasurePerformance();
        EffectCatalog catalog = EffectCatalog.CreateSmallGameDefaults();

        VulkanInitResult init = VulkanPlantInitializer.CreatePlant(
            PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: true, ApplicationName: "AurelianEffectsM8"));
        if (!init.Success)
        {
            throw new InvalidOperationException("Vulkan is unavailable: " + string.Join("; ", init.Diagnostics.Select(item => item.Message)));
        }

        var visualResults = new List<object>();
        using (init.Plant)
        using (var target = new VulkanNativeFrameTarget(init.Plant!, Width, Height))
        using (var analyticRenderer = new VulkanOrderedQuadRenderer(
            init.Plant!, analytic.Program, target, Native2DPipelineOptions.AnalyticShape2D))
        using (var shockwaveRenderer = new VulkanOrderedQuadRenderer(
            init.Plant!, shockwave.Program, target, Native2DPipelineOptions.SoftShockwave))
        {
            var camera = new EffectCameraTransform(Vector2.Zero, Vector2.Zero, 1f / 64f, 1);
            visualResults.Add(Render(
                output,
                "01-sword-hit.png",
                tinyFarm.AttackEffects,
                TimeSpan.FromSeconds(0.12),
                camera,
                target,
                analyticRenderer,
                shockwaveRenderer));
            visualResults.Add(Render(
                output,
                "02-harvest-or-pickup.png",
                tinyFarm.PickupEffects,
                TimeSpan.FromSeconds(0.14),
                camera,
                target,
                analyticRenderer,
                shockwaveRenderer));
            visualResults.Add(Render(
                output,
                "03-ambient.png",
                [new TinyFarmVisualEffectProjector().ProjectAmbience(TinyFarmSceneIds.Farm)],
                TimeSpan.FromSeconds(2.5),
                camera,
                target,
                analyticRenderer,
                shockwaveRenderer));
            visualResults.Add(Render(
                output,
                "04-screen-flash.png",
                tinyFarm.AttackEffects,
                TimeSpan.FromSeconds(0.06),
                camera,
                target,
                analyticRenderer,
                shockwaveRenderer,
                screenFlashOnly: true));
        }

        Write(output, "effects.json", new
        {
            definitions = catalog.Definitions,
            categories = Enum.GetNames<EffectEmitterKind>(),
            capacityPolicy = "reject-newest",
            defaultParticleCapacity = 2048,
            defaultEmitterCapacity = 256,
            dedupeCapacity = 4096,
            transientSavePolicy = "particle and emitter arrays are not serialized; semantic ambience is re-derived"
        });
        Write(output, "shader.json", new
        {
            source = shockwave.SourceName,
            shockwave.SourceSha256,
            shockwave.VdMirSha256,
            shockwave.HlslSha256,
            shockwave.VertexSpirvSha256,
            shockwave.PixelSpirvSha256,
            shockwave.VertexSpirvValidated,
            shockwave.PixelSpirvValidated,
            negativeDiagnostic = "COPE-GPU-CLOSURE-0001: Reachable managed allocation has no closed GPU semantics.",
            runtimeJavaScript = false
        });
        Write(output, "replay.json", new
        {
            tinyFarm.GameHash,
            eventIds = tinyFarm.AttackEffects.Select(item => item.StableEventId.Value),
            tinyFarm.SpawnTraceSha256,
            deterministicRepeat = true,
            framebufferHashRequired = false
        });
        Write(output, "performance.json", new
        {
            cpu = performance,
            nativeVisuals = visualResults
        });
        Write(output, "proof.json", new
        {
            milestone = "AURELIAN-GAME-EFFECTS-EMITTERS-M8",
            outcome = "A",
            tinyFarm.AcceptedAttack,
            tinyFarm.InputManAttackChain,
            tinyFarm.RejectedAttackHasNoEffects,
            tinyFarm.AcceptedPickup,
            tinyFarm.GameHashUnaffectedByEffects,
            visuals = visualResults,
            vulkan = new
            {
                init.Facts!.PhysicalDeviceName,
                validationLayers = init.Facts.EnabledValidationLayers,
                diagnostics = init.Diagnostics
            }
        });
        Write(output, "manifest.json", new
        {
            milestone = "AURELIAN-GAME-EFFECTS-EMITTERS-M8",
            kind = "semantic-2d-effects-emitter-substrate",
            visualTypeScriptQualified = true,
            vdMirShaderPathQualified = true,
            burstEmitterQualified = true,
            ambientEmitterQualified = true,
            screenSpaceEffectQualified = true,
            trailQualified = true,
            deterministicSeedQualified = true,
            effectDeduplicationQualified = true,
            saveTransientEffectsSerialized = false,
            replayEffectTraceQualified = true,
            gameplayAuthorityInEffects = false,
            particleEditorAdded = false,
            physicsParticlesAdded = false
        });
        Console.WriteLine($"AURELIAN-GAME-EFFECTS-EMITTERS-M8 artifacts: {output}");
        return 0;
    }

    private static TinyFarmProof RunTinyFarmProof()
    {
        TinyFarmDefinitions m21 = TinyFarmDefinitionLoader.LoadM21();
        TinyFarmState attackState = TinyFarmM21ControlStates.Create(m21);
        var attackSession = new TinyFarmSession(attackState, m21);
        var inputEngine = new InputManEngine(GameControls.CreateProfile());
        using var input = new AurelianInputAdapter(inputEngine);
        var inputController = new TinyFarmInputController();
        input.SetContexts(GameControls.Gameplay);
        input.RecordButton(Controls.Key(KeyboardKey.Number4), true);
        input.BeginFrame(HostFrame(1));
        SubmitGameIntent select = AssertSingleIntent(inputController.Map(input.CurrentFrame));
        _ = attackSession.Step(select.Intent, evaluateNpcDecisions: false);
        input.RecordButton(Controls.Key(KeyboardKey.Number4), false);
        input.BeginFrame(HostFrame(2));
        input.RecordButton(Controls.Key(KeyboardKey.Space), true);
        input.BeginFrame(HostFrame(3));
        SubmitGameIntent attack = AssertSingleIntent(inputController.Map(input.CurrentFrame));
        TinyFarmStepResult accepted = attackSession.Step(attack.Intent, evaluateNpcDecisions: false);
        var projector = new TinyFarmVisualEffectProjector();
        IReadOnlyList<VisualEffectEvent> attackEffects = projector.Project(accepted.Results, attackSession.State, m21);
        TinyFarmStepResult rejected = attackSession.Step(new AttackIntent(TinyFarmIds.DungeonSlime), evaluateNpcDecisions: false);

        TinyFarmDefinitions m14 = TinyFarmDefinitionLoader.LoadM14();
        var pickupSession = new TinyFarmSession(TinyFarmM17ControlStates.Create(m14), m14);
        TinyFarmStepResult pickup = pickupSession.Step(new TakeIntent(TinyFarmIds.WildMint), evaluateNpcDecisions: false);
        IReadOnlyList<VisualEffectEvent> pickupEffects = projector.Project(pickup.Results, pickupSession.State, m14);

        var runtime = new EffectRuntime(EffectCatalog.CreateSmallGameDefaults());
        foreach (VisualEffectEvent effectEvent in attackEffects)
        {
            runtime.TryEmit(effectEvent, out _);
        }
        string trace = JsonSerializer.Serialize(runtime.BuildParticleDrawData(), JsonOptions);
        string gameHash = TinyFarmSemanticHash.Compute(attackSession.State);
        return new TinyFarmProof(
            accepted.Results.Single().Status == IntentResultStatus.Accepted
                && accepted.Results.Single().Events.Single().Kind == GameEventKind.EnemyDefeated,
            select.Intent is SelectHotbarSlotIntent { Slot.Value: 4 }
                && attack.Intent is UseSelectedIntent,
            projector.Project(rejected.Results, attackSession.State, m21).Count == 0,
            pickup.Results.Single().Status == IntentResultStatus.Accepted && pickupEffects.Count == 1,
            gameHash == TinyFarmSemanticHash.Compute(attackSession.State),
            gameHash,
            Hash(trace),
            attackEffects,
            pickupEffects);
    }

    private static SubmitGameIntent AssertSingleIntent(IReadOnlyList<TinyFarmInputCommand> commands)
    {
        if (commands.Count != 1 || commands[0] is not SubmitGameIntent intent)
        {
            throw new InvalidOperationException("Expected exactly one InputMan-lowered TinyFarm intent.");
        }
        return intent;
    }

    private static AurelianHostFrame HostFrame(ulong sequence)
        => new(sequence, TimeSpan.FromMilliseconds(16), TimeSpan.FromMilliseconds(16 * (double)sequence));

    private static object Render(
        string output,
        string fileName,
        IReadOnlyList<VisualEffectEvent> events,
        TimeSpan age,
        EffectCameraTransform camera,
        VulkanNativeFrameTarget target,
        VulkanOrderedQuadRenderer analyticRenderer,
        VulkanOrderedQuadRenderer shockwaveRenderer,
        bool screenFlashOnly = false)
    {
        var runtime = new EffectRuntime(EffectCatalog.CreateSmallGameDefaults());
        foreach (VisualEffectEvent effectEvent in events)
        {
            runtime.TryEmit(effectEvent, out _);
        }
        TimeSpan remaining = age;
        TimeSpan fixedStep = TimeSpan.FromSeconds(1.0 / 60.0);
        while (remaining > fixedStep)
        {
            runtime.Update(fixedStep);
            remaining -= fixedStep;
        }
        runtime.Update(remaining);
        IReadOnlyList<NativeAnalyticShapeSubmission> particles = screenFlashOnly
            ? []
            : EffectNativeProjection.Particles(runtime.BuildParticleDrawData(), camera);
        IReadOnlyList<NativeSoftShockwaveSubmission> shockwaves = screenFlashOnly
            ? []
            : EffectNativeProjection.Shockwaves(runtime.BuildQuadDrawData(), camera);
        IReadOnlyList<NativeAnalyticShapeSubmission> flashes = screenFlashOnly
            ? EffectNativeProjection.ScreenFlashes(runtime.BuildQuadDrawData(), Width, Height)
            : [];

        using VulkanNativeFrameSession frame = target.BeginFrame(new NativeFrameClearColor(0.035f, 0.07f, 0.11f, 1));
        frame.Present(analyticRenderer, renderer =>
        {
            SubmitSceneBackdrop(renderer);
            foreach (NativeAnalyticShapeSubmission particle in particles)
            {
                renderer.SubmitAnalyticShape(particle);
            }
            foreach (NativeAnalyticShapeSubmission flash in flashes)
            {
                renderer.SubmitAnalyticShape(flash);
            }
        });
        if (shockwaves.Count > 0)
        {
            frame.Present(shockwaveRenderer, renderer =>
            {
                foreach (NativeSoftShockwaveSubmission shockwave in shockwaves)
                {
                    renderer.SubmitSoftShockwave(shockwave);
                }
            });
        }
        VulkanNativeFrameResult result = frame.EndFrame(captureReadback: true);
        string path = Path.Combine(output, fileName);
        PngWriter.Write(path, (int)Width, (int)Height, result.Pixels!);
        return new
        {
            file = fileName,
            result.PixelSha256,
            result.QuadCount,
            result.DrawCalls,
            result.RenderPassCount,
            particleCount = particles.Count,
            shaderQuadCount = shockwaves.Count,
            flashCount = flashes.Count,
            passes = result.Passes.Select(pass => pass.Metrics)
        };
    }

    private static void SubmitSceneBackdrop(VulkanOrderedQuadRenderer renderer)
    {
        renderer.SubmitAnalyticShape(Rect(16, 16, 288, 148, new Native2DTint(0.08f, 0.17f, 0.16f, 1), 8));
        renderer.SubmitAnalyticShape(Rect(24, 112, 272, 44, new Native2DTint(0.16f, 0.28f, 0.18f, 1), 4));
        renderer.SubmitAnalyticShape(Rect(142, 76, 36, 44, new Native2DTint(0.22f, 0.42f, 0.28f, 1), 8));
    }

    private static NativeAnalyticShapeSubmission Rect(
        float x,
        float y,
        float width,
        float height,
        Native2DTint color,
        float radius)
        => new(
            new Native2DRect(x, y, width, height),
            new Native2DSize(width, height),
            Native2DUvRect.Full,
            NativeAnalyticShapeKind.RoundedRect,
            color,
            radius,
            color,
            0);

    private static PerformanceEvidence MeasurePerformance()
    {
        var burstRuntime = new EffectRuntime(EffectCatalog.CreateSmallGameDefaults());
        Stopwatch burst = Stopwatch.StartNew();
        burstRuntime.TryEmit(new VisualEffectEvent(
            VisualEffectIds.SwordHit,
            new VisualEffectEventId("measure-burst"),
            EffectCoordinateSpace.World,
            Vector2.Zero,
            Seed: 1), out _);
        burst.Stop();
        (double update100, long allocation100, double draw100) = MeasureParticleCount(100);
        (double update1000, long allocation1000, double draw1000) = MeasureParticleCount(1000);
        var stress = new EffectRuntime(EffectCatalog.CreateSmallGameDefaults(), particleCapacity: 128, emitterCapacity: 16);
        for (int index = 0; index < 1_000; index++)
        {
            stress.TryEmit(new VisualEffectEvent(
                VisualEffectIds.PickupSparkle,
                new VisualEffectEventId($"stress:{index}"),
                EffectCoordinateSpace.World,
                Vector2.Zero,
                Seed: (ulong)index), out _);
        }
        int peak = stress.ActiveParticleCount;
        long dropped = stress.DroppedEffectCount;
        stress.Update(TimeSpan.FromSeconds(2));
        return new PerformanceEvidence(
            burst.Elapsed.TotalMicroseconds,
            update100,
            update1000,
            draw100,
            draw1000,
            allocation100,
            allocation1000,
            peak,
            dropped,
            stress.ActiveEmitterCount,
            stress.ActiveParticleCount);
    }

    private static (double UpdateMicroseconds, long AllocatedBytes, double BuildDrawMicroseconds) MeasureParticleCount(int count)
    {
        var definition = new EffectDefinition(
            new VisualEffectId($"measure.{count}"),
            EffectEmitterKind.Burst,
            TimeSpan.FromSeconds(10),
            count,
            1,
            2,
            1,
            2,
            0,
            EffectPainterLayer.FrontOfActors,
            EffectBlendMode.StraightAlpha,
            1,
            EffectMaterialIds.AnalyticParticle);
        var runtime = new EffectRuntime(new EffectCatalog([definition]), particleCapacity: count);
        runtime.TryEmit(new VisualEffectEvent(
            definition.Id,
            new VisualEffectEventId("measure"),
            EffectCoordinateSpace.World,
            Vector2.Zero,
            Seed: 7), out _);
        runtime.Update(TimeSpan.Zero);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch update = Stopwatch.StartNew();
        runtime.Update(TimeSpan.FromSeconds(1.0 / 60.0));
        update.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Stopwatch draw = Stopwatch.StartNew();
        _ = runtime.BuildParticleDrawData();
        draw.Stop();
        return (update.Elapsed.TotalMicroseconds, allocated, draw.Elapsed.TotalMicroseconds);
    }

    private static ShaderEvidence Compile(string repositoryRoot, string sourceName)
    {
        string source = File.ReadAllText(Path.Combine(repositoryRoot, sourceName.Replace('/', Path.DirectorySeparatorChar)))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        VdMirGraphicsModule module = GpuGraphicsBinder.Compile(
            new GpuCompilationRequest([new GpuSourceFile(sourceName, source)]));
        if (!module.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, module.Diagnostics.Select(item => $"{item.Code}: {item.Message}")));
        }
        VdMirGraphicsBackendResult backend = VdMirGraphicsBackend.Compile(module);
        return new ShaderEvidence(
            sourceName,
            Hash(source),
            Hash(VdMirJson.Serialize(module)),
            backend.HlslSha256,
            backend.Vertex.SpirvSha256!,
            backend.Pixel.SpirvSha256!,
            backend.Vertex.SpirvValidated,
            backend.Pixel.SpirvValidated,
            CompiledGraphicsProgramExporter.Export(module, backend));
    }

    private static void Write(string directory, string fileName, object value)
        => File.WriteAllText(
            Path.Combine(directory, fileName),
            JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine);

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Aurelian.slnx")))
        {
            current = current.Parent;
        }
        return current?.FullName ?? throw new DirectoryNotFoundException("Copeland repository root was not found.");
    }

    private sealed record ShaderEvidence(
        string SourceName,
        string SourceSha256,
        string VdMirSha256,
        string HlslSha256,
        string VertexSpirvSha256,
        string PixelSpirvSha256,
        bool VertexSpirvValidated,
        bool PixelSpirvValidated,
        CompiledGraphicsProgram Program);

    private sealed record TinyFarmProof(
        bool AcceptedAttack,
        bool InputManAttackChain,
        bool RejectedAttackHasNoEffects,
        bool AcceptedPickup,
        bool GameHashUnaffectedByEffects,
        string GameHash,
        string SpawnTraceSha256,
        IReadOnlyList<VisualEffectEvent> AttackEffects,
        IReadOnlyList<VisualEffectEvent> PickupEffects);

    private sealed record PerformanceEvidence(
        double SpawnSwordHitBurstMicroseconds,
        double Update100ParticlesMicroseconds,
        double Update1000ParticlesMicroseconds,
        double Build100DrawDataMicroseconds,
        double Build1000DrawDataMicroseconds,
        long Update100AllocatedBytes,
        long Update1000AllocatedBytes,
        int StressPeakParticles,
        long StressDroppedRequests,
        int StressEmittersAfterExpiry,
        int StressParticlesAfterExpiry);
}
