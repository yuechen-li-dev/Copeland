using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanBufferM0Tests
{
    [Fact]
    public void VulkanBufferFactory_Create_WhenVulkanUnavailable_SkipsCleanly()
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
            VulkanBufferCreateResult result = CreateSmallVertexBuffer(init.Plant!, allocator);
            if (!result.Success)
            {
                Assert.NotEmpty(result.Diagnostics);
                return;
            }

            using AurelianVulkanBuffer buffer = result.Buffer!;
            Assert.Equal(PlantId.Zero, buffer.PlantId);
        }
    }

    [Fact]
    public void VulkanBufferFactory_Create_RejectsZeroSize()
        => WithPlantAndAllocator((plant, allocator) =>
        {
            VulkanBufferCreateResult result = VulkanBufferFactory.Create(
                plant,
                allocator,
                new VulkanBufferCreatePlan(PlantId.Zero, 0, VulkanBufferUsage.Vertex, VulkanMemoryUsage.CpuToGpu, "zero-size"));

            Assert.False(result.Success);
            Assert.Equal(VulkanBufferStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, d => d.Code == VulkanBufferDiagnosticCodes.InvalidBufferSize);
        });

    [Fact]
    public void VulkanBufferFactory_Create_RejectsNoUsage()
        => WithPlantAndAllocator((plant, allocator) =>
        {
            VulkanBufferCreateResult result = VulkanBufferFactory.Create(
                plant,
                allocator,
                new VulkanBufferCreatePlan(PlantId.Zero, 4096, VulkanBufferUsage.None, VulkanMemoryUsage.CpuToGpu, "no-usage"));

            Assert.False(result.Success);
            Assert.Equal(VulkanBufferStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, d => d.Code == VulkanBufferDiagnosticCodes.InvalidBufferUsage);
        });

    [Fact]
    public void VulkanBufferFactory_Create_RejectsUnknownMemoryUsage()
        => WithPlantAndAllocator((plant, allocator) =>
        {
            VulkanBufferCreateResult result = VulkanBufferFactory.Create(
                plant,
                allocator,
                new VulkanBufferCreatePlan(PlantId.Zero, 4096, VulkanBufferUsage.Vertex, VulkanMemoryUsage.Unknown, "unknown-memory"));

            Assert.False(result.Success);
            Assert.Equal(VulkanBufferStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, d => d.Code == VulkanBufferDiagnosticCodes.InvalidMemoryUsage);
        });

    [Fact]
    public void VulkanBufferFactory_Create_RejectsPlantMismatch()
        => WithPlantAndAllocator((plant, allocator) =>
        {
            VulkanBufferCreateResult result = VulkanBufferFactory.Create(
                plant,
                allocator,
                new VulkanBufferCreatePlan(new PlantId(plant.Context.Id.Value + 1), 4096, VulkanBufferUsage.Vertex, VulkanMemoryUsage.CpuToGpu, "plant-mismatch"));

            Assert.False(result.Success);
            Assert.Equal(VulkanBufferStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, d => d.Code == VulkanBufferDiagnosticCodes.PlantMismatch);
        });

    [Fact]
    public void VulkanBufferFactory_CreateSmallCpuVisibleVertexBuffer_WhenPlantCreated_SucceedsOrReportsCleanFailure()
        => WithPlantAndAllocator((plant, allocator) =>
        {
            VulkanBufferCreateResult result = CreateSmallVertexBuffer(plant, allocator);
            if (!result.Success)
            {
                Assert.NotEmpty(result.Diagnostics);
                Assert.All(result.Diagnostics, diagnostic => Assert.False(string.IsNullOrWhiteSpace(diagnostic.Code)));
                return;
            }

            using AurelianVulkanBuffer buffer = result.Buffer!;
            Assert.Equal(plant.Context.Id, buffer.PlantId);
            Assert.Equal(4096UL, buffer.SizeBytes);
            Assert.Equal(VulkanBufferUsage.Vertex | VulkanBufferUsage.TransferDestination, buffer.Usage);
            Assert.Equal(VulkanMemoryUsage.CpuToGpu, buffer.MemoryUsage);
            Assert.Equal(plant.Context.Id, buffer.ResourceState.PlantId);
            Assert.Equal(4096UL, buffer.ResourceState.SizeBytes);
            Assert.Equal(VulkanAllocationBackendKind.RawVulkan, buffer.ResourceState.AllocationBackend);
        });

    [Fact]
    public void AurelianVulkanBuffer_Dispose_IsIdempotent()
        => WithPlantAndAllocator((plant, allocator) =>
        {
            VulkanBufferCreateResult result = CreateSmallVertexBuffer(plant, allocator);
            if (!result.Success)
            {
                Assert.NotEmpty(result.Diagnostics);
                return;
            }

            AurelianVulkanBuffer buffer = result.Buffer!;
            buffer.Dispose();
            buffer.Dispose();

            Assert.True(buffer.IsDisposed);
            Assert.Equal(1UL, allocator.Telemetry.FreeCount);
            Assert.Equal(0UL, allocator.Telemetry.LiveAllocationCount);
        });

    [Fact]
    public void VulkanBufferFactory_UsesAllocatorBoundary_NotRawMemoryCalls()
    {
        string buffersRoot = Path.Combine(FindRepositoryRoot(), "src", "Aurelian.Graphics", "Vulkan", "Resources", "Buffers");
        string[] sourceFiles = Directory.GetFiles(buffersRoot, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(sourceFiles);

        foreach (string file in sourceFiles)
        {
            string text = File.ReadAllText(file);
            Assert.DoesNotContain("AllocateMemory", text, StringComparison.Ordinal);
            Assert.DoesNotContain("FreeMemory", text, StringComparison.Ordinal);
            Assert.DoesNotContain("vkAllocateMemory", text, StringComparison.Ordinal);
            Assert.DoesNotContain("vkFreeMemory", text, StringComparison.Ordinal);
        }
    }

    private static VulkanBufferCreateResult CreateSmallVertexBuffer(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator)
        => VulkanBufferFactory.Create(
            plant,
            allocator,
            new VulkanBufferCreatePlan(
                plant.Context.Id,
                4096,
                VulkanBufferUsage.Vertex | VulkanBufferUsage.TransferDestination,
                VulkanMemoryUsage.CpuToGpu,
                "test.vertex"));

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

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Aurelian.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root from the test output directory.");
    }
}
