using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Rendering.Contracts.Lighting;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

internal sealed unsafe partial class VulkanDynamicDiffuse3D
{
    private readonly AurelianVulkanBuffer[] regionParameters = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count];
    private readonly AurelianVulkanBuffer?[] regionReadback = new AurelianVulkanBuffer?[VulkanFrameInFlightSet.Count];
    private readonly PendingRegionReadback?[] pendingRegionReadback = new PendingRegionReadback?[VulkanFrameInFlightSet.Count];
    private readonly bool[] probeInitialized = new bool[512];
    private readonly int[] probeCursor = new int[512];
    private AurelianVulkanBuffer probeMetrics = null!;
    private DiffuseRegionPartition3D? regionPartition;
    private static long regionalSequence;
    public DiffuseRegionalObservation3D? CompletedRegions { get; private set; }

    private void CreateRegionalBuffers()
    {
        for (int slot = 0; slot < VulkanFrameInFlightSet.Count; slot++)
            regionParameters[slot] = Buffer(512 * 4 * 4, VulkanBufferUsage.Storage, VulkanMemoryUsage.CpuToGpu);
        probeMetrics = Buffer(512 * 8 * 4, VulkanBufferUsage.Storage | VulkanBufferUsage.TransferSource);
    }

    private void ResetRegionalState()
    {
        Array.Clear(probeInitialized);
        Array.Clear(probeCursor);
        Array.Clear(pendingRegionReadback);
        CompletedRegions = null;
    }

    private void PrepareRegions(DynamicDiffuseLighting3D settings)
    {
        // Prepare runs after frames.Begin has retired this slot; no extra wait or submission.
        ConsumeRegionalReadback();
        var plan = settings.RegionalPlan;
        if (plan is null)
        {
            if (regionPartition is not null) ResetRegionalState();
            regionPartition = null;
            return;
        }
        if (regionPartition != plan.Partition)
        {
            ResetRegionalState();
            regionPartition = plan.Partition;
        }
        regionReadback[frames.Slot] ??= Buffer(512 * 8 * 4, VulkanBufferUsage.TransferDestination, VulkanMemoryUsage.GpuToCpu);
        float[] words = new float[512 * 4];
        for (int probe = 0; probe < settings.Grid!.Count; probe++)
        {
            var command = plan.Regions[plan.Partition.RegionForProbe(settings.Grid, probe)];
            words[probe * 4] = command.Strategy == DiffuseRegionStrategy3D.Compiled ? 3 : 2;
            words[probe * 4 + 1] = command.RaysPerProbe;
            words[probe * 4 + 2] = probeCursor[probe];
            words[probe * 4 + 3] = probeInitialized[probe] ? 1 : 0;
        }
        Write(regionParameters[frames.Slot], words);
    }

    private void RecordRegionalReadback(VulkanCommandBufferLease command)
    {
        if (options.RegionalPlan is not { } plan) return;
        if (plan.Regions.All(region => region.Strategy == DiffuseRegionStrategy3D.Compiled))
        {
            Array.Clear(probeInitialized);
            return;
        }
        ulong bytes = (ulong)options.Grid!.Count * 8 * 4;
        Barrier(command, AccessFlags.ShaderWriteBit, AccessFlags.TransferReadBit);
        BufferCopy copy = new(0, 0, bytes);
        plant.Vk.CmdCopyBuffer(command.CommandBuffer, probeMetrics.NativeBuffer,
            regionReadback[frames.Slot]!.NativeBuffer, 1, &copy);
        MemoryBarrier host = new() { SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit, DstAccessMask = AccessFlags.HostReadBit };
        plant.Vk.CmdPipelineBarrier(command.CommandBuffer, PipelineStageFlags.TransferBit,
            PipelineStageFlags.HostBit, 0, 1, &host, 0, null, 0, null);
        pendingRegionReadback[frames.Slot] = new(Interlocked.Increment(ref regionalSequence), options.Grid, plan);
        for (int probe = 0; probe < options.Grid.Count; probe++)
        {
            var update = plan.Regions[plan.Partition.RegionForProbe(options.Grid, probe)];
            if (update.Strategy == DiffuseRegionStrategy3D.Compiled)
            {
                probeInitialized[probe] = false;
                continue;
            }
            probeInitialized[probe] = true;
            probeCursor[probe] = (probeCursor[probe] + update.RaysPerProbe) % options.Grid.RaysPerProbe;
        }
    }

    private void ConsumeRegionalReadback()
    {
        if (pendingRegionReadback[frames.Slot] is not { } pending) return;
        pendingRegionReadback[frames.Slot] = null;
        float[] data = MemoryMarshal.Cast<byte, float>(regionReadback[frames.Slot]!.ReadBytes(pending.Grid.Count * 8 * 4)).ToArray();
        double[] baselineDifference = new double[pending.Plan.Partition.Count];
        double[] temporalDifference = new double[baselineDifference.Length];
        double[] energy = new double[baselineDifference.Length];
        int[] probes = new int[baselineDifference.Length];
        bool[] baselineValidityChanged = new bool[baselineDifference.Length];
        bool[] temporalValidityChanged = new bool[baselineDifference.Length];
        for (int probe = 0; probe < pending.Grid.Count; probe++)
        {
            int region = pending.Plan.Partition.RegionForProbe(pending.Grid, probe);
            int offset = probe * 8;
            for (int field = 0; field < 8; field++)
                if (!float.IsFinite(data[offset + field]) || data[offset + field] < 0)
                    throw new InvalidOperationException("AUR-GI-REGION-006: GPU change telemetry is invalid.");
            if (data[offset + 4] != pending.Plan.Regions[region].RaysPerProbe || data[offset + 5] != 1)
                throw new InvalidOperationException("AUR-GI-REGION-006: GPU telemetry does not match submitted work.");
            baselineDifference[region] += data[offset];
            temporalDifference[region] += data[offset + 1];
            energy[region] += Math.Max(data[offset + 2], data[offset + 3]);
            probes[region]++;
            baselineValidityChanged[region] |= data[offset + 6] > .5f;
            temporalValidityChanged[region] |= data[offset + 7] > .5f;
        }
        var measured = ImmutableArray.CreateBuilder<DiffuseRegionMeasurement3D>();
        for (int region = 0; region < probes.Length; region++)
        {
            var submitted = pending.Plan.Regions[region];
            double normalizer = Math.Max(energy[region], probes[region] * pending.Grid.RaysPerProbe * 3 * .0001);
            // Temporal sums cover only the updated subset. Normalize to that sample count.
            double samplingScale = submitted.RaysPerProbe > 0 ? (double)pending.Grid.RaysPerProbe / submitted.RaysPerProbe : 0;
            measured.Add(new(region, submitted.Epoch, submitted.Strategy, probes[region], submitted.RaysPerProbe,
                Math.Max(baselineValidityChanged[region] ? 1 : 0, Math.Sqrt(baselineDifference[region] / normalizer)),
                Math.Max(temporalValidityChanged[region] ? 1 : 0, Math.Sqrt(temporalDifference[region] * samplingScale / normalizer))));
        }
        CompletedRegions = new(pending.Sequence, pending.Grid, pending.Plan.Partition, measured.ToImmutable(), pending.Plan.PolicyIdentity);
    }

    private sealed record PendingRegionReadback(long Sequence, DiffuseProbeGrid Grid, DiffuseRegionalPlan3D Plan);
}
