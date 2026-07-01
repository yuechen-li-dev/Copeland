using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Resources.Textures;

public static unsafe class VulkanTextureFactory
{
    public static VulkanTextureCreateResult Create(
        AurelianVulkanPlant plant,
        IVulkanMemoryAllocator allocator,
        VulkanTextureCreatePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plant);
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(plan);

        List<VulkanTextureDiagnostic> diagnostics = [];
        Validate(plant.Context.Id, allocator.PlantId, plan, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == VulkanTextureDiagnosticSeverity.Error))
        {
            return new VulkanTextureCreateResult(VulkanTextureStatus.Rejected, null, diagnostics);
        }

        Vk vk = plant.Vk;
        Silk.NET.Vulkan.Device device = plant.Device;
        Image image = default;
        ImageView imageView = default;
        bool imageViewCreated = false;
        VulkanMemoryAllocation? allocation = null;

        try
        {
            Format nativeFormat = MapFormat(plan.Format);
            ImageUsageFlags nativeUsage = MapUsage(plan.Usage);
            ImageCreateInfo createInfo = new()
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = nativeFormat,
                Extent = new Extent3D(plan.Width, plan.Height, 1),
                MipLevels = plan.MipLevels,
                ArrayLayers = plan.ArrayLayers,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = nativeUsage,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined,
            };

            Result createResult = vk.CreateImage(device, &createInfo, (AllocationCallbacks*)null, out image);
            if (createResult != Result.Success)
            {
                diagnostics.Add(Diagnostic(
                    VulkanTextureDiagnosticCodes.ImageCreationFailed,
                    VulkanTextureDiagnosticSeverity.Error,
                    $"vkCreateImage failed with result {createResult}.",
                    plan));
                return new VulkanTextureCreateResult(VulkanTextureStatus.Failed, null, diagnostics);
            }

            vk.GetImageMemoryRequirements(device, image, out MemoryRequirements requirements);
            if (requirements.Size == 0 || requirements.MemoryTypeBits == 0)
            {
                DestroyImage(vk, device, ref image);
                diagnostics.Add(Diagnostic(
                    VulkanTextureDiagnosticCodes.MemoryRequirementsFailed,
                    VulkanTextureDiagnosticSeverity.Error,
                    "Vulkan returned empty memory requirements for the image.",
                    plan));
                return new VulkanTextureCreateResult(VulkanTextureStatus.Failed, null, diagnostics);
            }

            VulkanAllocationResult allocationResult = allocator.Allocate(new VulkanAllocationRequest(
                plan.PlantId,
                requirements.Size,
                requirements.MemoryTypeBits,
                plan.MemoryUsage,
                plan.DebugName,
                MapOnCreate: false));

            if (!allocationResult.Success)
            {
                DestroyImage(vk, device, ref image);
                diagnostics.Add(Diagnostic(
                    VulkanTextureDiagnosticCodes.AllocationFailed,
                    VulkanTextureDiagnosticSeverity.Error,
                    "Texture image memory allocation failed through IVulkanMemoryAllocator.",
                    plan));
                diagnostics.AddRange(allocationResult.Diagnostics.Select(diagnostic => new VulkanTextureDiagnostic(
                    diagnostic.Code,
                    MapSeverity(diagnostic.Severity),
                    diagnostic.Message,
                    diagnostic.PlantId,
                    diagnostic.DebugName)));
                return new VulkanTextureCreateResult(
                    allocationResult.Status == VulkanMemoryAllocatorStatus.Failed ? VulkanTextureStatus.Failed : VulkanTextureStatus.Rejected,
                    null,
                    diagnostics);
            }

            allocation = allocationResult.Allocation!;
            Result bindResult = vk.BindImageMemory(device, image, allocation.Memory, allocation.Offset);
            if (bindResult != Result.Success)
            {
                allocation.Dispose();
                allocation = null;
                DestroyImage(vk, device, ref image);
                diagnostics.Add(Diagnostic(
                    VulkanTextureDiagnosticCodes.BindMemoryFailed,
                    VulkanTextureDiagnosticSeverity.Error,
                    $"vkBindImageMemory failed with result {bindResult}.",
                    plan));
                return new VulkanTextureCreateResult(VulkanTextureStatus.Failed, null, diagnostics);
            }

            ImageView? optionalImageView = null;
            if (ShouldCreateDefaultImageView(plan.Usage))
            {
                ImageViewCreateInfo viewCreateInfo = new()
                {
                    SType = StructureType.ImageViewCreateInfo,
                    Image = image,
                    ViewType = plan.ArrayLayers == 1 ? ImageViewType.Type2D : ImageViewType.Type2DArray,
                    Format = nativeFormat,
                    SubresourceRange = new ImageSubresourceRange(
                        ImageAspectFlags.ColorBit,
                        0,
                        plan.MipLevels,
                        0,
                        plan.ArrayLayers),
                };

                Result viewResult = vk.CreateImageView(device, &viewCreateInfo, (AllocationCallbacks*)null, out imageView);
                if (viewResult != Result.Success)
                {
                    allocation.Dispose();
                    allocation = null;
                    DestroyImage(vk, device, ref image);
                    diagnostics.Add(Diagnostic(
                        VulkanTextureDiagnosticCodes.ImageViewCreationFailed,
                        VulkanTextureDiagnosticSeverity.Error,
                        $"vkCreateImageView failed with result {viewResult}.",
                        plan));
                    return new VulkanTextureCreateResult(VulkanTextureStatus.Failed, null, diagnostics);
                }

                imageViewCreated = true;
                optionalImageView = imageView;
            }

            AurelianVulkanTexture texture = new(
                vk,
                device,
                image,
                optionalImageView,
                allocation,
                plan.PlantId,
                plan.Width,
                plan.Height,
                plan.MipLevels,
                plan.ArrayLayers,
                plan.Format,
                plan.Usage,
                plan.InitialLayout);

            image = default;
            imageView = default;
            imageViewCreated = false;
            allocation = null;

            return new VulkanTextureCreateResult(VulkanTextureStatus.Created, texture, diagnostics);
        }
        catch (Exception ex)
        {
            if (imageViewCreated)
            {
                DestroyImageView(vk, device, ref imageView);
            }

            allocation?.Dispose();
            DestroyImage(vk, device, ref image);
            diagnostics.Add(Diagnostic(
                VulkanTextureDiagnosticCodes.ImageCreationFailed,
                VulkanTextureDiagnosticSeverity.Error,
                $"Unexpected Vulkan texture creation failure: {ex.GetType().Name}: {ex.Message}",
                plan));
            return new VulkanTextureCreateResult(VulkanTextureStatus.Failed, null, diagnostics);
        }
    }

    private static void Validate(
        PlantId plantId,
        PlantId allocatorPlantId,
        VulkanTextureCreatePlan plan,
        List<VulkanTextureDiagnostic> diagnostics)
    {
        if (plan.Width == 0 || plan.Height == 0)
        {
            diagnostics.Add(Diagnostic(
                VulkanTextureDiagnosticCodes.InvalidDimensions,
                VulkanTextureDiagnosticSeverity.Error,
                "Texture width and height must be greater than zero.",
                plan));
        }

        if (plan.MipLevels == 0)
        {
            diagnostics.Add(Diagnostic(
                VulkanTextureDiagnosticCodes.InvalidMipLevels,
                VulkanTextureDiagnosticSeverity.Error,
                "Texture mip level count must be greater than zero.",
                plan));
        }

        if (plan.ArrayLayers == 0)
        {
            diagnostics.Add(Diagnostic(
                VulkanTextureDiagnosticCodes.InvalidArrayLayers,
                VulkanTextureDiagnosticSeverity.Error,
                "Texture array layer count must be greater than zero.",
                plan));
        }

        if (plan.Usage == VulkanTextureUsage.None)
        {
            diagnostics.Add(Diagnostic(
                VulkanTextureDiagnosticCodes.InvalidTextureUsage,
                VulkanTextureDiagnosticSeverity.Error,
                "Texture usage must include at least one usage flag.",
                plan));
        }

        if (!Enum.IsDefined(plan.Format))
        {
            diagnostics.Add(Diagnostic(
                VulkanTextureDiagnosticCodes.UnsupportedFormat,
                VulkanTextureDiagnosticSeverity.Error,
                "Texture format is not supported by Texture2D M0.",
                plan));
        }

        if (plan.MemoryUsage == VulkanMemoryUsage.Unknown)
        {
            diagnostics.Add(Diagnostic(
                VulkanTextureDiagnosticCodes.InvalidMemoryUsage,
                VulkanTextureDiagnosticSeverity.Error,
                "Texture memory usage must not be Unknown.",
                plan));
        }

        if (plan.PlantId != plantId || allocatorPlantId != plantId)
        {
            diagnostics.Add(Diagnostic(
                VulkanTextureDiagnosticCodes.PlantMismatch,
                VulkanTextureDiagnosticSeverity.Error,
                "Texture plan plant, plant context, and allocator plant must match.",
                plan));
        }

        if (plan.InitialLayout != VulkanResourceLayout.Undefined)
        {
            diagnostics.Add(Diagnostic(
                VulkanTextureDiagnosticCodes.UnsupportedInitialLayout,
                VulkanTextureDiagnosticSeverity.Error,
                "Texture M0 supports only Undefined initial layout because VkImageCreateInfo.initialLayout cannot truthfully start in shader, transfer, or attachment layouts without barrier emission.",
                plan));
        }
    }

    private static Format MapFormat(VulkanTextureFormat format)
        => format switch
        {
            VulkanTextureFormat.Rgba8Unorm => Format.R8G8B8A8Unorm,
            VulkanTextureFormat.Bgra8Unorm => Format.B8G8R8A8Unorm,
            VulkanTextureFormat.Rgba8Srgb => Format.R8G8B8A8Srgb,
            VulkanTextureFormat.Bgra8Srgb => Format.B8G8R8A8Srgb,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported Vulkan texture format."),
        };

    private static ImageUsageFlags MapUsage(VulkanTextureUsage usage)
    {
        ImageUsageFlags flags = 0;
        if ((usage & VulkanTextureUsage.ShaderResource) != 0)
        {
            flags |= ImageUsageFlags.SampledBit;
        }

        if ((usage & VulkanTextureUsage.ColorAttachment) != 0)
        {
            flags |= ImageUsageFlags.ColorAttachmentBit;
        }

        if ((usage & VulkanTextureUsage.TransferSource) != 0)
        {
            flags |= ImageUsageFlags.TransferSrcBit;
        }

        if ((usage & VulkanTextureUsage.TransferDestination) != 0)
        {
            flags |= ImageUsageFlags.TransferDstBit;
        }

        return flags;
    }

    private static bool ShouldCreateDefaultImageView(VulkanTextureUsage usage)
        => (usage & (VulkanTextureUsage.ShaderResource | VulkanTextureUsage.ColorAttachment)) != 0;

    private static VulkanTextureDiagnosticSeverity MapSeverity(VulkanMemoryAllocatorDiagnosticSeverity severity)
        => severity switch
        {
            VulkanMemoryAllocatorDiagnosticSeverity.Warning => VulkanTextureDiagnosticSeverity.Warning,
            VulkanMemoryAllocatorDiagnosticSeverity.Info => VulkanTextureDiagnosticSeverity.Info,
            _ => VulkanTextureDiagnosticSeverity.Error,
        };

    private static VulkanTextureDiagnostic Diagnostic(
        string code,
        VulkanTextureDiagnosticSeverity severity,
        string message,
        VulkanTextureCreatePlan plan)
        => new(code, severity, message, plan.PlantId, plan.DebugName);

    private static void DestroyImage(Vk vk, Silk.NET.Vulkan.Device device, ref Image image)
    {
        if (image.Handle != 0 && device.Handle != 0)
        {
            vk.DestroyImage(device, image, (AllocationCallbacks*)null);
            image = default;
        }
    }

    private static void DestroyImageView(Vk vk, Silk.NET.Vulkan.Device device, ref ImageView imageView)
    {
        if (imageView.Handle != 0 && device.Handle != 0)
        {
            vk.DestroyImageView(device, imageView, (AllocationCallbacks*)null);
            imageView = default;
        }
    }
}
