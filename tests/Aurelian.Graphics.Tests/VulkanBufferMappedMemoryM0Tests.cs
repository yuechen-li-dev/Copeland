using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanBufferMappedMemoryM0Tests
{
    [Fact]
    public void VulkanBufferFactory_CreateMappedCpuToGpuBuffer_WhenPlantCreated_SucceedsOrCleanlyRejects()
        => WithPlantAndAllocator((plant, allocator) =>
        {
            VulkanBufferCreateResult result = CreateMappedCpuToGpuBuffer(plant, allocator);
            if (!result.Success)
            {
                Assert.NotEmpty(result.Diagnostics);
                Assert.All(result.Diagnostics, diagnostic => Assert.False(string.IsNullOrWhiteSpace(diagnostic.Code)));
                return;
            }

            using AurelianVulkanBuffer buffer = result.Buffer!;
            Assert.True(buffer.IsMapped);
            Assert.True(buffer.CanWrite);
            Assert.Equal(VulkanMemoryUsage.CpuToGpu, buffer.MemoryUsage);
        });

    [Fact]
    public void AurelianVulkanBuffer_WriteMappedBuffer_WritesBytesWithinBounds()
        => WithPlantAndAllocator((plant, allocator) =>
        {
            VulkanBufferCreateResult result = CreateMappedCpuToGpuBuffer(plant, allocator);
            if (!result.Success)
            {
                Assert.NotEmpty(result.Diagnostics);
                return;
            }

            using AurelianVulkanBuffer buffer = result.Buffer!;
            VulkanBufferWriteResult write = buffer.Write([1, 2, 3, 4], 8);

            Assert.True(write.Success);
            Assert.Equal(VulkanBufferWriteStatus.Written, write.Status);
            Assert.Empty(write.Diagnostics);
        });

    [Fact]
    public void AurelianVulkanBuffer_WriteRejectsUnmappedBuffer()
        => WithPlantAndAllocator((plant, allocator) =>
        {
            VulkanBufferCreateResult result = CreateUnmappedCpuToGpuBuffer(plant, allocator);
            if (!result.Success)
            {
                Assert.NotEmpty(result.Diagnostics);
                return;
            }

            using AurelianVulkanBuffer buffer = result.Buffer!;
            VulkanBufferWriteResult write = buffer.Write([1, 2, 3, 4]);

            Assert.False(write.Success);
            Assert.Equal(VulkanBufferWriteStatus.Rejected, write.Status);
            Assert.Contains(write.Diagnostics, diagnostic => diagnostic.Code == VulkanBufferWriteDiagnosticCodes.BufferNotMapped);
        });

    [Fact]
    public void AurelianVulkanBuffer_WriteRejectsOutOfBounds()
        => WithPlantAndAllocator((plant, allocator) =>
        {
            VulkanBufferCreateResult result = CreateMappedCpuToGpuBuffer(plant, allocator);
            if (!result.Success)
            {
                Assert.NotEmpty(result.Diagnostics);
                return;
            }

            using AurelianVulkanBuffer buffer = result.Buffer!;
            VulkanBufferWriteResult write = buffer.Write([1, 2, 3, 4], 62);

            Assert.False(write.Success);
            Assert.Equal(VulkanBufferWriteStatus.Rejected, write.Status);
            Assert.Contains(write.Diagnostics, diagnostic => diagnostic.Code == VulkanBufferWriteDiagnosticCodes.WriteOutOfBounds);
        });

    [Fact]
    public void AurelianVulkanBuffer_WriteAfterDispose_ReturnsDisposedDiagnostic()
        => WithPlantAndAllocator((plant, allocator) =>
        {
            VulkanBufferCreateResult result = CreateMappedCpuToGpuBuffer(plant, allocator);
            if (!result.Success)
            {
                Assert.NotEmpty(result.Diagnostics);
                return;
            }

            AurelianVulkanBuffer buffer = result.Buffer!;
            buffer.Dispose();
            VulkanBufferWriteResult write = buffer.Write([1]);

            Assert.False(write.Success);
            Assert.Equal(VulkanBufferWriteStatus.Rejected, write.Status);
            Assert.Contains(write.Diagnostics, diagnostic => diagnostic.Code == VulkanBufferWriteDiagnosticCodes.BufferDisposed);
        });

    [Fact]
    public void RawVulkanMemoryAllocator_MapOnCreateRejectsGpuOnlyUsage()
        => WithAllocator(allocator =>
        {
            VulkanAllocationResult result = allocator.Allocate(new VulkanAllocationRequest(
                allocator.PlantId,
                4096,
                uint.MaxValue,
                VulkanMemoryUsage.GpuOnly,
                "mapped-gpu-only",
                MapOnCreate: true));

            Assert.False(result.Success);
            Assert.Equal(VulkanMemoryAllocatorStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanMemoryAllocatorDiagnosticCodes.MappingNotSupportedForUsage);
        });

    [Fact]
    public void RawVulkanMemoryAllocator_MapOnCreateUpdatesTelemetryWhenMapped()
        => WithAllocator(allocator =>
        {
            VulkanAllocationResult result = AllocateMappedCpuVisible(allocator);
            if (!result.Success)
            {
                AssertCleanAllocationFailure(result);
                return;
            }

            using VulkanMemoryAllocation allocation = result.Allocation!;
            Assert.True(allocation.IsMapped);
            Assert.True(allocation.CanWrite);
            Assert.Equal(1UL, allocator.Telemetry.MappedAllocationCount);
            Assert.Equal(4096UL, allocator.Telemetry.MappedLiveBytes);
        });

    [Fact]
    public void VulkanMemoryAllocation_Dispose_UnmapsBeforeFreeing()
        => WithAllocator(allocator =>
        {
            VulkanAllocationResult result = AllocateMappedCpuVisible(allocator);
            if (!result.Success)
            {
                AssertCleanAllocationFailure(result);
                return;
            }

            VulkanMemoryAllocation allocation = result.Allocation!;
            allocation.Dispose();
            allocation.Dispose();

            Assert.False(allocation.IsMapped);
            Assert.Equal(1UL, allocator.Telemetry.FreeCount);
            Assert.Equal(0UL, allocator.Telemetry.LiveAllocationCount);
            Assert.Equal(0UL, allocator.Telemetry.MappedLiveBytes);
        });

    private static void AssertCleanAllocationFailure(VulkanAllocationResult result)
    {
        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.False(string.IsNullOrWhiteSpace(diagnostic.Code)));
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == VulkanMemoryAllocatorDiagnosticCodes.NoSuitableMemoryType
            || diagnostic.Code == VulkanMemoryAllocatorDiagnosticCodes.MapMemoryFailed);
    }

    private static VulkanBufferCreateResult CreateMappedCpuToGpuBuffer(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator)
        => VulkanBufferFactory.Create(
            plant,
            allocator,
            new VulkanBufferCreatePlan(
                plant.Context.Id,
                64,
                VulkanBufferUsage.Vertex | VulkanBufferUsage.TransferSource,
                VulkanMemoryUsage.CpuToGpu,
                "test.mapped-cpu-to-gpu",
                MapOnCreate: true));

    private static VulkanBufferCreateResult CreateUnmappedCpuToGpuBuffer(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator)
        => VulkanBufferFactory.Create(
            plant,
            allocator,
            new VulkanBufferCreatePlan(
                plant.Context.Id,
                64,
                VulkanBufferUsage.Vertex | VulkanBufferUsage.TransferSource,
                VulkanMemoryUsage.CpuToGpu,
                "test.unmapped-cpu-to-gpu"));

    private static VulkanAllocationResult AllocateMappedCpuVisible(RawVulkanMemoryAllocator allocator)
        => allocator.Allocate(new VulkanAllocationRequest(
            allocator.PlantId,
            4096,
            uint.MaxValue,
            VulkanMemoryUsage.CpuToGpu,
            "mapped-cpu-visible",
            MapOnCreate: true));

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

    private static void WithPlantAndAllocator(Action<AurelianVulkanPlant, RawVulkanMemoryAllocator> action)
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
            action(init.Plant!, allocator);
        }
    }
}
