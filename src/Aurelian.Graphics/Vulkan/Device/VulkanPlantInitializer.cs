using System.Runtime.InteropServices;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Silk.NET.Core;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;

namespace Aurelian.Graphics.Vulkan.Device;

public static unsafe class VulkanPlantInitializer
{
    private const string ValidationLayerName = "VK_LAYER_KHRONOS_validation";
    private const string DebugUtilsExtensionName = "VK_EXT_debug_utils";

    public static VulkanInitResult CreatePlant(
        PlantId plantId,
        VulkanPlantOptions? options = null)
    {
        options ??= new VulkanPlantOptions();
        List<VulkanInitDiagnostic> diagnostics = [];
        Vk? vk = null;
        Instance instance = default;
        Silk.NET.Vulkan.Device device = default;

        try
        {
            vk = Vk.GetApi();

            IReadOnlyList<string> availableLayers = EnumerateInstanceLayers(vk);
            IReadOnlyList<string> availableInstanceExtensions = EnumerateInstanceExtensions(vk);
            List<string> enabledLayers = [];
            List<string> enabledInstanceExtensions = [];

            if (options.EnablePresentation)
            {
                IReadOnlyList<string>? requiredSurfaceExtensions = TryGetRequiredPresentationInstanceExtensions(out string? presentationExtensionError);
                if (requiredSurfaceExtensions is null)
                {
                    vk.Dispose();
                    return ResultWith(
                        VulkanInitStatus.Unavailable,
                        diagnostics,
                        VulkanInitDiagnosticCodes.PresentationSurfaceExtensionsUnavailable,
                        VulkanInitDiagnosticSeverity.Error,
                        $"Vulkan presentation was requested, but Silk.NET.Windowing could not report required surface extensions: {presentationExtensionError}",
                        plantId);
                }

                foreach (string requiredExtension in requiredSurfaceExtensions)
                {
                    if (!availableInstanceExtensions.Contains(requiredExtension, StringComparer.Ordinal))
                    {
                        vk.Dispose();
                        return ResultWith(
                            VulkanInitStatus.Rejected,
                            diagnostics,
                            VulkanInitDiagnosticCodes.RequiredExtensionMissing,
                            VulkanInitDiagnosticSeverity.Error,
                            $"Vulkan presentation requires instance extension '{requiredExtension}', but it is not available.",
                            plantId);
                    }

                    enabledInstanceExtensions.Add(requiredExtension);
                }
            }

            if (options.EnableValidation)
            {
                if (availableLayers.Contains(ValidationLayerName, StringComparer.Ordinal))
                {
                    enabledLayers.Add(ValidationLayerName);
                }
                else
                {
                    diagnostics.Add(new VulkanInitDiagnostic(
                        VulkanInitDiagnosticCodes.ValidationLayerUnavailable,
                        VulkanInitDiagnosticSeverity.Warning,
                        "Vulkan validation was requested, but VK_LAYER_KHRONOS_validation is not available.",
                        plantId));
                }

                if (availableInstanceExtensions.Contains(DebugUtilsExtensionName, StringComparer.Ordinal))
                {
                    enabledInstanceExtensions.Add(DebugUtilsExtensionName);
                }
                else
                {
                    diagnostics.Add(new VulkanInitDiagnostic(
                        VulkanInitDiagnosticCodes.DebugUtilsUnavailable,
                        VulkanInitDiagnosticSeverity.Warning,
                        "Vulkan debug utils extension is not available; no debug utils extension was enabled.",
                        plantId));
                }
            }

            Result instanceResult = CreateInstance(vk, options, enabledLayers, enabledInstanceExtensions, out instance);
            if (instanceResult != Result.Success)
            {
                vk.Dispose();
                return ResultWith(
                    VulkanInitStatus.Unavailable,
                    diagnostics,
                    VulkanInitDiagnosticCodes.InstanceCreationFailed,
                    VulkanInitDiagnosticSeverity.Error,
                    $"Vulkan instance creation failed with result {instanceResult}.",
                    plantId);
            }

            var physicalDevices = EnumeratePhysicalDevices(vk, instance);
            if (physicalDevices.Count == 0)
            {
                vk.DestroyInstance(instance, (AllocationCallbacks*)null);
                vk.Dispose();
                return ResultWith(
                    VulkanInitStatus.Unavailable,
                    diagnostics,
                    VulkanInitDiagnosticCodes.NoPhysicalDevices,
                    VulkanInitDiagnosticSeverity.Error,
                    "No Vulkan physical devices were reported by the Vulkan runtime.",
                    plantId);
            }

            SelectedPhysicalDevice? selected = SelectPhysicalDevice(vk, physicalDevices, options.RequireTimelineSemaphores);
            if (selected is null)
            {
                vk.DestroyInstance(instance, (AllocationCallbacks*)null);
                vk.Dispose();
                return ResultWith(
                    VulkanInitStatus.Rejected,
                    diagnostics,
                    VulkanInitDiagnosticCodes.NoSuitableQueueFamily,
                    VulkanInitDiagnosticSeverity.Error,
                    "No Vulkan physical device has a queue family supporting graphics, compute, and transfer with the required features.",
                    plantId);
            }

            if (options.RequireTimelineSemaphores && !selected.TimelineSemaphores)
            {
                vk.DestroyInstance(instance, (AllocationCallbacks*)null);
                vk.Dispose();
                return ResultWith(
                    VulkanInitStatus.Rejected,
                    diagnostics,
                    VulkanInitDiagnosticCodes.TimelineSemaphoreUnsupported,
                    VulkanInitDiagnosticSeverity.Error,
                    "The selected Vulkan physical device does not support timeline semaphores.",
                    plantId);
            }

            List<string> enabledDeviceExtensions = [];
            if (options.EnablePresentation)
            {
                if (!IsDeviceExtensionAvailable(vk, selected.PhysicalDevice, KhrSwapchain.ExtensionName))
                {
                    vk.DestroyInstance(instance, (AllocationCallbacks*)null);
                    vk.Dispose();
                    return ResultWith(
                        VulkanInitStatus.Rejected,
                        diagnostics,
                        VulkanInitDiagnosticCodes.RequiredExtensionMissing,
                        VulkanInitDiagnosticSeverity.Error,
                        $"Vulkan presentation requires device extension '{KhrSwapchain.ExtensionName}', but it is not available on the selected physical device.",
                        plantId);
                }

                enabledDeviceExtensions.Add(KhrSwapchain.ExtensionName);
            }

            Result deviceResult = CreateLogicalDevice(vk, selected, options, enabledDeviceExtensions, out device);
            if (deviceResult != Result.Success)
            {
                vk.DestroyInstance(instance, (AllocationCallbacks*)null);
                vk.Dispose();
                return ResultWith(
                    VulkanInitStatus.Rejected,
                    diagnostics,
                    VulkanInitDiagnosticCodes.DeviceCreationFailed,
                    VulkanInitDiagnosticSeverity.Error,
                    $"Vulkan logical device creation failed with result {deviceResult}.",
                    plantId);
            }

            Queue queue = vk.GetDeviceQueue(device, selected.QueueFamilyIndex, queueIndex: 0);
            PlantContext context = new(
                plantId,
                PlantKind.Vulkan,
                GpuCapabilityTier.VulkanM0,
                selected.DeviceName,
                IsPresentationPlant: options.EnablePresentation);
            VulkanPlantFacts facts = new(
                plantId,
                selected.DeviceName,
                selected.DeviceType.ToString(),
                selected.ApiVersion,
                selected.DriverVersion,
                selected.VendorId,
                selected.DeviceId,
                selected.QueueFamilyIndex,
                selected.TimelineSemaphores,
                enabledInstanceExtensions.ToArray(),
                enabledDeviceExtensions.ToArray(),
                enabledLayers.ToArray());

            diagnostics.Add(new VulkanInitDiagnostic(
                VulkanInitDiagnosticCodes.DeviceSelected,
                VulkanInitDiagnosticSeverity.Info,
                $"Selected Vulkan physical device '{selected.DeviceName}' with queue family {selected.QueueFamilyIndex}.",
                plantId));

            var plant = new AurelianVulkanPlant(vk, instance, selected.PhysicalDevice, device, queue, selected.QueueFamilyIndex, context, facts);
            return new VulkanInitResult(VulkanInitStatus.Created, plant, facts, diagnostics);
        }
        catch (Exception ex) when (IsLoaderUnavailableException(ex))
        {
            device = DestroyDeviceIfNeeded(vk, device);
            DestroyInstanceIfNeeded(vk, instance);
            vk?.Dispose();
            return ResultWith(
                VulkanInitStatus.Unavailable,
                diagnostics,
                VulkanInitDiagnosticCodes.VulkanLoaderUnavailable,
                VulkanInitDiagnosticSeverity.Error,
                $"Vulkan loader or runtime is unavailable: {ex.GetType().Name}: {ex.Message}",
                plantId);
        }
        catch (Exception ex)
        {
            device = DestroyDeviceIfNeeded(vk, device);
            DestroyInstanceIfNeeded(vk, instance);
            vk?.Dispose();
            return ResultWith(
                VulkanInitStatus.Failed,
                diagnostics,
                VulkanInitDiagnosticCodes.DeviceCreationFailed,
                VulkanInitDiagnosticSeverity.Error,
                $"Unexpected Vulkan initialization failure: {ex.GetType().Name}: {ex.Message}",
                plantId);
        }
    }

    private static Result CreateInstance(
        Vk vk,
        VulkanPlantOptions options,
        IReadOnlyList<string> enabledLayers,
        IReadOnlyList<string> enabledExtensions,
        out Instance instance)
    {
        using var appName = SilkMarshal.StringToMemory(options.ApplicationName, NativeStringEncoding.UTF8);
        using var engineName = SilkMarshal.StringToMemory(options.EngineName, NativeStringEncoding.UTF8);
        using var layerNames = SilkMarshal.StringArrayToMemory(enabledLayers, NativeStringEncoding.UTF8);
        using var extensionNames = SilkMarshal.StringArrayToMemory(enabledExtensions, NativeStringEncoding.UTF8);

        ApplicationInfo applicationInfo = new()
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = (byte*)appName.Handle,
            ApplicationVersion = 1,
            PEngineName = (byte*)engineName.Handle,
            EngineVersion = 1,
            ApiVersion = GetRequestedApiVersion(vk),
        };

        InstanceCreateInfo createInfo = new()
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &applicationInfo,
            EnabledLayerCount = (uint)enabledLayers.Count,
            PpEnabledLayerNames = (byte**)layerNames.Handle,
            EnabledExtensionCount = (uint)enabledExtensions.Count,
            PpEnabledExtensionNames = (byte**)extensionNames.Handle,
        };

        return vk.CreateInstance(&createInfo, null, out instance);
    }

    private static uint GetRequestedApiVersion(Vk vk)
    {
        try
        {
            uint supportedVersion = Vk.Version10;
            return vk.EnumerateInstanceVersion(ref supportedVersion) == Result.Success && supportedVersion < Vk.Version12
                ? supportedVersion
                : Vk.Version12;
        }
        catch (EntryPointNotFoundException)
        {
            return Vk.Version10;
        }
    }

    private static Result CreateLogicalDevice(
        Vk vk,
        SelectedPhysicalDevice selected,
        VulkanPlantOptions options,
        IReadOnlyList<string> enabledDeviceExtensions,
        out Silk.NET.Vulkan.Device device)
    {
        float priority = 1.0f;
        DeviceQueueCreateInfo queueCreateInfo = new()
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = selected.QueueFamilyIndex,
            QueueCount = 1,
            PQueuePriorities = &priority,
        };

        PhysicalDeviceTimelineSemaphoreFeatures timelineFeatures = new()
        {
            SType = StructureType.PhysicalDeviceTimelineSemaphoreFeatures,
            TimelineSemaphore = options.RequireTimelineSemaphores && selected.TimelineSemaphores,
        };

        using var extensionNames = SilkMarshal.StringArrayToMemory(enabledDeviceExtensions, NativeStringEncoding.UTF8);
        DeviceCreateInfo createInfo = new()
        {
            SType = StructureType.DeviceCreateInfo,
            PNext = options.RequireTimelineSemaphores ? &timelineFeatures : null,
            QueueCreateInfoCount = 1,
            PQueueCreateInfos = &queueCreateInfo,
            EnabledExtensionCount = (uint)enabledDeviceExtensions.Count,
            PpEnabledExtensionNames = enabledDeviceExtensions.Count == 0 ? null : (byte**)extensionNames.Handle,
        };

        return vk.CreateDevice(selected.PhysicalDevice, &createInfo, null, out device);
    }


    private static IReadOnlyList<string>? TryGetRequiredPresentationInstanceExtensions(out string? error)
    {
        error = null;

        try
        {
            WindowOptions windowOptions = WindowOptions.DefaultVulkan;
            windowOptions.IsVisible = false;
            windowOptions.Size = new Vector2D<int>(64, 64);
            using IWindow window = Window.Create(windowOptions);
            IVkSurface? surface = window.VkSurface;
            if (surface is null)
            {
                error = "Silk.NET window did not expose a Vulkan surface source.";
                return null;
            }

            uint count = 0;
            byte** extensions = surface.GetRequiredExtensions(out count);
            List<string> names = [];
            for (int i = 0; i < count; i++)
            {
                string? name = SilkMarshal.PtrToString((nint)extensions[i], NativeStringEncoding.UTF8);
                if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name, StringComparer.Ordinal))
                {
                    names.Add(name);
                }
            }

            return names;
        }
        catch (Exception ex) when (IsWindowingUnavailableException(ex))
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return null;
        }
    }

    private static bool IsDeviceExtensionAvailable(Vk vk, PhysicalDevice physicalDevice, string extensionName)
    {
        uint count = 0;
        Result result = vk.EnumerateDeviceExtensionProperties(physicalDevice, (byte*)null, ref count, (ExtensionProperties*)null);
        if (result != Result.Success || count == 0)
        {
            return false;
        }

        ExtensionProperties[] properties = new ExtensionProperties[count];
        fixed (ExtensionProperties* propertiesPointer = properties)
        {
            result = vk.EnumerateDeviceExtensionProperties(physicalDevice, (byte*)null, ref count, propertiesPointer);
        }

        return result == Result.Success && properties.Any(property => string.Equals(FixedString(property.ExtensionName), extensionName, StringComparison.Ordinal));
    }

    private static bool IsWindowingUnavailableException(Exception ex)
        => ex is PlatformNotSupportedException
            or DllNotFoundException
            or FileNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException
            or InvalidOperationException;

    private static IReadOnlyList<string> EnumerateInstanceLayers(Vk vk)
    {
        uint count = 0;
        Result result = vk.EnumerateInstanceLayerProperties(ref count, (LayerProperties*)null);
        if (result != Result.Success || count == 0)
        {
            return [];
        }

        LayerProperties[] properties = new LayerProperties[count];
        fixed (LayerProperties* propertiesPointer = properties)
        {
            result = vk.EnumerateInstanceLayerProperties(ref count, propertiesPointer);
        }

        if (result != Result.Success)
        {
            return [];
        }

        List<string> names = [];
        foreach (LayerProperties property in properties)
        {
            string name = FixedString(property.LayerName);
            if (name.Length > 0)
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static IReadOnlyList<string> EnumerateInstanceExtensions(Vk vk)
    {
        uint count = 0;
        Result result = vk.EnumerateInstanceExtensionProperties((byte*)null, ref count, (ExtensionProperties*)null);
        if (result != Result.Success || count == 0)
        {
            return [];
        }

        ExtensionProperties[] properties = new ExtensionProperties[count];
        fixed (ExtensionProperties* propertiesPointer = properties)
        {
            result = vk.EnumerateInstanceExtensionProperties((byte*)null, ref count, propertiesPointer);
        }

        if (result != Result.Success)
        {
            return [];
        }

        List<string> names = [];
        foreach (ExtensionProperties property in properties)
        {
            string name = FixedString(property.ExtensionName);
            if (name.Length > 0)
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static IReadOnlyList<PhysicalDevice> EnumeratePhysicalDevices(Vk vk, Instance instance)
    {
        uint count = 0;
        Result result = vk.EnumeratePhysicalDevices(instance, ref count, (PhysicalDevice*)null);
        if (result != Result.Success || count == 0)
        {
            return [];
        }

        PhysicalDevice[] devices = new PhysicalDevice[count];
        fixed (PhysicalDevice* devicesPointer = devices)
        {
            result = vk.EnumeratePhysicalDevices(instance, ref count, devicesPointer);
        }

        return result == Result.Success ? devices : [];
    }

    private static SelectedPhysicalDevice? SelectPhysicalDevice(
        Vk vk,
        IReadOnlyList<PhysicalDevice> physicalDevices,
        bool requireTimelineSemaphores)
    {
        List<SelectedPhysicalDevice> candidates = [];
        for (int deviceIndex = 0; deviceIndex < physicalDevices.Count; deviceIndex++)
        {
            PhysicalDevice physicalDevice = physicalDevices[deviceIndex];
            PhysicalDeviceProperties properties = vk.GetPhysicalDeviceProperties(physicalDevice);
            uint? queueFamilyIndex = SelectQueueFamily(vk, physicalDevice);
            if (queueFamilyIndex is null)
            {
                continue;
            }

            bool timelineSemaphores = properties.ApiVersion >= Vk.Version12 && QueryTimelineSemaphores(vk, physicalDevice);
            if (requireTimelineSemaphores && !timelineSemaphores)
            {
                continue;
            }

            candidates.Add(new SelectedPhysicalDevice(
                physicalDevice,
                FixedString(properties.DeviceName),
                properties.DeviceType,
                properties.ApiVersion,
                properties.DriverVersion,
                properties.VendorID,
                properties.DeviceID,
                queueFamilyIndex.Value,
                timelineSemaphores,
                ScoreDeviceType(properties.DeviceType),
                deviceIndex));
        }

        return candidates
            .OrderByDescending(candidate => candidate.TypeScore)
            .ThenBy(candidate => candidate.DeviceName, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.OriginalIndex)
            .FirstOrDefault();
    }

    private static uint? SelectQueueFamily(Vk vk, PhysicalDevice physicalDevice)
    {
        uint count = 0;
        vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, ref count, (QueueFamilyProperties*)null);
        if (count == 0)
        {
            return null;
        }

        QueueFamilyProperties[] properties = new QueueFamilyProperties[count];
        fixed (QueueFamilyProperties* propertiesPointer = properties)
        {
            vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, ref count, propertiesPointer);
        }

        for (uint index = 0; index < properties.Length; index++)
        {
            QueueFamilyProperties family = properties[index];
            QueueFlags requiredFlags = QueueFlags.GraphicsBit | QueueFlags.ComputeBit | QueueFlags.TransferBit;
            if (family.QueueCount > 0 && (family.QueueFlags & requiredFlags) == requiredFlags)
            {
                return index;
            }
        }

        return null;
    }

    private static bool QueryTimelineSemaphores(Vk vk, PhysicalDevice physicalDevice)
    {
        PhysicalDeviceTimelineSemaphoreFeatures timelineFeatures = new()
        {
            SType = StructureType.PhysicalDeviceTimelineSemaphoreFeatures,
        };
        PhysicalDeviceFeatures2 features = new()
        {
            SType = StructureType.PhysicalDeviceFeatures2,
            PNext = &timelineFeatures,
        };

        vk.GetPhysicalDeviceFeatures2(physicalDevice, &features);
        return timelineFeatures.TimelineSemaphore;
    }

    private static int ScoreDeviceType(PhysicalDeviceType type)
        => type switch
        {
            PhysicalDeviceType.DiscreteGpu => 3,
            PhysicalDeviceType.IntegratedGpu => 2,
            _ => 1,
        };

    private static VulkanInitResult ResultWith(
        VulkanInitStatus status,
        List<VulkanInitDiagnostic> diagnostics,
        string code,
        VulkanInitDiagnosticSeverity severity,
        string message,
        PlantId plantId)
    {
        diagnostics.Add(new VulkanInitDiagnostic(code, severity, message, plantId));
        return new VulkanInitResult(status, null, null, diagnostics);
    }

    private static bool IsLoaderUnavailableException(Exception ex)
        => ex is DllNotFoundException
            or FileNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException
            or InvalidOperationException;

    private static Silk.NET.Vulkan.Device DestroyDeviceIfNeeded(Vk? vk, Silk.NET.Vulkan.Device device)
    {
        if (vk is not null && device.Handle != 0)
        {
            vk.DestroyDevice(device, (AllocationCallbacks*)null);
        }

        return default;
    }

    private static void DestroyInstanceIfNeeded(Vk? vk, Instance instance)
    {
        if (vk is not null && instance.Handle != 0)
        {
            vk.DestroyInstance(instance, (AllocationCallbacks*)null);
        }
    }

    private static string FixedString(byte* start)
        => SilkMarshal.PtrToString((nint)start, NativeStringEncoding.UTF8) ?? string.Empty;

    private sealed record SelectedPhysicalDevice(
        PhysicalDevice PhysicalDevice,
        string DeviceName,
        PhysicalDeviceType DeviceType,
        uint ApiVersion,
        uint DriverVersion,
        uint VendorId,
        uint DeviceId,
        uint QueueFamilyIndex,
        bool TimelineSemaphores,
        int TypeScore,
        int OriginalIndex);
}
