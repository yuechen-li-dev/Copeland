using Aurelian.Graphics.Vulkan.Device;
using Silk.NET.Core;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;

namespace Aurelian.Graphics.Vulkan.Presentation;

public static unsafe class VulkanSwapchainFactory
{
    private const uint SpecialCurrentExtent = uint.MaxValue;

    public static VulkanSwapchainCreateResult Create(
        AurelianVulkanPlant plant,
        VulkanSwapchainCreateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plant);
        options ??= new VulkanSwapchainCreateOptions();
        List<VulkanPresentationDiagnostic> diagnostics = [];

        if (!plant.Facts.EnabledDeviceExtensions.Contains(KhrSwapchain.ExtensionName, StringComparer.Ordinal))
        {
            return ResultWith(
                VulkanPresentationStatus.Rejected,
                diagnostics,
                VulkanPresentationDiagnosticCodes.SwapchainExtensionMissing,
                VulkanPresentationDiagnosticSeverity.Error,
                $"Plant '{plant.Context.Id}' was not created with {KhrSwapchain.ExtensionName}; create it with VulkanPlantOptions.EnablePresentation = true.",
                plant.Context.Id);
        }

        IWindow? window = null;
        KhrSurface? surfaceApi = null;
        KhrSwapchain? swapchainApi = null;
        AurelianVulkanSurface? surfaceOwner = null;
        AurelianVulkanSwapchain? swapchainOwner = null;
        VulkanPresentationSemaphoreSet? semaphoreSet = null;

        try
        {
            window = CreateWindow(options);
            window.Initialize();

            IVkSurface? vkSurface = window.VkSurface;
            if (vkSurface is null)
            {
                window.Dispose();
                return ResultWith(
                    VulkanPresentationStatus.Unavailable,
                    diagnostics,
                    VulkanPresentationDiagnosticCodes.SurfaceCreationFailed,
                    VulkanPresentationDiagnosticSeverity.Error,
                    "Silk.NET.Windowing did not expose a Vulkan surface source for the created window.",
                    plant.Context.Id);
            }

            if (!plant.Vk.TryGetInstanceExtension(plant.Instance, out surfaceApi))
            {
                window.Dispose();
                return ResultWith(
                    VulkanPresentationStatus.Rejected,
                    diagnostics,
                    VulkanPresentationDiagnosticCodes.SurfaceCreationFailed,
                    VulkanPresentationDiagnosticSeverity.Error,
                    "VK_KHR_surface commands are unavailable on the presentation plant instance.",
                    plant.Context.Id);
            }

            KhrSurface surfaceCommands = surfaceApi!;
            SurfaceKHR surface = new(vkSurface.Create(new VkHandle(plant.Instance.Handle), (AllocationCallbacks*)null).Handle);
            if (surface.Handle == 0)
            {
                surfaceCommands.Dispose();
                window.Dispose();
                return ResultWith(
                    VulkanPresentationStatus.Unavailable,
                    diagnostics,
                    VulkanPresentationDiagnosticCodes.SurfaceCreationFailed,
                    VulkanPresentationDiagnosticSeverity.Error,
                    "Silk.NET.Windowing returned a null VkSurfaceKHR handle.",
                    plant.Context.Id);
            }

            Result supportResult = surfaceCommands.GetPhysicalDeviceSurfaceSupport(plant.PhysicalDevice, plant.QueueFamilyIndex, surface, out Bool32 supported);
            if (supportResult != Result.Success || !supported)
            {
                surfaceCommands.DestroySurface(plant.Instance, surface, (AllocationCallbacks*)null);
                surfaceCommands.Dispose();
                window.Dispose();
                return ResultWith(
                    VulkanPresentationStatus.Rejected,
                    diagnostics,
                    VulkanPresentationDiagnosticCodes.SurfaceSupportMissing,
                    VulkanPresentationDiagnosticSeverity.Error,
                    $"Selected queue family {plant.QueueFamilyIndex} does not support presentation to the created surface. Vulkan result: {supportResult}.",
                    plant.Context.Id);
            }

            if (!TryQuerySurfaceState(surfaceCommands, plant, surface, diagnostics, out SurfaceCapabilitiesKHR capabilities, out SurfaceFormatKHR[] formats, out PresentModeKHR[] presentModes, out VulkanSwapchainCreateResult? queryFailure))
            {
                surfaceCommands.DestroySurface(plant.Instance, surface, (AllocationCallbacks*)null);
                surfaceCommands.Dispose();
                window.Dispose();
                return queryFailure!;
            }

            ImageUsageFlags imageUsage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferDstBit;
            if ((capabilities.SupportedUsageFlags & imageUsage) != imageUsage)
            {
                surfaceCommands.DestroySurface(plant.Instance, surface, (AllocationCallbacks*)null);
                surfaceCommands.Dispose();
                window.Dispose();
                return ResultWith(
                    VulkanPresentationStatus.Rejected,
                    diagnostics,
                    VulkanPresentationDiagnosticCodes.SwapchainCreationFailed,
                    VulkanPresentationDiagnosticSeverity.Error,
                    $"Surface does not support swapchain image usage required by the compositor copy path. Supported usage: {capabilities.SupportedUsageFlags}; required usage: {imageUsage}.",
                    plant.Context.Id);
            }

            SurfaceFormatKHR selectedFormat = SelectSurfaceFormat(formats);
            PresentModeKHR selectedPresentMode = SelectPresentMode(presentModes, options.VSync);
            Extent2D extent = ChooseExtent(capabilities, options.Width, options.Height);
            uint imageCount = ChooseImageCount(capabilities);

            VulkanSurfaceFacts surfaceFacts = new(
                plant.Context.Id,
                extent.Width,
                extent.Height,
                (uint)capabilities.CurrentTransform,
                capabilities.MinImageCount,
                capabilities.MaxImageCount);
            surfaceOwner = new AurelianVulkanSurface(plant, surfaceCommands, window, surface, surfaceFacts);
            surfaceApi = null;
            window = null;

            if (!plant.Vk.TryGetDeviceExtension(plant.Instance, plant.Device, out swapchainApi))
            {
                surfaceOwner.Dispose();
                return ResultWith(
                    VulkanPresentationStatus.Rejected,
                    diagnostics,
                    VulkanPresentationDiagnosticCodes.SwapchainExtensionMissing,
                    VulkanPresentationDiagnosticSeverity.Error,
                    "VK_KHR_swapchain commands are unavailable on the presentation plant device.",
                    plant.Context.Id);
            }

            KhrSwapchain swapchainCommands = swapchainApi!;
            SwapchainCreateInfoKHR createInfo = new()
            {
                SType = StructureType.SwapchainCreateInfoKhr,
                Surface = surface,
                MinImageCount = imageCount,
                ImageFormat = selectedFormat.Format,
                ImageColorSpace = selectedFormat.ColorSpace,
                ImageExtent = extent,
                ImageArrayLayers = 1,
                ImageUsage = imageUsage,
                ImageSharingMode = SharingMode.Exclusive,
                PreTransform = capabilities.CurrentTransform,
                CompositeAlpha = SelectCompositeAlpha(capabilities.SupportedCompositeAlpha),
                PresentMode = selectedPresentMode,
                Clipped = true,
                OldSwapchain = default,
            };

            Result createResult = swapchainCommands.CreateSwapchain(plant.Device, in createInfo, (AllocationCallbacks*)null, out SwapchainKHR swapchain);
            if (createResult != Result.Success)
            {
                swapchainCommands.Dispose();
                surfaceOwner.Dispose();
                return ResultWith(
                    VulkanPresentationStatus.Unavailable,
                    diagnostics,
                    VulkanPresentationDiagnosticCodes.SwapchainCreationFailed,
                    VulkanPresentationDiagnosticSeverity.Error,
                    $"vkCreateSwapchainKHR failed with result {createResult}.",
                    plant.Context.Id);
            }

            Result imagesResult = QuerySwapchainImages(swapchainCommands, plant, swapchain, out Image[] images);
            if (imagesResult != Result.Success || images.Length == 0)
            {
                swapchainCommands.DestroySwapchain(plant.Device, swapchain, (AllocationCallbacks*)null);
                swapchainCommands.Dispose();
                surfaceOwner.Dispose();
                return ResultWith(
                    VulkanPresentationStatus.Unavailable,
                    diagnostics,
                    VulkanPresentationDiagnosticCodes.SwapchainImageQueryFailed,
                    VulkanPresentationDiagnosticSeverity.Error,
                    $"vkGetSwapchainImagesKHR failed or returned no images. Vulkan result: {imagesResult}.",
                    plant.Context.Id);
            }

            if (!TryCreateImageViews(plant, images, selectedFormat.Format, diagnostics, out ImageView[] imageViews, out VulkanSwapchainCreateResult? imageViewFailure))
            {
                swapchainCommands.DestroySwapchain(plant.Device, swapchain, (AllocationCallbacks*)null);
                swapchainCommands.Dispose();
                surfaceOwner.Dispose();
                return imageViewFailure!;
            }

            if (!VulkanPresentationSemaphoreSet.TryCreate(plant, out semaphoreSet, out Result semaphoreResult))
            {
                foreach (ImageView imageView in imageViews)
                {
                    if (imageView.Handle != 0)
                    {
                        plant.Vk.DestroyImageView(plant.Device, imageView, (AllocationCallbacks*)null);
                    }
                }

                swapchainCommands.DestroySwapchain(plant.Device, swapchain, (AllocationCallbacks*)null);
                swapchainCommands.Dispose();
                surfaceOwner.Dispose();
                return ResultWith(
                    VulkanPresentationStatus.Unavailable,
                    diagnostics,
                    VulkanPresentationDiagnosticCodes.SemaphoreCreationFailed,
                    VulkanPresentationDiagnosticSeverity.Error,
                    $"vkCreateSemaphore failed for swapchain presentation synchronization with result {semaphoreResult}.",
                    plant.Context.Id);
            }

            VulkanSwapchainFacts facts = new(
                plant.Context.Id,
                extent.Width,
                extent.Height,
                selectedFormat.Format.ToString(),
                selectedFormat.ColorSpace.ToString(),
                selectedPresentMode.ToString(),
                (uint)images.Length,
                (uint)imageViews.Length,
                (uint)capabilities.CurrentTransform);
            swapchainOwner = new AurelianVulkanSwapchain(plant, swapchainCommands, swapchain, images, imageViews, semaphoreSet, facts);
            semaphoreSet = null;
            swapchainApi = null;

            return new VulkanSwapchainCreateResult(VulkanPresentationStatus.Created, surfaceOwner, swapchainOwner, diagnostics);
        }
        catch (Exception ex) when (IsWindowingUnavailableException(ex))
        {
            swapchainOwner?.Dispose();
            semaphoreSet?.Dispose();
            surfaceOwner?.Dispose();
            swapchainApi?.Dispose();
            surfaceApi?.Dispose();
            window?.Dispose();
            return ResultWith(
                VulkanPresentationStatus.Unavailable,
                diagnostics,
                VulkanPresentationDiagnosticCodes.HeadlessEnvironment,
                VulkanPresentationDiagnosticSeverity.Error,
                $"Window/surface creation is unavailable in this environment: {ex.GetType().Name}: {ex.Message}",
                plant.Context.Id);
        }
        catch (Exception ex)
        {
            swapchainOwner?.Dispose();
            semaphoreSet?.Dispose();
            surfaceOwner?.Dispose();
            swapchainApi?.Dispose();
            surfaceApi?.Dispose();
            window?.Dispose();
            return ResultWith(
                VulkanPresentationStatus.Failed,
                diagnostics,
                VulkanPresentationDiagnosticCodes.SwapchainCreationFailed,
                VulkanPresentationDiagnosticSeverity.Error,
                $"Unexpected Vulkan swapchain creation failure: {ex.GetType().Name}: {ex.Message}",
                plant.Context.Id);
        }
    }

    public static SurfaceFormatKHR SelectSurfaceFormat(IReadOnlyList<SurfaceFormatKHR> formats)
    {
        if (formats.Count == 1 && formats[0].Format == Format.Undefined)
        {
            return new SurfaceFormatKHR(Format.B8G8R8A8Srgb, ColorSpaceKHR.SpaceSrgbNonlinearKhr);
        }

        foreach (SurfaceFormatKHR format in formats)
        {
            if (format.Format == Format.B8G8R8A8Srgb && format.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
            {
                return format;
            }
        }

        foreach (SurfaceFormatKHR format in formats)
        {
            if (format.Format == Format.B8G8R8A8Unorm && format.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
            {
                return format;
            }
        }

        return formats[0];
    }

    public static PresentModeKHR SelectPresentMode(IReadOnlyList<PresentModeKHR> presentModes, bool vsync)
    {
        if (vsync)
        {
            return presentModes.Contains(PresentModeKHR.FifoKhr) ? PresentModeKHR.FifoKhr : presentModes[0];
        }

        if (presentModes.Contains(PresentModeKHR.MailboxKhr))
        {
            return PresentModeKHR.MailboxKhr;
        }

        return presentModes.Contains(PresentModeKHR.ImmediateKhr) ? PresentModeKHR.ImmediateKhr : presentModes[0];
    }

    private static IWindow CreateWindow(VulkanSwapchainCreateOptions options)
    {
        WindowOptions windowOptions = WindowOptions.DefaultVulkan;
        windowOptions.IsVisible = options.Visible;
        windowOptions.Size = new Vector2D<int>((int)Math.Clamp(options.Width, 1, int.MaxValue), (int)Math.Clamp(options.Height, 1, int.MaxValue));
        windowOptions.Title = options.Title;
        windowOptions.VSync = options.VSync;
        return Window.Create(windowOptions);
    }

    private static bool TryQuerySurfaceState(
        KhrSurface surfaceApi,
        AurelianVulkanPlant plant,
        SurfaceKHR surface,
        List<VulkanPresentationDiagnostic> diagnostics,
        out SurfaceCapabilitiesKHR capabilities,
        out SurfaceFormatKHR[] formats,
        out PresentModeKHR[] presentModes,
        out VulkanSwapchainCreateResult? failure)
    {
        failure = null;
        formats = [];
        presentModes = [];
        Result capabilitiesResult = surfaceApi.GetPhysicalDeviceSurfaceCapabilities(plant.PhysicalDevice, surface, out capabilities);
        if (capabilitiesResult != Result.Success)
        {
            failure = ResultWith(VulkanPresentationStatus.Unavailable, diagnostics, VulkanPresentationDiagnosticCodes.SwapchainCreationFailed, VulkanPresentationDiagnosticSeverity.Error, $"vkGetPhysicalDeviceSurfaceCapabilitiesKHR failed with result {capabilitiesResult}.", plant.Context.Id);
            return false;
        }

        uint formatCount = 0;
        Result formatCountResult = surfaceApi.GetPhysicalDeviceSurfaceFormats(plant.PhysicalDevice, surface, ref formatCount, (SurfaceFormatKHR*)null);
        if (formatCountResult != Result.Success || formatCount == 0)
        {
            failure = ResultWith(VulkanPresentationStatus.Rejected, diagnostics, VulkanPresentationDiagnosticCodes.NoSurfaceFormats, VulkanPresentationDiagnosticSeverity.Error, $"Surface reported no usable formats. Vulkan result: {formatCountResult}.", plant.Context.Id);
            return false;
        }

        formats = new SurfaceFormatKHR[formatCount];
        fixed (SurfaceFormatKHR* formatsPointer = formats)
        {
            formatCountResult = surfaceApi.GetPhysicalDeviceSurfaceFormats(plant.PhysicalDevice, surface, ref formatCount, formatsPointer);
        }

        uint presentModeCount = 0;
        Result presentModeCountResult = surfaceApi.GetPhysicalDeviceSurfacePresentModes(plant.PhysicalDevice, surface, ref presentModeCount, (PresentModeKHR*)null);
        if (formatCountResult != Result.Success || presentModeCountResult != Result.Success || presentModeCount == 0)
        {
            failure = ResultWith(VulkanPresentationStatus.Rejected, diagnostics, VulkanPresentationDiagnosticCodes.NoPresentModes, VulkanPresentationDiagnosticSeverity.Error, $"Surface reported no usable present modes. Format result: {formatCountResult}; present mode result: {presentModeCountResult}.", plant.Context.Id);
            return false;
        }

        presentModes = new PresentModeKHR[presentModeCount];
        fixed (PresentModeKHR* presentModesPointer = presentModes)
        {
            presentModeCountResult = surfaceApi.GetPhysicalDeviceSurfacePresentModes(plant.PhysicalDevice, surface, ref presentModeCount, presentModesPointer);
        }

        if (presentModeCountResult != Result.Success)
        {
            failure = ResultWith(VulkanPresentationStatus.Rejected, diagnostics, VulkanPresentationDiagnosticCodes.NoPresentModes, VulkanPresentationDiagnosticSeverity.Error, $"vkGetPhysicalDeviceSurfacePresentModesKHR failed with result {presentModeCountResult}.", plant.Context.Id);
            return false;
        }

        return true;
    }

    private static Extent2D ChooseExtent(SurfaceCapabilitiesKHR capabilities, uint width, uint height)
    {
        if (capabilities.CurrentExtent.Width != SpecialCurrentExtent)
        {
            return capabilities.CurrentExtent;
        }

        return new Extent2D(
            Math.Clamp(width, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width),
            Math.Clamp(height, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height));
    }

    private static uint ChooseImageCount(SurfaceCapabilitiesKHR capabilities)
    {
        uint desired = capabilities.MinImageCount + 1;
        return capabilities.MaxImageCount > 0 && desired > capabilities.MaxImageCount ? capabilities.MaxImageCount : desired;
    }

    private static CompositeAlphaFlagsKHR SelectCompositeAlpha(CompositeAlphaFlagsKHR supported)
    {
        CompositeAlphaFlagsKHR[] preference =
        [
            CompositeAlphaFlagsKHR.OpaqueBitKhr,
            CompositeAlphaFlagsKHR.PreMultipliedBitKhr,
            CompositeAlphaFlagsKHR.PostMultipliedBitKhr,
            CompositeAlphaFlagsKHR.InheritBitKhr,
        ];

        return preference.First(flag => (supported & flag) == flag);
    }

    private static Result QuerySwapchainImages(KhrSwapchain swapchainApi, AurelianVulkanPlant plant, SwapchainKHR swapchain, out Image[] images)
    {
        uint imageCount = 0;
        Result result = swapchainApi.GetSwapchainImages(plant.Device, swapchain, ref imageCount, (Image*)null);
        if (result != Result.Success || imageCount == 0)
        {
            images = [];
            return result;
        }

        images = new Image[imageCount];
        fixed (Image* imagesPointer = images)
        {
            return swapchainApi.GetSwapchainImages(plant.Device, swapchain, ref imageCount, imagesPointer);
        }
    }

    private static bool TryCreateImageViews(
        AurelianVulkanPlant plant,
        IReadOnlyList<Image> images,
        Format format,
        List<VulkanPresentationDiagnostic> diagnostics,
        out ImageView[] imageViews,
        out VulkanSwapchainCreateResult? failure)
    {
        imageViews = new ImageView[images.Count];
        failure = null;

        for (int i = 0; i < images.Count; i++)
        {
            ImageViewCreateInfo createInfo = new()
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = images[i],
                ViewType = ImageViewType.Type2D,
                Format = format,
                Components = new ComponentMapping(ComponentSwizzle.Identity, ComponentSwizzle.Identity, ComponentSwizzle.Identity, ComponentSwizzle.Identity),
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
            };

            Result result = plant.Vk.CreateImageView(plant.Device, in createInfo, (AllocationCallbacks*)null, out imageViews[i]);
            if (result == Result.Success)
            {
                continue;
            }

            for (int destroyIndex = 0; destroyIndex < i; destroyIndex++)
            {
                if (imageViews[destroyIndex].Handle != 0)
                {
                    plant.Vk.DestroyImageView(plant.Device, imageViews[destroyIndex], (AllocationCallbacks*)null);
                }
            }

            failure = ResultWith(VulkanPresentationStatus.Unavailable, diagnostics, VulkanPresentationDiagnosticCodes.ImageViewCreationFailed, VulkanPresentationDiagnosticSeverity.Error, $"vkCreateImageView failed for swapchain image {i} with result {result}.", plant.Context.Id);
            return false;
        }

        return true;
    }

    private static VulkanSwapchainCreateResult ResultWith(
        VulkanPresentationStatus status,
        List<VulkanPresentationDiagnostic> diagnostics,
        string code,
        VulkanPresentationDiagnosticSeverity severity,
        string message,
        Aurelian.Graphics.Plants.PlantId plantId)
    {
        diagnostics.Add(new VulkanPresentationDiagnostic(code, severity, message, plantId));
        return new VulkanSwapchainCreateResult(status, null, null, diagnostics.ToArray());
    }

    private static bool IsWindowingUnavailableException(Exception ex)
        => ex is PlatformNotSupportedException
            or DllNotFoundException
            or FileNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException
            or InvalidOperationException;
}
