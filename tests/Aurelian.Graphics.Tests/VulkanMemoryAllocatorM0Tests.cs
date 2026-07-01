using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanMemoryAllocatorM0Tests
{
    [Fact]
    public void RawVulkanMemoryAllocator_Create_WhenVulkanUnavailable_SkipsCleanly()
    {
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            using var allocator = new RawVulkanMemoryAllocator(init.Plant!);
            Assert.Equal(PlantId.Zero, allocator.PlantId);
            Assert.Equal(VulkanAllocationBackendKind.RawVulkan, allocator.Telemetry.Backend);
        }
    }

    [Fact]
    public void RawVulkanMemoryAllocator_AllocateRejectsZeroSize()
    {
        WithAllocator(allocator =>
        {
            VulkanAllocationResult result = allocator.Allocate(new VulkanAllocationRequest(
                allocator.PlantId,
                0,
                uint.MaxValue,
                VulkanMemoryUsage.CpuToGpu,
                "zero-size"));

            Assert.False(result.Success);
            Assert.Equal(VulkanMemoryAllocatorStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, d => d.Code == VulkanMemoryAllocatorDiagnosticCodes.InvalidAllocationSize);
        });
    }

    [Fact]
    public void RawVulkanMemoryAllocator_AllocateRejectsZeroMemoryTypeBits()
    {
        WithAllocator(allocator =>
        {
            VulkanAllocationResult result = allocator.Allocate(new VulkanAllocationRequest(
                allocator.PlantId,
                4096,
                0,
                VulkanMemoryUsage.CpuToGpu,
                "zero-memory-type-bits"));

            Assert.False(result.Success);
            Assert.Equal(VulkanMemoryAllocatorStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, d => d.Code == VulkanMemoryAllocatorDiagnosticCodes.InvalidMemoryTypeBits);
        });
    }

    [Fact]
    public void RawVulkanMemoryAllocator_AllocateRejectsPlantMismatch()
    {
        WithAllocator(allocator =>
        {
            VulkanAllocationResult result = allocator.Allocate(new VulkanAllocationRequest(
                new PlantId(allocator.PlantId.Value + 1),
                4096,
                uint.MaxValue,
                VulkanMemoryUsage.CpuToGpu,
                "plant-mismatch"));

            Assert.False(result.Success);
            Assert.Equal(VulkanMemoryAllocatorStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, d => d.Code == VulkanMemoryAllocatorDiagnosticCodes.PlantMismatch);
        });
    }

    [Fact]
    public void RawVulkanMemoryAllocator_AllocateSmallCpuVisibleMemory_WhenPlantCreated_SucceedsOrReportsNoSuitableType()
    {
        WithAllocator(allocator =>
        {
            VulkanAllocationResult result = AllocateSmallCpuVisible(allocator);
            if (!result.Success)
            {
                Assert.Equal(VulkanMemoryAllocatorStatus.Rejected, result.Status);
                Assert.Contains(result.Diagnostics, d => d.Code == VulkanMemoryAllocatorDiagnosticCodes.NoSuitableMemoryType);
                return;
            }

            using VulkanMemoryAllocation allocation = result.Allocation!;
            Assert.Equal(allocator.PlantId, allocation.PlantId);
            Assert.Equal(VulkanAllocationBackendKind.RawVulkan, allocation.Backend);
            Assert.Equal(4096UL, allocation.SizeBytes);
            Assert.Equal(VulkanMemoryUsage.CpuToGpu, allocation.Usage);
            Assert.Equal(0UL, allocation.Offset);
            Assert.NotEqual(0UL, allocation.Memory.Handle);
        });
    }

    [Fact]
    public void RawVulkanMemoryAllocator_AllocationDispose_IsIdempotent()
    {
        WithAllocator(allocator =>
        {
            VulkanAllocationResult result = AllocateSmallCpuVisible(allocator);
            if (!result.Success)
            {
                Assert.Contains(result.Diagnostics, d => d.Code == VulkanMemoryAllocatorDiagnosticCodes.NoSuitableMemoryType);
                return;
            }

            VulkanMemoryAllocation allocation = result.Allocation!;
            allocation.Dispose();
            allocation.Dispose();

            Assert.Equal(1UL, allocator.Telemetry.FreeCount);
            Assert.Equal(0UL, allocator.Telemetry.LiveAllocationCount);
        });
    }

    [Fact]
    public void RawVulkanMemoryAllocator_TelemetryTracksAllocationAndFree()
    {
        WithAllocator(allocator =>
        {
            VulkanAllocationResult result = AllocateSmallCpuVisible(allocator);
            if (!result.Success)
            {
                Assert.Contains(result.Diagnostics, d => d.Code == VulkanMemoryAllocatorDiagnosticCodes.NoSuitableMemoryType);
                return;
            }

            VulkanMemoryAllocatorTelemetry allocated = allocator.Telemetry;
            Assert.Equal(1UL, allocated.AllocationCount);
            Assert.Equal(0UL, allocated.FreeCount);
            Assert.Equal(1UL, allocated.LiveAllocationCount);
            Assert.Equal(4096UL, allocated.RequestedBytes);
            Assert.Equal(4096UL, allocated.LiveBytes);
            Assert.Equal(4096UL, allocated.HighWaterLiveBytes);

            result.Allocation!.Dispose();

            VulkanMemoryAllocatorTelemetry freed = allocator.Telemetry;
            Assert.Equal(1UL, freed.AllocationCount);
            Assert.Equal(1UL, freed.FreeCount);
            Assert.Equal(0UL, freed.LiveAllocationCount);
            Assert.Equal(0UL, freed.LiveBytes);
            Assert.Equal(4096UL, freed.HighWaterLiveBytes);
        });
    }

    [Fact]
    public void RawVulkanMemoryAllocator_AllocateAfterDispose_ReturnsAllocatorDisposed()
    {
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            var allocator = new RawVulkanMemoryAllocator(init.Plant!);
            allocator.Dispose();

            VulkanAllocationResult result = allocator.Allocate(new VulkanAllocationRequest(
                PlantId.Zero,
                4096,
                uint.MaxValue,
                VulkanMemoryUsage.CpuToGpu,
                "after-dispose"));

            Assert.False(result.Success);
            Assert.Equal(VulkanMemoryAllocatorStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, d => d.Code == VulkanMemoryAllocatorDiagnosticCodes.AllocatorDisposed);
        }
    }

    [Fact]
    public void RawVulkanMemoryAllocator_PublicContractsStayAurelianOwned()
    {
        Type[] contractTypes =
        [
            typeof(IVulkanMemoryAllocator),
            typeof(VulkanAllocationRequest),
            typeof(VulkanAllocationResult),
            typeof(VulkanMemoryAllocation),
            typeof(VulkanMemoryAllocatorTelemetry),
        ];

        foreach (Type type in contractTypes)
        {
            Assert.StartsWith("Aurelian.Graphics.", type.FullName, StringComparison.Ordinal);
        }
    }

    private static VulkanAllocationResult AllocateSmallCpuVisible(RawVulkanMemoryAllocator allocator)
        => allocator.Allocate(new VulkanAllocationRequest(
            allocator.PlantId,
            4096,
            uint.MaxValue,
            VulkanMemoryUsage.CpuToGpu,
            "small-cpu-visible"));

    private static void WithAllocator(Action<RawVulkanMemoryAllocator> action)
    {
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            using var allocator = new RawVulkanMemoryAllocator(init.Plant!);
            action(allocator);
        }
    }
}
