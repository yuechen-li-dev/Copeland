using System.Diagnostics;
using System.Numerics;
using Aurelian.Graphics.FramePlans;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Sync;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Models;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

public sealed unsafe partial class VulkanSolid3DRenderer
{
    private readonly FramePlanOptions planOptions;
    private readonly Dictionary<Native3DFeatures, FramePlan> framePlanCache = [];
    private readonly Dictionary<string, NativeFrameInstruction[][]> batchTapeCache = [];
    private VulkanFenceBundle? computeFences;
    private VulkanCommandBufferPool? computeCommands;
    private VulkanCommandSubmitter? computeSubmitter;
    private Vulkan3DGpuTimings? computeTimings;
    private readonly bool[] computeRecorded = new bool[VulkanFrameInFlightSet.Count];
    public NativeQueueOverlap? LastQueueOverlap { get; private set; }
    public FramePlan? CurrentFramePlan { get; private set; }
    public ulong TransientTextureBytes => surfaceLighting?.TransientTextureBytes ?? 0;
    public int TransientPhysicalImages => surfaceLighting?.TransientPhysicalImages ?? 0;
    public ClusterVisibilityMode VisibilityMode { get; set; } = ClusterVisibilityMode.ClusterAndCone;
    public bool EnableBindlessMaterials { get; set; } = true;
    public bool EnableVisibilityBuffer { get; set; }
    public LocalLightSampling3D LocalLightSampling { get; set; } = new();
    public void ResetLightSamplingHistory() => surfaceLighting?.ResetLightSampling();
    public float[] ReadLightSamplesForQualification() => surfaceLighting!.ReadSamplesForQualification();
    public ReferenceImage3D ReadLightingForQualification() => surfaceLighting!.ReadLightingForQualification();
    public bool EnableRayShadows { get; set; }
    public Aurelian.Graphics.Vulkan.RayQueries.SceneAccelerationStatistics3D? RaySceneStatistics => surfaceLighting?.RayStatistics;
    public bool EnableHierarchicalZ { get; set; }
    /// <summary>Prepared LOD error estimate in pixels. Zero forces LOD0. No runtime simplification.</summary>
    public float LodPixelError { get; set; } = .25f;
    public HzbPyramidReadback ReadHzbForQualification() => retainedModels!.ReadHzbForQualification();
    public NativeVisibilityBufferReadback ReadIdentitiesForQualification()
        => retainedModels?.ReadIdentitiesForQualification() ?? throw new InvalidOperationException("Retained renderer was not configured.");
    public IReadOnlyList<ClusterVisibilityReadback> ReadVisibilityForQualification()
        => retainedModels?.ReadVisibilityForQualification() ?? [];

    /// <summary>Installs a verified artifact for a matching native feature profile.</summary>
    public void InstallFramePlan(string json)
    {
        FramePlan plan = FramePlan.Load(json);
        var match = framePlanCache.SingleOrDefault(entry => entry.Value.Hash == plan.Hash);
        if (match.Value is null)
        {
            throw new FramePlanException("AUR-PLAN-010", "Artifact does not match an admitted native frame profile. Render that profile before installing it.");
        }
        framePlanCache[match.Key] = plan;
        if (CurrentFramePlan?.Hash == plan.Hash) CurrentFramePlan = plan;
    }

    private FramePlan GetFramePlan(FrameState state)
    {
        bool shaded = state.Settings.SurfaceDebugView == SurfaceDebugView3D.Shaded;
        var features = new Native3DFeatures(surfaceOnly, outputPass is not null, motionOutput, shadowPass is not null,
            !planOptions.OptimizeInactivePasses || state.Settings.AmbientOcclusionStrength > 0,
            !planOptions.OptimizeInactivePasses || state.Settings.LocalLightCulling && LocalLights.Count > 0,
            state.UseTemporal, state.Settings.BloomIntensity > 0, shaded && state.HasSubsurface,
            shaded && state.Settings.Fog.Density > 0, state.Settings.Volumetrics.Enabled,
            shaded && state.HasRefraction, shaded && state.HasTransparency, shaded && state.HasParticles,
            planOptions.AsyncParticleUpdates && plant.ComputeQueue.HasValue, state.UseTemporal && state.GpuGeometry is not null,
            surfaceLighting?.ShadowCount ?? 0, planOptions.AliasTransientTextures, retainedModels?.HasVisibility == true,
            retainedModels?.VisibilityBufferActive == true, retainedModels?.HierarchicalZActive == true, surfaceLighting?.RayShadowsActive == true);
        if (!framePlanCache.TryGetValue(features, out var plan))
        {
            plan = Native3DFrameGraph.Compile(features, (int)target.Width, (int)target.Height);
            framePlanCache.Add(features, plan);
        }
        CurrentFramePlan = plan;
        return plan;
    }

    private (long RecordedAt, long SubmittedAt, ulong Fence) ExecutePlan(FramePlan plan, FrameState state, bool capture)
    {
        bool asynchronous = plan.Batches.Any(batch => batch.Queue == FrameQueue.Compute);
        if (asynchronous && computeFences is null)
        {
            computeFences = VulkanFenceBundle.Create(plant);
            computeCommands = VulkanCommandBufferPool.Create(plant);
            computeSubmitter = new(plant, computeCommands, computeFences, plant.ComputeQueue);
            computeTimings = new(plant, ["particles-update"], frames);
        }
        computeRecorded[frames.Slot] = asynchronous;
        if (asynchronous)
        {
            Require(computeFences!.CommandListFence.QueryCompletedValue().Success, "Compute retirement query failed.");
        }
        if (!batchTapeCache.TryGetValue(plan.Hash, out var batchTapes))
        {
            batchTapes = plan.Batches.Select(batch =>
            {
                var passes = batch.Passes.ToHashSet();
                return LowerBatch(plan, passes);
            }).ToArray();
            batchTapeCache.Add(plan.Hash, batchTapes);
        }
        var commands = new VulkanCommandBufferLease[plan.Batches.Length];
        int firstGraphics = plan.Batches.First(batch => batch.Queue == FrameQueue.Graphics).Id;
        int lastGraphics = plan.Batches.Last(batch => batch.Queue == FrameQueue.Graphics).Id;
        uint writtenTimings = 0;
        // Record everything before submitting, so CPU recording cannot consume the overlap window.
        foreach (FrameBatch batch in plan.Batches)
        {
            bool compute = batch.Queue == FrameQueue.Compute;
            var pool = compute ? computeCommands! : commandPool;
            var fence = compute ? computeFences!.CommandListFence : fences.CommandListFence;
            var command = pool.Rent(fence.LastKnownCompletedValue);
            commands[batch.Id] = command;
            Require(command.Begin().Success, "3D command begin failed.");
            if (batch.Id == firstGraphics)
            {
                RecordMemoryBarrier(command, AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
                    AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit);
                gpuTimings?.Reset(command);
                gpuTimings?.BeginFrame(command, frameSequence);
            }
            if (compute)
            {
                computeTimings!.Reset(command);
                computeTimings.BeginFrame(command, frameSequence);
            }
            foreach (NativeFrameInstruction instruction in batchTapes[batch.Id])
            {
                if (instruction.IsBarrier)
                {
                    RecordMemoryBarrier(command, instruction.Before, instruction.After);
                    continue;
                }
                var implementation = instruction.Pass;
                if (compute)
                {
                    computeTimings!.Mark(command, 0);
                    ParticleSystem!.Record(command);
                    computeTimings.Mark(command, 1);
                }
                else
                {
                    RecordPass(implementation, command, state);
                    int timing = TimingIndex(implementation);
                    if (timing >= 0) writtenTimings |= 1u << timing;
                }
            }
            if (compute) computeTimings!.EndFrame(command);
            if (batch.Id == lastGraphics)
            {
                // Keep the timing schema complete without writing a query twice.
                int timingCount = gpuTimings?.PassCount ?? 0;
                for (int index = 0; index < timingCount; index++)
                {
                    if ((writtenTimings & (1u << index)) == 0)
                    {
                        gpuTimings?.Mark(command, (uint)index * 2);
                        gpuTimings?.Mark(command, (uint)index * 2 + 1);
                    }
                }
                gpuTimings?.EndFrame(command);
            }
            Require(command.End().Success, "3D command end failed.");
        }
        long recordedAt = Stopwatch.GetTimestamp();
        var signals = new ulong[plan.Batches.Length];
        foreach (FrameBatch batch in plan.Batches)
        {
            bool compute = batch.Queue == FrameQueue.Compute;
            var waits = batch.WaitFor.Select(parent => new VulkanTimelineWait(
                plan.Batches[parent].Queue == FrameQueue.Compute ? computeFences!.CommandListFence : fences.CommandListFence,
                signals[parent])).ToList();
            // The shared particle vertex buffer also belongs to the preceding frame's draw.
            if (compute && frames.LastSubmitted > 0) waits.Add(new(fences.CommandListFence, frames.LastSubmitted));
            bool wait = batch.Id == lastGraphics && (capture || FrameMode == Native3DFrameMode.Capture);
            var result = (compute ? computeSubmitter! : submitter).Submit(new(commands[batch.Id],
                WaitForCompletion: wait, TimeoutNanoseconds: 5_000_000_000, DebugName: "solid3d.frame-plan")
            {
                TimelineWaits = waits,
            });
            Require(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
            signals[batch.Id] = result.SignalFenceValue!.Value;
        }
        return (recordedAt, Stopwatch.GetTimestamp(), signals[lastGraphics]);
    }

    private static NativeFrameInstruction[] LowerBatch(FramePlan plan, HashSet<int> passes)
    {
        var tape = new List<NativeFrameInstruction>();
        AccessFlags before = 0;
        AccessFlags after = 0;
        foreach (FrameInstruction instruction in plan.Tape.Where(instruction => passes.Contains(instruction.Pass)))
        {
            if (instruction.Op == FrameTapeOp.Barrier)
            {
                before |= AccessMask(instruction.Before);
                after |= AccessMask(instruction.After);
            }
            else
            {
                // Resource hazards at one boundary share a single native memory dependency.
                if (before != 0) tape.Add(new(true, default, before, after));
                tape.Add(new(false, (Native3DPass)plan.Graph.Passes[instruction.Pass].Implementation, 0, 0));
                before = 0;
                after = 0;
            }
        }
        return tape.ToArray();
    }

    private readonly record struct NativeFrameInstruction(bool IsBarrier, Native3DPass Pass, AccessFlags Before, AccessFlags After);

    private void ReadQueueEvidence()
    {
        if (!computeRecorded[frames.Slot]) return;
        var compute = computeTimings!.ReadInterval(0);
        var shadow = gpuTimings!.ReadInterval(0);
        if (compute is null || shadow is null) return;
        ulong start = Math.Max(compute.StartTicks, shadow.StartTicks);
        ulong end = Math.Min(compute.EndTicks, shadow.EndTicks);
        double overlap = end > start ? (end - start) * compute.NanosecondsPerTick / 1_000_000d : 0;
        LastQueueOverlap = new(compute.Sequence,
            (compute.EndTicks - compute.StartTicks) * compute.NanosecondsPerTick / 1_000_000d,
            (shadow.EndTicks - shadow.StartTicks) * shadow.NanosecondsPerTick / 1_000_000d, overlap);
    }

    private void RecordPass(Native3DPass pass, VulkanCommandBufferLease command, FrameState state)
    {
        switch (pass)
        {
            case Native3DPass.Uploads: surfaceLighting!.RecordUploads(command); break;
            case Native3DPass.Particles:
                gpuTimings?.Mark(command, 26);
                ParticleSystem!.Record(command);
                gpuTimings?.Mark(command, 27);
                break;
            case Native3DPass.DirectionalShadows: RecordDirectionalShadows(command, state); break;
            case Native3DPass.RayShadows:
                gpuTimings?.Mark(command, 16);
                surfaceLighting!.RecordRayShadows(command);
                gpuTimings?.Mark(command, 17);
                break;
            case Native3DPass.LocalShadows: RecordLocalShadows(command, state); break;
            case Native3DPass.Geometry: RecordGeometry(command, state); break;
            case Native3DPass.IdentityRaster:
                gpuTimings?.Mark(command, 30);
                retainedModels!.RecordIdentityRaster(command);
                gpuTimings?.Mark(command, 31);
                break;
            case Native3DPass.HzbSeedRaster:
                gpuTimings?.Mark(command, 32);
                retainedModels!.RecordIdentityRaster(command);
                gpuTimings?.Mark(command, 33);
                break;
            case Native3DPass.HzbSeedPyramid:
                gpuTimings?.Mark(command, 34);
                retainedModels!.RecordHzbPyramid(command, history: false);
                gpuTimings?.Mark(command, 35);
                break;
            case Native3DPass.HzbFinalCull:
                gpuTimings?.Mark(command, 36);
                retainedModels!.RecordHzbFinalCull(command, state.WorldToClip, state.Eye, VisibilityMode);
                gpuTimings?.Mark(command, 37);
                break;
            case Native3DPass.HzbHistoryPyramid:
                gpuTimings?.Mark(command, 38);
                retainedModels!.RecordHzbPyramid(command, history: true);
                gpuTimings?.Mark(command, 39);
                break;
            case Native3DPass.AmbientOcclusion: RecordSurfaceStage(command, state, pass, 10); break;
            case Native3DPass.LightTiles: RecordSurfaceStage(command, state, pass, 12); break;
            case Native3DPass.SurfaceResolve: RecordSurfaceStage(command, state, pass, 14); break;
            case Native3DPass.Subsurface: RecordSurfaceStage(command, state, pass, 18); break;
            case Native3DPass.Atmosphere: RecordSurfaceStage(command, state, pass, 20); break;
            case Native3DPass.Refraction: RecordRefraction(command, state); break;
            case Native3DPass.Transparency: RecordTransparency(command, state); break;
            case Native3DPass.Temporal: RecordTemporal(command, state); break;
            case Native3DPass.Bloom: RecordBloom(command, state); break;
            case Native3DPass.Output: RecordOutput(command, state); break;
            case Native3DPass.HistoryCopy: CopyPreviousGpu(command, state.GpuGeometry!); break;
            case Native3DPass.Visibility:
                gpuTimings?.Mark(command, 28);
                retainedModels!.RecordVisibility(command, state.WorldToClip, state.Eye, VisibilityMode, LodPixelError);
                gpuTimings?.Mark(command, 29);
                break;
            default: throw new FramePlanException("AUR-PLAN-010", "Unknown native pass implementation.");
        }
    }

    private void RecordSurfaceStage(VulkanCommandBufferLease command, FrameState state, Native3DPass pass, uint query)
    {
        gpuTimings?.Mark(command, query);
        switch (pass)
        {
            case Native3DPass.AmbientOcclusion: surfaceLighting!.RecordAmbientOcclusion(command, outputVertices!); break;
            case Native3DPass.LightTiles: surfaceLighting!.RecordLightTiles(command, outputVertices!); break;
            case Native3DPass.SurfaceResolve: surfaceLighting!.RecordSurfaceResolve(command, outputVertices!); break;
            case Native3DPass.Subsurface: surfaceLighting!.RecordSubsurface(command, outputVertices!); break;
            case Native3DPass.Atmosphere: surfaceLighting!.RecordAtmosphere(command, outputVertices!); break;
        }
        state.Resolved = surfaceLighting!.Resolved;
        gpuTimings?.Mark(command, query + 1);
    }

    private static int TimingIndex(Native3DPass pass) => pass switch
    {
        Native3DPass.DirectionalShadows => 0, Native3DPass.Geometry => 1, Native3DPass.Temporal => 2,
        Native3DPass.Bloom => 3, Native3DPass.Output => 4, Native3DPass.AmbientOcclusion => 5,
        Native3DPass.LightTiles => 6, Native3DPass.SurfaceResolve => 7, Native3DPass.LocalShadows => 8, Native3DPass.RayShadows => 8,
        Native3DPass.Subsurface => 9, Native3DPass.Atmosphere => 10, Native3DPass.Transparency => 11,
        Native3DPass.Refraction => 12, Native3DPass.Particles => 13, Native3DPass.Visibility => 14,
        Native3DPass.IdentityRaster => 15, Native3DPass.HzbSeedRaster => 16, Native3DPass.HzbSeedPyramid => 17,
        Native3DPass.HzbFinalCull => 18, Native3DPass.HzbHistoryPyramid => 19, _ => -1,
    };

    private void RecordMemoryBarrier(VulkanCommandBufferLease command, AccessFlags before, AccessFlags after)
    {
        if (plant.Facts.Synchronization2Enabled)
        {
            MemoryBarrier2 memory = new()
            {
                SType = StructureType.MemoryBarrier2,
                SrcStageMask = PipelineStageFlags2.AllCommandsBit,
                DstStageMask = PipelineStageFlags2.AllCommandsBit,
                SrcAccessMask = (AccessFlags2)(ulong)before,
                DstAccessMask = (AccessFlags2)(ulong)after,
            };
            DependencyInfo dependency = new() { SType = StructureType.DependencyInfo, MemoryBarrierCount = 1, PMemoryBarriers = &memory };
            plant.Vk.CmdPipelineBarrier2(command.CommandBuffer, &dependency);
            return;
        }
        MemoryBarrier barrier = new() { SType = StructureType.MemoryBarrier, SrcAccessMask = before, DstAccessMask = after };
        plant.Vk.CmdPipelineBarrier(command.CommandBuffer, PipelineStageFlags.AllCommandsBit,
            PipelineStageFlags.AllCommandsBit, 0, 1, &barrier, 0, null, 0, null);
    }

    private static AccessFlags AccessMask(FrameAccess access)
    {
        AccessFlags mask = 0;
        if ((access & FrameAccess.Sample) != 0) mask |= AccessFlags.ShaderReadBit;
        if ((access & FrameAccess.Vertex) != 0) mask |= AccessFlags.VertexAttributeReadBit;
        if ((access & FrameAccess.Indirect) != 0) mask |= AccessFlags.IndirectCommandReadBit;
        if ((access & FrameAccess.ColorWrite) != 0) mask |= AccessFlags.ColorAttachmentWriteBit;
        if ((access & FrameAccess.DepthWrite) != 0) mask |= AccessFlags.DepthStencilAttachmentWriteBit;
        if ((access & FrameAccess.ComputeWrite) != 0) mask |= AccessFlags.ShaderWriteBit;
        if ((access & FrameAccess.TransferRead) != 0) mask |= AccessFlags.TransferReadBit;
        if ((access & FrameAccess.TransferWrite) != 0) mask |= AccessFlags.TransferWriteBit;
        if ((access & FrameAccess.HistoryReadWrite) != 0)
            mask |= AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit | AccessFlags.ColorAttachmentWriteBit;
        return mask;
    }

    private sealed class FrameState
    {
        public int VertexCount;
        public IReadOnlyList<NativeModel3DBatch> Models = [];
        public IReadOnlyList<SceneInstance3D> Instances = [];
        public Matrix4x4 WorldToClip, LogicalCamera, ShadowCamera;
        public Vector3 Eye;
        public NativeFrameClearColor Clear;
        public Graphics3DSettings Settings = null!;
        public NativeGpuGeometry3D? GpuGeometry;
        public DirectionalShadowCascades3D? Cascades;
        public float[] Lighting = [];
        public bool UseTemporal, UseDiffuse, HasParticles, HasTransparency, HasRefraction, HasSubsurface, UseWaterReflection;
        public WaterFrame3D WaterFrame = null!;
        public (string? Identity, int Count, ModelMaterial Material)[] ModelKeys = [];
        public (string Identity, NativeMesh3D Mesh, ModelMaterial Material)[] InstanceKeys = [];
        public AurelianVulkanTexture? Resolved, Glow;
    }
}

public sealed record NativeQueueOverlap(long Sequence, double ComputeMilliseconds, double ShadowMilliseconds,
    double OverlapMilliseconds);
