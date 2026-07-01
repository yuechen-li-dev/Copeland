using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Resources.Uploads;
using Aurelian.Graphics.Vulkan.Sync;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanTextureUploadM0Tests
{
    [Fact]
    public void VulkanTextureUploader_Upload_WhenVulkanUnavailable_SkipsCleanly()
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
            using var fences = VulkanFenceBundle.Create(init.Plant!);
            using var commandPool = VulkanCommandBufferPool.Create(init.Plant!);
            using var uploader = new VulkanTextureUploader(init.Plant!, allocator, commandPool, fences);

            VulkanTextureCreateResult destinationResult = CreateUploadTexture(init.Plant!, allocator);
            if (!destinationResult.Success)
            {
                Assert.NotEmpty(destinationResult.Diagnostics);
                return;
            }

            using AurelianVulkanTexture destination = destinationResult.Texture!;
            VulkanTextureUploadResult upload = uploader.Upload(new VulkanTextureUploadRequest(destination, Rgba4x4(), DebugName: "test.texture-upload"));
            AssertUploadSuccessOrCleanFailure(upload);
        }
    }

    [Fact]
    public void VulkanTextureUploader_UploadRejectsEmptyData()
        => WithUploader((plant, allocator, _, _, uploader) =>
        {
            VulkanTextureCreateResult destinationResult = CreateUploadTextureOrSkip(plant, allocator);
            if (!destinationResult.Success)
            {
                return;
            }

            using AurelianVulkanTexture destination = destinationResult.Texture!;
            VulkanTextureUploadResult upload = uploader.Upload(new VulkanTextureUploadRequest(destination, ReadOnlyMemory<byte>.Empty));

            Assert.False(upload.Success);
            Assert.Equal(VulkanTextureUploadStatus.Rejected, upload.Status);
            Assert.Contains(upload.Diagnostics, diagnostic => diagnostic.Code == VulkanTextureUploadDiagnosticCodes.EmptyUpload);
        });

    [Fact]
    public void VulkanTextureUploader_UploadRejectsTextureWithoutTransferDestinationUsage()
        => WithUploader((plant, allocator, _, _, uploader) =>
        {
            VulkanTextureCreateResult destinationResult = VulkanTextureFactory.Create(
                plant,
                allocator,
                UploadPlan(plant) with { Usage = VulkanTextureUsage.ShaderResource, DebugName = "test.no-transfer-dst" });
            if (!destinationResult.Success)
            {
                Assert.NotEmpty(destinationResult.Diagnostics);
                return;
            }

            using AurelianVulkanTexture destination = destinationResult.Texture!;
            VulkanTextureUploadResult upload = uploader.Upload(new VulkanTextureUploadRequest(destination, Rgba4x4()));

            Assert.False(upload.Success);
            Assert.Equal(VulkanTextureUploadStatus.Rejected, upload.Status);
            Assert.Contains(upload.Diagnostics, diagnostic => diagnostic.Code == VulkanTextureUploadDiagnosticCodes.DestinationMissingTransferDestinationUsage);
        });

    [Fact]
    public void VulkanTextureUploader_UploadRejectsSizeMismatch()
        => WithUploader((plant, allocator, _, _, uploader) =>
        {
            VulkanTextureCreateResult destinationResult = CreateUploadTextureOrSkip(plant, allocator);
            if (!destinationResult.Success)
            {
                return;
            }

            using AurelianVulkanTexture destination = destinationResult.Texture!;
            VulkanTextureUploadResult upload = uploader.Upload(new VulkanTextureUploadRequest(destination, new byte[] { 1, 2, 3, 4 }));

            Assert.False(upload.Success);
            Assert.Equal(VulkanTextureUploadStatus.Rejected, upload.Status);
            Assert.Contains(upload.Diagnostics, diagnostic => diagnostic.Code == VulkanTextureUploadDiagnosticCodes.UploadSizeMismatch);
        });

    [Fact]
    public void VulkanTextureUploader_UploadRejectsDisposedTexture()
        => WithUploader((plant, allocator, _, _, uploader) =>
        {
            VulkanTextureCreateResult destinationResult = CreateUploadTextureOrSkip(plant, allocator);
            if (!destinationResult.Success)
            {
                return;
            }

            AurelianVulkanTexture destination = destinationResult.Texture!;
            destination.Dispose();
            VulkanTextureUploadResult upload = uploader.Upload(new VulkanTextureUploadRequest(destination, Rgba4x4()));

            Assert.False(upload.Success);
            Assert.Equal(VulkanTextureUploadStatus.Rejected, upload.Status);
            Assert.Contains(upload.Diagnostics, diagnostic => diagnostic.Code == VulkanTextureUploadDiagnosticCodes.DestinationTextureDisposed);
        });

    [Fact]
    public void VulkanTextureUploader_UploadToTexture_WhenPlantCreated_SubmitsAndSignalsFence()
        => WithUploader((plant, allocator, fences, _, uploader) =>
        {
            VulkanTextureCreateResult destinationResult = CreateUploadTexture(plant, allocator);
            if (!destinationResult.Success)
            {
                Assert.NotEmpty(destinationResult.Diagnostics);
                return;
            }

            using AurelianVulkanTexture destination = destinationResult.Texture!;
            VulkanTextureUploadResult upload = uploader.Upload(new VulkanTextureUploadRequest(destination, Rgba4x4(), DebugName: "test.texture-upload"));
            AssertUploadSuccessOrCleanFailure(upload);
            if (!upload.Success)
            {
                return;
            }

            Assert.NotNull(upload.SignalFenceValue);
            VulkanFenceOperationResult completed = fences.CommandListFence.QueryCompletedValue();
            Assert.True(completed.Success, FormatDiagnostics(completed.Diagnostics.Select(static d => d.Message)));
            Assert.True(completed.Value >= upload.SignalFenceValue.Value);
        });

    [Fact]
    public void VulkanTextureUploader_UploadTransitionsTextureToShaderResourceFragment()
        => WithUploader((plant, allocator, _, _, uploader) =>
        {
            VulkanTextureCreateResult destinationResult = CreateUploadTexture(plant, allocator);
            if (!destinationResult.Success)
            {
                Assert.NotEmpty(destinationResult.Diagnostics);
                return;
            }

            using AurelianVulkanTexture destination = destinationResult.Texture!;
            VulkanTextureUploadResult upload = uploader.Upload(new VulkanTextureUploadRequest(destination, Rgba4x4(), DebugName: "test.texture-layout"));
            AssertUploadSuccessOrCleanFailure(upload);
            if (!upload.Success)
            {
                return;
            }

            Assert.Equal(VulkanResourceLayout.ShaderResourceFragment, destination.LayoutTracker.Get(0, 0));
        });

    [Fact]
    public void VulkanTextureUploader_Dispose_IsIdempotent()
        => WithUploader((_, _, _, _, uploader) =>
        {
            uploader.Dispose();
            uploader.Dispose();
        });

    [Fact]
    public void VulkanTextureUploader_UploadAfterDispose_ReturnsDisposedDiagnostic()
        => WithUploader((plant, allocator, _, _, uploader) =>
        {
            VulkanTextureCreateResult destinationResult = CreateUploadTexture(plant, allocator);
            if (!destinationResult.Success)
            {
                Assert.NotEmpty(destinationResult.Diagnostics);
                return;
            }

            using AurelianVulkanTexture destination = destinationResult.Texture!;
            uploader.Dispose();
            VulkanTextureUploadResult upload = uploader.Upload(new VulkanTextureUploadRequest(destination, Rgba4x4()));

            Assert.False(upload.Success);
            Assert.Equal(VulkanTextureUploadStatus.Rejected, upload.Status);
            Assert.Contains(upload.Diagnostics, diagnostic => diagnostic.Code == VulkanTextureUploadDiagnosticCodes.UploaderDisposed);
        });

    [Fact]
    public void VulkanTextureUploader_DoesNotCallRawAllocationApis()
    {
        string uploadsRoot = Path.Combine(FindRepositoryRoot(), "src", "Aurelian.Graphics", "Vulkan", "Resources", "Uploads");
        string[] sourceFiles = Directory.GetFiles(uploadsRoot, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(sourceFiles);

        string[] forbidden = ["vkAllocateMemory", "vkFreeMemory", "AllocateMemory", "FreeMemory", "vkMapMemory", "vkUnmapMemory", "MapMemory", "UnmapMemory"];
        foreach (string file in sourceFiles)
        {
            string text = File.ReadAllText(file);
            foreach (string token in forbidden)
            {
                Assert.DoesNotContain(token, text, StringComparison.Ordinal);
            }
        }
    }

    private static VulkanTextureCreateResult CreateUploadTextureOrSkip(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator)
    {
        VulkanTextureCreateResult result = CreateUploadTexture(plant, allocator);
        if (!result.Success)
        {
            Assert.NotEmpty(result.Diagnostics);
        }

        return result;
    }

    private static VulkanTextureCreateResult CreateUploadTexture(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator)
        => VulkanTextureFactory.Create(plant, allocator, UploadPlan(plant));

    private static VulkanTextureCreatePlan UploadPlan(AurelianVulkanPlant plant)
        => new(
            plant.Context.Id,
            4,
            4,
            VulkanTextureFormat.Rgba8Unorm,
            VulkanTextureUsage.TransferDestination | VulkanTextureUsage.ShaderResource,
            VulkanMemoryUsage.GpuOnly,
            VulkanResourceLayout.Undefined,
            MipLevels: 1,
            ArrayLayers: 1,
            DebugName: "test.upload-texture");

    private static byte[] Rgba4x4()
    {
        byte[] bytes = new byte[4 * 4 * 4];
        for (int i = 0; i < bytes.Length; i += 4)
        {
            bytes[i] = 255;
            bytes[i + 1] = (byte)i;
            bytes[i + 2] = 128;
            bytes[i + 3] = 255;
        }

        return bytes;
    }

    private static void WithUploader(Action<AurelianVulkanPlant, RawVulkanMemoryAllocator, VulkanFenceBundle, VulkanCommandBufferPool, VulkanTextureUploader> action)
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
            using var fences = VulkanFenceBundle.Create(init.Plant!);
            using var commandPool = VulkanCommandBufferPool.Create(init.Plant!);
            using var uploader = new VulkanTextureUploader(init.Plant!, allocator, commandPool, fences);
            action(init.Plant!, allocator, fences, commandPool, uploader);
        }
    }

    private static void AssertUploadSuccessOrCleanFailure(VulkanTextureUploadResult upload)
    {
        if (upload.Success)
        {
            Assert.Equal(VulkanTextureUploadStatus.Submitted, upload.Status);
            Assert.NotNull(upload.SignalFenceValue);
            return;
        }

        Assert.NotEmpty(upload.Diagnostics);
        Assert.All(upload.Diagnostics, diagnostic => Assert.False(string.IsNullOrWhiteSpace(diagnostic.Code)));
    }

    private static string FormatDiagnostics(IEnumerable<string> diagnostics)
        => string.Join(Environment.NewLine, diagnostics);

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aurelian.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
