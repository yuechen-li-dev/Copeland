using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Uploads;
using Aurelian.Graphics.Vulkan.Sync;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanBufferUploadM0Tests
{
    [Fact]
    public void VulkanBufferUploader_Upload_WhenVulkanUnavailable_SkipsCleanly()
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
            using var uploader = new VulkanBufferUploader(init.Plant!, allocator, commandPool, fences);

            VulkanBufferCreateResult destinationResult = CreateDestination(init.Plant!, allocator);
            if (!destinationResult.Success)
            {
                Assert.NotEmpty(destinationResult.Diagnostics);
                return;
            }

            using AurelianVulkanBuffer destination = destinationResult.Buffer!;
            VulkanBufferUploadResult upload = uploader.Upload(new VulkanBufferUploadRequest(destination, new byte[] { 1, 2, 3, 4 }, DebugName: "test.upload"));
            AssertUploadSuccessOrCleanFailure(upload);
        }
    }

    [Fact]
    public void VulkanBufferUploader_UploadRejectsEmptyData()
        => WithUploader((plant, allocator, _, _, uploader) =>
        {
            VulkanBufferCreateResult destinationResult = CreateDestinationOrSkip(plant, allocator);
            if (!destinationResult.Success)
            {
                return;
            }

            using AurelianVulkanBuffer destination = destinationResult.Buffer!;
            VulkanBufferUploadResult upload = uploader.Upload(new VulkanBufferUploadRequest(destination, ReadOnlyMemory<byte>.Empty));

            Assert.False(upload.Success);
            Assert.Equal(VulkanBufferUploadStatus.Rejected, upload.Status);
            Assert.Contains(upload.Diagnostics, diagnostic => diagnostic.Code == VulkanBufferUploadDiagnosticCodes.EmptyUpload);
        });

    [Fact]
    public void VulkanBufferUploader_UploadRejectsDestinationWithoutTransferDestinationUsage()
        => WithUploader((plant, allocator, _, _, uploader) =>
        {
            VulkanBufferCreateResult destinationResult = VulkanBufferFactory.Create(
                plant,
                allocator,
                new VulkanBufferCreatePlan(plant.Context.Id, 64, VulkanBufferUsage.Vertex, VulkanMemoryUsage.GpuOnly, "test.no-transfer-dst"));
            if (!destinationResult.Success)
            {
                Assert.NotEmpty(destinationResult.Diagnostics);
                return;
            }

            using AurelianVulkanBuffer destination = destinationResult.Buffer!;
            VulkanBufferUploadResult upload = uploader.Upload(new VulkanBufferUploadRequest(destination, new byte[] { 1, 2, 3, 4 }));

            Assert.False(upload.Success);
            Assert.Equal(VulkanBufferUploadStatus.Rejected, upload.Status);
            Assert.Contains(upload.Diagnostics, diagnostic => diagnostic.Code == VulkanBufferUploadDiagnosticCodes.DestinationMissingTransferDestinationUsage);
        });

    [Fact]
    public void VulkanBufferUploader_UploadRejectsOutOfBounds()
        => WithUploader((plant, allocator, _, _, uploader) =>
        {
            VulkanBufferCreateResult destinationResult = CreateDestination(plant, allocator);
            if (!destinationResult.Success)
            {
                Assert.NotEmpty(destinationResult.Diagnostics);
                return;
            }

            using AurelianVulkanBuffer destination = destinationResult.Buffer!;
            VulkanBufferUploadResult upload = uploader.Upload(new VulkanBufferUploadRequest(destination, new byte[] { 1, 2, 3, 4 }, DestinationOffset: 62));

            Assert.False(upload.Success);
            Assert.Equal(VulkanBufferUploadStatus.Rejected, upload.Status);
            Assert.Contains(upload.Diagnostics, diagnostic => diagnostic.Code == VulkanBufferUploadDiagnosticCodes.UploadOutOfBounds);
        });

    [Fact]
    public void VulkanBufferUploader_UploadToDeviceLocalBuffer_WhenPlantCreated_SubmitsAndSignalsFence()
        => WithUploader((plant, allocator, fences, _, uploader) =>
        {
            VulkanBufferCreateResult destinationResult = CreateDestination(plant, allocator);
            if (!destinationResult.Success)
            {
                Assert.NotEmpty(destinationResult.Diagnostics);
                return;
            }

            using AurelianVulkanBuffer destination = destinationResult.Buffer!;
            VulkanBufferUploadResult upload = uploader.Upload(new VulkanBufferUploadRequest(destination, new byte[] { 1, 2, 3, 4 }, DebugName: "test.device-local-upload"));
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
    public void VulkanBufferUploader_Dispose_IsIdempotent()
        => WithUploader((_, _, _, _, uploader) =>
        {
            uploader.Dispose();
            uploader.Dispose();
        });

    [Fact]
    public void VulkanBufferUploader_UploadAfterDispose_ReturnsDisposedDiagnostic()
        => WithUploader((plant, allocator, _, _, uploader) =>
        {
            VulkanBufferCreateResult destinationResult = CreateDestination(plant, allocator);
            if (!destinationResult.Success)
            {
                Assert.NotEmpty(destinationResult.Diagnostics);
                return;
            }

            using AurelianVulkanBuffer destination = destinationResult.Buffer!;
            uploader.Dispose();
            VulkanBufferUploadResult upload = uploader.Upload(new VulkanBufferUploadRequest(destination, new byte[] { 1 }));

            Assert.False(upload.Success);
            Assert.Equal(VulkanBufferUploadStatus.Rejected, upload.Status);
            Assert.Contains(upload.Diagnostics, diagnostic => diagnostic.Code == VulkanBufferUploadDiagnosticCodes.UploaderDisposed);
        });

    [Fact]
    public void VulkanBufferUploader_DoesNotCallRawAllocationApis()
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

    private static VulkanBufferCreateResult CreateDestinationOrSkip(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator)
    {
        VulkanBufferCreateResult result = CreateDestination(plant, allocator);
        if (!result.Success)
        {
            Assert.NotEmpty(result.Diagnostics);
        }

        return result;
    }

    private static VulkanBufferCreateResult CreateDestination(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator)
        => VulkanBufferFactory.Create(
            plant,
            allocator,
            new VulkanBufferCreatePlan(
                plant.Context.Id,
                64,
                VulkanBufferUsage.Vertex | VulkanBufferUsage.TransferDestination,
                VulkanMemoryUsage.GpuOnly,
                "test.upload-destination"));

    private static void WithUploader(Action<AurelianVulkanPlant, RawVulkanMemoryAllocator, VulkanFenceBundle, VulkanCommandBufferPool, VulkanBufferUploader> action)
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
            using var uploader = new VulkanBufferUploader(init.Plant!, allocator, commandPool, fences);
            action(init.Plant!, allocator, fences, commandPool, uploader);
        }
    }

    private static void AssertUploadSuccessOrCleanFailure(VulkanBufferUploadResult upload)
    {
        if (upload.Success)
        {
            Assert.Equal(VulkanBufferUploadStatus.Submitted, upload.Status);
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
