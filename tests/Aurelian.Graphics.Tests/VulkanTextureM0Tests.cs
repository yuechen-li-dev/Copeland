using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanTextureM0Tests
{
    [Fact]
    public void VulkanTextureFactory_Create_WhenVulkanUnavailable_SkipsCleanly()
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
            VulkanTextureCreateResult result = CreateSmallRgbaTexture(init.Plant!, allocator);
            if (!result.Success)
            {
                Assert.NotEmpty(result.Diagnostics);
                return;
            }

            using AurelianVulkanTexture texture = result.Texture!;
            Assert.Equal(PlantId.Zero, texture.PlantId);
        }
    }

    [Fact]
    public void VulkanTextureFactory_Create_RejectsZeroDimensions()
        => WithPlantAndAllocator((plant, allocator) =>
        {
            VulkanTextureCreateResult result = VulkanTextureFactory.Create(
                plant,
                allocator,
                ValidPlan(plant) with { Width = 0, DebugName = "zero-width" });

            Assert.False(result.Success);
            Assert.Equal(VulkanTextureStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, d => d.Code == VulkanTextureDiagnosticCodes.InvalidDimensions);
        });

    [Fact]
    public void VulkanTextureFactory_Create_RejectsZeroMipLevels()
        => WithPlantAndAllocator((plant, allocator) =>
        {
            VulkanTextureCreateResult result = VulkanTextureFactory.Create(
                plant,
                allocator,
                ValidPlan(plant) with { MipLevels = 0, DebugName = "zero-mips" });

            Assert.False(result.Success);
            Assert.Equal(VulkanTextureStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, d => d.Code == VulkanTextureDiagnosticCodes.InvalidMipLevels);
        });

    [Fact]
    public void VulkanTextureFactory_Create_RejectsZeroArrayLayers()
        => WithPlantAndAllocator((plant, allocator) =>
        {
            VulkanTextureCreateResult result = VulkanTextureFactory.Create(
                plant,
                allocator,
                ValidPlan(plant) with { ArrayLayers = 0, DebugName = "zero-layers" });

            Assert.False(result.Success);
            Assert.Equal(VulkanTextureStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, d => d.Code == VulkanTextureDiagnosticCodes.InvalidArrayLayers);
        });

    [Fact]
    public void VulkanTextureFactory_Create_RejectsNoUsage()
        => WithPlantAndAllocator((plant, allocator) =>
        {
            VulkanTextureCreateResult result = VulkanTextureFactory.Create(
                plant,
                allocator,
                ValidPlan(plant) with { Usage = VulkanTextureUsage.None, DebugName = "no-usage" });

            Assert.False(result.Success);
            Assert.Equal(VulkanTextureStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, d => d.Code == VulkanTextureDiagnosticCodes.InvalidTextureUsage);
        });

    [Fact]
    public void VulkanTextureFactory_Create_RejectsUnknownMemoryUsage()
        => WithPlantAndAllocator((plant, allocator) =>
        {
            VulkanTextureCreateResult result = VulkanTextureFactory.Create(
                plant,
                allocator,
                ValidPlan(plant) with { MemoryUsage = VulkanMemoryUsage.Unknown, DebugName = "unknown-memory" });

            Assert.False(result.Success);
            Assert.Equal(VulkanTextureStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, d => d.Code == VulkanTextureDiagnosticCodes.InvalidMemoryUsage);
        });

    [Fact]
    public void VulkanTextureFactory_Create_RejectsPlantMismatch()
        => WithPlantAndAllocator((plant, allocator) =>
        {
            VulkanTextureCreateResult result = VulkanTextureFactory.Create(
                plant,
                allocator,
                ValidPlan(plant) with { PlantId = new PlantId(plant.Context.Id.Value + 1), DebugName = "plant-mismatch" });

            Assert.False(result.Success);
            Assert.Equal(VulkanTextureStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, d => d.Code == VulkanTextureDiagnosticCodes.PlantMismatch);
        });

    [Fact]
    public void VulkanTextureFactory_Create_RejectsNonUndefinedInitialLayout()
        => WithPlantAndAllocator((plant, allocator) =>
        {
            VulkanTextureCreateResult result = VulkanTextureFactory.Create(
                plant,
                allocator,
                ValidPlan(plant) with { InitialLayout = VulkanResourceLayout.ShaderResourceFragment, DebugName = "non-undefined" });

            Assert.False(result.Success);
            Assert.Equal(VulkanTextureStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, d => d.Code == VulkanTextureDiagnosticCodes.UnsupportedInitialLayout);
        });

    [Fact]
    public void VulkanTextureFactory_CreateSmallRgbaTexture_WhenPlantCreated_SucceedsOrReportsCleanFailure()
        => WithPlantAndAllocator((plant, allocator) =>
        {
            VulkanTextureCreateResult result = CreateSmallRgbaTexture(plant, allocator);
            if (!result.Success)
            {
                Assert.NotEmpty(result.Diagnostics);
                Assert.All(result.Diagnostics, diagnostic => Assert.False(string.IsNullOrWhiteSpace(diagnostic.Code)));
                return;
            }

            using AurelianVulkanTexture texture = result.Texture!;
            Assert.Equal(plant.Context.Id, texture.PlantId);
            Assert.Equal(4U, texture.Width);
            Assert.Equal(4U, texture.Height);
            Assert.Equal(1U, texture.MipLevels);
            Assert.Equal(1U, texture.ArrayLayers);
            Assert.Equal(VulkanTextureFormat.Rgba8Unorm, texture.Format);
            Assert.Equal(VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferDestination, texture.Usage);
            Assert.Equal(VulkanResourceLayout.Undefined, texture.InitialLayout);
            Assert.Equal(plant.Context.Id, texture.ResourceState.PlantId);
            Assert.True(texture.ResourceState.SizeBytes > 0);
            Assert.Equal(VulkanAllocationBackendKind.RawVulkan, texture.ResourceState.AllocationBackend);
        });

    [Fact]
    public void AurelianVulkanTexture_Dispose_IsIdempotent()
        => WithPlantAndAllocator((plant, allocator) =>
        {
            VulkanTextureCreateResult result = CreateSmallRgbaTexture(plant, allocator);
            if (!result.Success)
            {
                Assert.NotEmpty(result.Diagnostics);
                return;
            }

            AurelianVulkanTexture texture = result.Texture!;
            texture.Dispose();
            texture.Dispose();

            Assert.True(texture.IsDisposed);
            Assert.Equal(1UL, allocator.Telemetry.FreeCount);
            Assert.Equal(0UL, allocator.Telemetry.LiveAllocationCount);
        });

    [Fact]
    public void AurelianVulkanTexture_InitializesLayoutTrackerForAllSubresources()
        => WithPlantAndAllocator((plant, allocator) =>
        {
            VulkanTextureCreateResult result = VulkanTextureFactory.Create(
                plant,
                allocator,
                ValidPlan(plant) with { MipLevels = 2, ArrayLayers = 3, DebugName = "tracker" });
            if (!result.Success)
            {
                Assert.NotEmpty(result.Diagnostics);
                return;
            }

            using AurelianVulkanTexture texture = result.Texture!;
            Assert.Equal(2U, texture.LayoutTracker.MipLevels);
            Assert.Equal(3U, texture.LayoutTracker.ArrayLayers);
            for (uint mip = 0; mip < texture.MipLevels; mip++)
            {
                for (uint layer = 0; layer < texture.ArrayLayers; layer++)
                {
                    Assert.Equal(VulkanResourceLayout.Undefined, texture.LayoutTracker.Get(mip, layer));
                }
            }
        });

    [Fact]
    public void VulkanTextureFactory_UsesAllocatorBoundary_NotRawMemoryCalls()
    {
        string texturesRoot = Path.Combine(FindRepositoryRoot(), "src", "Aurelian.Graphics", "Vulkan", "Resources", "Textures");
        string[] sourceFiles = Directory.GetFiles(texturesRoot, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(sourceFiles);

        foreach (string file in sourceFiles)
        {
            string text = File.ReadAllText(file);
            Assert.DoesNotContain("AllocateMemory", text, StringComparison.Ordinal);
            Assert.DoesNotContain("FreeMemory", text, StringComparison.Ordinal);
            Assert.DoesNotContain("vkAllocateMemory", text, StringComparison.Ordinal);
            Assert.DoesNotContain("vkFreeMemory", text, StringComparison.Ordinal);
            Assert.DoesNotContain("CmdCopyBuffer" + "ToImage", text, StringComparison.Ordinal);
            Assert.DoesNotContain("vkCmdCopyBuffer" + "ToImage", text, StringComparison.Ordinal);
        }
    }

    private static VulkanTextureCreateResult CreateSmallRgbaTexture(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator)
        => VulkanTextureFactory.Create(plant, allocator, ValidPlan(plant));

    private static VulkanTextureCreatePlan ValidPlan(AurelianVulkanPlant plant)
        => new(
            plant.Context.Id,
            4,
            4,
            VulkanTextureFormat.Rgba8Unorm,
            VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferDestination,
            VulkanMemoryUsage.GpuOnly,
            VulkanResourceLayout.Undefined,
            MipLevels: 1,
            ArrayLayers: 1,
            DebugName: "test.texture");

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
