using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Compositor;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Aurelian.Graphics.Vulkan.Presentation;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Sync;
using Aurelian.Rendering.Contracts.Compositor;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanCompositorPassthroughM0Tests
{
    [Fact]
    public void VulkanCompositorPassthrough_Dispatch_WhenHeadlessOrUnavailable_SkipsCleanly()
    {
        VulkanInitResult init = CreatePresentationPlant();
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            VulkanSwapchainCreateResult swapchainResult = VulkanSwapchainFactory.Create(init.Plant!);
            using (swapchainResult.Surface)
            using (swapchainResult.Swapchain)
            {
                if (!swapchainResult.Success)
                {
                    Assert.NotEmpty(swapchainResult.Diagnostics);
                    Assert.Contains(swapchainResult.Status, new[] { VulkanPresentationStatus.Unavailable, VulkanPresentationStatus.Rejected, VulkanPresentationStatus.Failed });
                }
            }
        }
    }

    [Fact]
    public void VulkanCompositorPassthrough_RejectsUnsupportedPolicy()
        => WithCompositor((plant, compositor) =>
        {
            CompositorDispatchRequest request = Request(plant, CompositorPolicyKind.Differential, [new PlantOutputRef(plant.Context.Id.Value, 1, "final")]);

            VulkanCompositorResult result = compositor.Dispatch(request, new VulkanPlantOutputImageSet([]), CreateTargetSet(plant.Context.Id));

            Assert.False(result.Success);
            Assert.Equal(VulkanCompositorStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanCompositorDiagnosticCodes.UnsupportedPolicy);
        });

    [Fact]
    public void VulkanCompositorPassthrough_RejectsMissingInput()
        => WithCompositor((plant, compositor) =>
        {
            VulkanCompositorResult result = compositor.Dispatch(Request(plant, CompositorPolicyKind.Passthrough, []), new VulkanPlantOutputImageSet([]), CreateTargetSet(plant.Context.Id));

            Assert.False(result.Success);
            Assert.Equal(VulkanCompositorStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanCompositorDiagnosticCodes.MissingInput);
        });

    [Fact]
    public void VulkanCompositorPassthrough_RejectsMultipleInputs()
        => WithCompositor((plant, compositor) =>
        {
            PlantOutputRef first = new(plant.Context.Id.Value, 1, "first");
            PlantOutputRef second = new(plant.Context.Id.Value, 1, "second");

            VulkanCompositorResult result = compositor.Dispatch(Request(plant, CompositorPolicyKind.Passthrough, [first, second]), new VulkanPlantOutputImageSet([]), CreateTargetSet(plant.Context.Id));

            Assert.False(result.Success);
            Assert.Equal(VulkanCompositorStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanCompositorDiagnosticCodes.MultipleInputsUnsupported);
        });

    [Fact]
    public void VulkanCompositorPassthrough_RejectsMissingPlantOutput()
        => WithCompositor((plant, compositor) =>
        {
            PlantOutputRef input = new(plant.Context.Id.Value, 1, "missing");

            VulkanCompositorResult result = compositor.Dispatch(Request(plant, CompositorPolicyKind.Passthrough, [input]), new VulkanPlantOutputImageSet([]), CreateTargetSet(plant.Context.Id));

            Assert.False(result.Success);
            Assert.Equal(VulkanCompositorStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanCompositorDiagnosticCodes.PlantOutputResolutionFailed);
        });

    [Fact]
    public void VulkanCompositorPassthrough_RejectsMissingPresentationTarget()
        => WithCompositor((plant, compositor) =>
        {
            using AurelianVulkanTexture texture = VulkanPlantOutputImageM0Tests.CreateTexture(VulkanTextureUsage.TransferSource, plant.Context.Id);
            PlantOutputRef input = new(plant.Context.Id.Value, 1, "final");
            VulkanPlantOutputImageSet outputs = new([new VulkanPlantOutputImage(input, texture)]);
            VulkanPresentationTargetImageSet targets = new(plant.Context.Id, []);

            VulkanCompositorResult result = compositor.Dispatch(Request(plant, CompositorPolicyKind.Passthrough, [input]), outputs, targets);

            Assert.False(result.Success);
            Assert.Equal(VulkanCompositorStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanCompositorDiagnosticCodes.PresentationTargetResolutionFailed);
        });

    [Fact]
    public void VulkanCompositorPassthrough_DispatchPassthrough_WhenAvailable_CopiesAndSignalsFence()
    {
        VulkanInitResult init = CreatePresentationPlant();
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            VulkanSwapchainCreateResult swapchainResult = VulkanSwapchainFactory.Create(init.Plant!);
            using (swapchainResult.Surface)
            using (swapchainResult.Swapchain)
            {
                if (!swapchainResult.Success)
                {
                    Assert.NotEmpty(swapchainResult.Diagnostics);
                    return;
                }

                AurelianVulkanSwapchain swapchain = swapchainResult.Swapchain!;
                VulkanSwapchainAcquireResult acquire = swapchain.AcquireNextImage();
                if (acquire.Status is not (VulkanSwapchainAcquireStatus.Acquired or VulkanSwapchainAcquireStatus.Suboptimal) || acquire.ImageIndex is null)
                {
                    Assert.NotEmpty(acquire.Diagnostics);
                    return;
                }

                if (!TryMapTextureFormat(swapchain.Facts.SelectedFormat, out VulkanTextureFormat sourceFormat))
                {
                    return;
                }

                using var allocator = new RawVulkanMemoryAllocator(init.Plant!);
                VulkanTextureCreateResult textureResult = VulkanTextureFactory.Create(
                    init.Plant!,
                    allocator,
                    new VulkanTextureCreatePlan(
                        init.Plant!.Context.Id,
                        swapchain.Facts.Width,
                        swapchain.Facts.Height,
                        sourceFormat,
                        VulkanTextureUsage.TransferSource | VulkanTextureUsage.TransferDestination | VulkanTextureUsage.ColorAttachment,
                        VulkanMemoryUsage.GpuOnly,
                        VulkanResourceLayout.Undefined,
                        DebugName: "a53.passthrough.source"));
                if (!textureResult.Success)
                {
                    Assert.NotEmpty(textureResult.Diagnostics);
                    return;
                }

                using AurelianVulkanTexture sourceTexture = textureResult.Texture!;
                using var fences = VulkanFenceBundle.Create(init.Plant!);
                using var commandPool = VulkanCommandBufferPool.Create(init.Plant!);
                using var submitter = new VulkanCommandSubmitter(init.Plant!, commandPool, fences);
                using var compositor = new VulkanCompositorPassthrough(init.Plant!, commandPool, submitter);

                PlantOutputRef outputRef = new(init.Plant!.Context.Id.Value, 1, "final");
                VulkanPlantOutputImageSet outputs = new([new VulkanPlantOutputImage(outputRef, sourceTexture)]);
                VulkanPresentationTargetImageSet targets = swapchain.CreatePresentationTargetImageSet();
                CompositorDispatchRequest request = new(
                    1,
                    CompositorPolicyKind.Passthrough,
                    [outputRef],
                    new PresentationTargetRef(init.Plant!.Context.Id.Value, acquire.ImageIndex.Value, 1));

                VulkanCompositorResult dispatch = compositor.Dispatch(request, outputs, targets);

                Assert.True(dispatch.Success, FormatDiagnostics(dispatch));
                Assert.NotNull(dispatch.SignalFenceValue);
                Assert.Equal(VulkanResourceLayout.Present, targets.Images[(int)acquire.ImageIndex.Value].LayoutTracker.Get(0, 0));
                Assert.Equal(VulkanResourceLayout.Undefined, sourceTexture.LayoutTracker.Get(0, 0));
            }
        }
    }

    [Fact]
    public void VulkanCompositorPassthrough_Dispose_IsIdempotent()
        => WithCompositor((_, compositor) =>
        {
            compositor.Dispose();
            compositor.Dispose();
        });

    [Fact]
    public void VulkanCompositorPassthrough_DispatchAfterDispose_ReturnsDisposedDiagnostic()
        => WithCompositor((plant, compositor) =>
        {
            compositor.Dispose();

            VulkanCompositorResult result = compositor.Dispatch(Request(plant, CompositorPolicyKind.Passthrough, []), null!, null!);

            Assert.False(result.Success);
            Assert.Equal(VulkanCompositorStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanCompositorDiagnosticCodes.CompositorDisposed);
        });

    private static VulkanInitResult CreatePresentationPlant()
        => VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false, EnablePresentation: true));

    private static void WithCompositor(Action<AurelianVulkanPlant, VulkanCompositorPassthrough> action)
    {
        VulkanInitResult init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            using var fences = VulkanFenceBundle.Create(init.Plant!);
            using var commandPool = VulkanCommandBufferPool.Create(init.Plant!);
            using var submitter = new VulkanCommandSubmitter(init.Plant!, commandPool, fences);
            using var compositor = new VulkanCompositorPassthrough(init.Plant!, commandPool, submitter);
            action(init.Plant!, compositor);
        }
    }

    private static CompositorDispatchRequest Request(AurelianVulkanPlant plant, CompositorPolicyKind policy, IReadOnlyList<PlantOutputRef> inputs)
        => new(1, policy, inputs, new PresentationTargetRef(plant.Context.Id.Value, 0, 1));

    private static VulkanPresentationTargetImageSet CreateTargetSet(PlantId plantId)
        => new(plantId, [new VulkanPresentationTargetImage(plantId, 0, default, default, 64, 64, "B8G8R8A8Srgb")]);

    private static bool TryMapTextureFormat(string swapchainFormat, out VulkanTextureFormat textureFormat)
    {
        textureFormat = swapchainFormat switch
        {
            "B8G8R8A8Srgb" => VulkanTextureFormat.Bgra8Srgb,
            "B8G8R8A8Unorm" => VulkanTextureFormat.Bgra8Unorm,
            "R8G8B8A8Srgb" => VulkanTextureFormat.Rgba8Srgb,
            "R8G8B8A8Unorm" => VulkanTextureFormat.Rgba8Unorm,
            _ => default,
        };

        return swapchainFormat is "B8G8R8A8Srgb" or "B8G8R8A8Unorm" or "R8G8B8A8Srgb" or "R8G8B8A8Unorm";
    }

    private static string FormatDiagnostics(VulkanCompositorResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
