using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Silk.NET.Vulkan;
using NativeBuffer = Silk.NET.Vulkan.Buffer;

namespace Aurelian.Graphics.Vulkan.Resources.Buffers;

public static unsafe class VulkanBufferFactory
{
    public static VulkanBufferCreateResult Create(
        AurelianVulkanPlant plant,
        IVulkanMemoryAllocator allocator,
        VulkanBufferCreatePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plant);
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(plan);

        List<VulkanBufferDiagnostic> diagnostics = [];
        Validate(plant.Context.Id, allocator.PlantId, plan, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == VulkanBufferDiagnosticSeverity.Error))
        {
            return new VulkanBufferCreateResult(VulkanBufferStatus.Rejected, null, diagnostics);
        }

        Vk vk = plant.Vk;
        Silk.NET.Vulkan.Device device = plant.Device;
        NativeBuffer buffer = default;
        VulkanMemoryAllocation? allocation = null;

        try
        {
            BufferUsageFlags nativeUsage = MapUsage(plan.Usage);
            BufferCreateInfo createInfo = new()
            {
                SType = StructureType.BufferCreateInfo,
                Size = plan.SizeBytes,
                Usage = nativeUsage,
                SharingMode = SharingMode.Exclusive,
            };

            Result createResult = vk.CreateBuffer(device, &createInfo, (AllocationCallbacks*)null, out buffer);
            if (createResult != Result.Success)
            {
                diagnostics.Add(Diagnostic(
                    VulkanBufferDiagnosticCodes.BufferCreationFailed,
                    VulkanBufferDiagnosticSeverity.Error,
                    $"vkCreateBuffer failed with result {createResult}.",
                    plan));
                return new VulkanBufferCreateResult(VulkanBufferStatus.Failed, null, diagnostics);
            }

            vk.GetBufferMemoryRequirements(device, buffer, out MemoryRequirements requirements);
            if (requirements.Size == 0 || requirements.MemoryTypeBits == 0)
            {
                DestroyBuffer(vk, device, ref buffer);
                diagnostics.Add(Diagnostic(
                    VulkanBufferDiagnosticCodes.MemoryRequirementsFailed,
                    VulkanBufferDiagnosticSeverity.Error,
                    "Vulkan returned empty memory requirements for the buffer.",
                    plan));
                return new VulkanBufferCreateResult(VulkanBufferStatus.Failed, null, diagnostics);
            }

            VulkanAllocationResult allocationResult = allocator.Allocate(new VulkanAllocationRequest(
                plan.PlantId,
                requirements.Size,
                requirements.MemoryTypeBits,
                plan.MemoryUsage,
                plan.DebugName,
                plan.MapOnCreate));

            if (!allocationResult.Success)
            {
                DestroyBuffer(vk, device, ref buffer);
                diagnostics.Add(Diagnostic(
                    VulkanBufferDiagnosticCodes.AllocationFailed,
                    allocationResult.Status == VulkanMemoryAllocatorStatus.Failed
                        ? VulkanBufferDiagnosticSeverity.Error
                        : VulkanBufferDiagnosticSeverity.Error,
                    "Buffer memory allocation failed through IVulkanMemoryAllocator.",
                    plan));
                diagnostics.AddRange(allocationResult.Diagnostics.Select(diagnostic => new VulkanBufferDiagnostic(
                    diagnostic.Code,
                    MapSeverity(diagnostic.Severity),
                    diagnostic.Message,
                    diagnostic.PlantId,
                    diagnostic.DebugName)));
                return new VulkanBufferCreateResult(
                    allocationResult.Status == VulkanMemoryAllocatorStatus.Failed ? VulkanBufferStatus.Failed : VulkanBufferStatus.Rejected,
                    null,
                    diagnostics);
            }

            allocation = allocationResult.Allocation!;
            Result bindResult = vk.BindBufferMemory(device, buffer, allocation.Memory, allocation.Offset);
            if (bindResult != Result.Success)
            {
                allocation.Dispose();
                DestroyBuffer(vk, device, ref buffer);
                diagnostics.Add(Diagnostic(
                    VulkanBufferDiagnosticCodes.BindMemoryFailed,
                    VulkanBufferDiagnosticSeverity.Error,
                    $"vkBindBufferMemory failed with result {bindResult}.",
                    plan));
                return new VulkanBufferCreateResult(VulkanBufferStatus.Failed, null, diagnostics);
            }

            AurelianVulkanBuffer aurelianBuffer = new(
                vk,
                device,
                buffer,
                allocation,
                plan.PlantId,
                plan.SizeBytes,
                plan.Usage,
                plan.MemoryUsage);

            return new VulkanBufferCreateResult(VulkanBufferStatus.Created, aurelianBuffer, diagnostics);
        }
        catch (Exception ex)
        {
            allocation?.Dispose();
            DestroyBuffer(vk, device, ref buffer);
            diagnostics.Add(Diagnostic(
                VulkanBufferDiagnosticCodes.BufferCreationFailed,
                VulkanBufferDiagnosticSeverity.Error,
                $"Unexpected Vulkan buffer creation failure: {ex.GetType().Name}: {ex.Message}",
                plan));
            return new VulkanBufferCreateResult(VulkanBufferStatus.Failed, null, diagnostics);
        }
    }

    private static void Validate(
        PlantId plantId,
        PlantId allocatorPlantId,
        VulkanBufferCreatePlan plan,
        List<VulkanBufferDiagnostic> diagnostics)
    {
        if (plan.SizeBytes == 0)
        {
            diagnostics.Add(Diagnostic(
                VulkanBufferDiagnosticCodes.InvalidBufferSize,
                VulkanBufferDiagnosticSeverity.Error,
                "Buffer size must be greater than zero bytes.",
                plan));
        }

        if (plan.Usage == VulkanBufferUsage.None)
        {
            diagnostics.Add(Diagnostic(
                VulkanBufferDiagnosticCodes.InvalidBufferUsage,
                VulkanBufferDiagnosticSeverity.Error,
                "Buffer usage must include at least one usage flag.",
                plan));
        }

        if (plan.MemoryUsage == VulkanMemoryUsage.Unknown)
        {
            diagnostics.Add(Diagnostic(
                VulkanBufferDiagnosticCodes.InvalidMemoryUsage,
                VulkanBufferDiagnosticSeverity.Error,
                "Buffer memory usage must not be Unknown.",
                plan));
        }

        if (plan.PlantId != plantId || allocatorPlantId != plantId)
        {
            diagnostics.Add(Diagnostic(
                VulkanBufferDiagnosticCodes.PlantMismatch,
                VulkanBufferDiagnosticSeverity.Error,
                "Buffer plan plant, plant context, and allocator plant must match.",
                plan));
        }
    }

    private static BufferUsageFlags MapUsage(VulkanBufferUsage usage)
    {
        BufferUsageFlags flags = 0;
        if ((usage & VulkanBufferUsage.Vertex) != 0)
        {
            flags |= BufferUsageFlags.VertexBufferBit;
        }

        if ((usage & VulkanBufferUsage.Index) != 0)
        {
            flags |= BufferUsageFlags.IndexBufferBit;
        }

        if ((usage & VulkanBufferUsage.Uniform) != 0)
        {
            flags |= BufferUsageFlags.UniformBufferBit;
        }

        if ((usage & VulkanBufferUsage.Storage) != 0)
        {
            flags |= BufferUsageFlags.StorageBufferBit;
        }

        if ((usage & VulkanBufferUsage.TransferSource) != 0)
        {
            flags |= BufferUsageFlags.TransferSrcBit;
        }

        if ((usage & VulkanBufferUsage.TransferDestination) != 0)
        {
            flags |= BufferUsageFlags.TransferDstBit;
        }

        return flags;
    }

    private static VulkanBufferDiagnosticSeverity MapSeverity(VulkanMemoryAllocatorDiagnosticSeverity severity)
        => severity switch
        {
            VulkanMemoryAllocatorDiagnosticSeverity.Warning => VulkanBufferDiagnosticSeverity.Warning,
            VulkanMemoryAllocatorDiagnosticSeverity.Info => VulkanBufferDiagnosticSeverity.Info,
            _ => VulkanBufferDiagnosticSeverity.Error,
        };

    private static VulkanBufferDiagnostic Diagnostic(
        string code,
        VulkanBufferDiagnosticSeverity severity,
        string message,
        VulkanBufferCreatePlan plan)
        => new(code, severity, message, plan.PlantId, plan.DebugName);

    private static void DestroyBuffer(Vk vk, Silk.NET.Vulkan.Device device, ref NativeBuffer buffer)
    {
        if (buffer.Handle != 0 && device.Handle != 0)
        {
            vk.DestroyBuffer(device, buffer, (AllocationCallbacks*)null);
            buffer = default;
        }
    }
}
