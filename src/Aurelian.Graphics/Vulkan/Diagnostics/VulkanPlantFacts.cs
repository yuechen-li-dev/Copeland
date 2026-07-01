using Aurelian.Graphics.Plants;

namespace Aurelian.Graphics.Vulkan.Diagnostics;

public sealed record VulkanPlantFacts(
    PlantId PlantId,
    string PhysicalDeviceName,
    string DeviceType,
    uint ApiVersion,
    uint DriverVersion,
    uint VendorId,
    uint DeviceId,
    uint QueueFamilyIndex,
    bool TimelineSemaphores,
    IReadOnlyList<string> EnabledInstanceExtensions,
    IReadOnlyList<string> EnabledDeviceExtensions,
    IReadOnlyList<string> EnabledValidationLayers);
