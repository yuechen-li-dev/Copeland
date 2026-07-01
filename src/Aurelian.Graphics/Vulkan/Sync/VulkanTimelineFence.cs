using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Silk.NET.Vulkan;
using VulkanSemaphore = Silk.NET.Vulkan.Semaphore;

namespace Aurelian.Graphics.Vulkan.Sync;

public sealed unsafe class VulkanTimelineFence : IDisposable
{
    private readonly object stateLock = new();
    private readonly Vk vk;
    private readonly Silk.NET.Vulkan.Device device;
    private VulkanSemaphore semaphore;
    private ulong nextValue = 1;
    private ulong lastKnownCompletedValue;
    private bool disposed;

    private VulkanTimelineFence(
        PlantId plantId,
        string name,
        Vk vk,
        Silk.NET.Vulkan.Device device,
        VulkanSemaphore semaphore)
    {
        PlantId = plantId;
        Name = name;
        this.vk = vk;
        this.device = device;
        this.semaphore = semaphore;
    }

    public PlantId PlantId { get; }

    public string Name { get; }

    public VulkanSemaphore Semaphore
    {
        get
        {
            lock (stateLock)
            {
                return semaphore;
            }
        }
    }

    public ulong NextValue
    {
        get
        {
            lock (stateLock)
            {
                return nextValue;
            }
        }
    }

    public ulong LastKnownCompletedValue
    {
        get
        {
            lock (stateLock)
            {
                return lastKnownCompletedValue;
            }
        }
    }

    public static VulkanTimelineFence Create(AurelianVulkanPlant plant, string name)
    {
        ArgumentNullException.ThrowIfNull(plant);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        SemaphoreTypeCreateInfo timelineInfo = new()
        {
            SType = StructureType.SemaphoreTypeCreateInfo,
            SemaphoreType = SemaphoreType.Timeline,
            InitialValue = 0,
        };
        SemaphoreCreateInfo createInfo = new()
        {
            SType = StructureType.SemaphoreCreateInfo,
            PNext = &timelineInfo,
        };

        Result result = plant.Vk.CreateSemaphore(plant.Device, &createInfo, null, out VulkanSemaphore semaphore);
        if (result != Result.Success)
        {
            throw new InvalidOperationException(
                $"Failed to create Vulkan timeline semaphore fence '{name}' for plant {plant.Context.Id}: {result} ({VulkanFenceDiagnosticCodes.TimelineSemaphoreCreationFailed}).");
        }

        return new VulkanTimelineFence(plant.Context.Id, name, plant.Vk, plant.Device, semaphore);
    }

    public ulong AllocateSignalValue()
    {
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return nextValue++;
        }
    }

    public VulkanFenceOperationResult QueryCompletedValue()
    {
        VulkanSemaphore localSemaphore;
        lock (stateLock)
        {
            if (disposed)
            {
                return DisposedResult(VulkanFenceDiagnosticCodes.FenceDisposed, "Cannot query a disposed Vulkan timeline fence.");
            }

            localSemaphore = semaphore;
        }

        ulong completedValue = 0;
        Result result = vk.GetSemaphoreCounterValue(device, localSemaphore, &completedValue);
        if (result != Result.Success)
        {
            return VulkanFenceOperationResult.Failed(
                LastKnownCompletedValue,
                new VulkanFenceDiagnostic(
                    VulkanFenceDiagnosticCodes.TimelineSemaphoreQueryFailed,
                    VulkanFenceDiagnosticSeverity.Error,
                    $"Vulkan timeline semaphore query failed with result {result}.",
                    PlantId,
                    Name));
        }

        lock (stateLock)
        {
            if (completedValue > lastKnownCompletedValue)
            {
                lastKnownCompletedValue = completedValue;
            }

            return VulkanFenceOperationResult.Succeeded(lastKnownCompletedValue);
        }
    }

    public VulkanFenceOperationResult WaitForValue(ulong value, ulong timeoutNanoseconds)
    {
        if (value == 0)
        {
            return VulkanFenceOperationResult.Failed(
                LastKnownCompletedValue,
                new VulkanFenceDiagnostic(
                    VulkanFenceDiagnosticCodes.InvalidFenceValue,
                    VulkanFenceDiagnosticSeverity.Error,
                    "Fence value 0 is the initial timeline value and is not a valid allocated signal value.",
                    PlantId,
                    Name));
        }

        VulkanSemaphore localSemaphore;
        lock (stateLock)
        {
            if (disposed)
            {
                return DisposedResult(VulkanFenceDiagnosticCodes.FenceDisposed, "Cannot wait on a disposed Vulkan timeline fence.");
            }

            if (value <= lastKnownCompletedValue)
            {
                return VulkanFenceOperationResult.Succeeded(lastKnownCompletedValue);
            }

            localSemaphore = semaphore;
        }

        SemaphoreWaitInfo waitInfo = new()
        {
            SType = StructureType.SemaphoreWaitInfo,
            SemaphoreCount = 1,
            PSemaphores = &localSemaphore,
            PValues = &value,
        };

        Result result = vk.WaitSemaphores(device, &waitInfo, timeoutNanoseconds);
        if (result != Result.Success)
        {
            return VulkanFenceOperationResult.Failed(
                LastKnownCompletedValue,
                new VulkanFenceDiagnostic(
                    VulkanFenceDiagnosticCodes.TimelineSemaphoreWaitFailed,
                    VulkanFenceDiagnosticSeverity.Error,
                    $"Vulkan timeline semaphore wait for value {value} failed with result {result}.",
                    PlantId,
                    Name));
        }

        lock (stateLock)
        {
            if (value > lastKnownCompletedValue)
            {
                lastKnownCompletedValue = value;
            }

            return VulkanFenceOperationResult.Succeeded(lastKnownCompletedValue);
        }
    }

    public void Dispose()
    {
        VulkanSemaphore semaphoreToDestroy = default;
        lock (stateLock)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            semaphoreToDestroy = semaphore;
            semaphore = default;
        }

        if (semaphoreToDestroy.Handle != 0)
        {
            vk.DestroySemaphore(device, semaphoreToDestroy, null);
        }
    }

    private VulkanFenceOperationResult DisposedResult(string code, string message)
        => VulkanFenceOperationResult.Failed(
            LastKnownCompletedValue,
            new VulkanFenceDiagnostic(code, VulkanFenceDiagnosticSeverity.Error, message, PlantId, Name));
}
