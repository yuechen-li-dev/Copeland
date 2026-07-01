using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;

namespace Aurelian.Graphics.Vulkan.Resources.Textures;

public sealed record VulkanTextureCreatePlan(
    PlantId PlantId,
    uint Width,
    uint Height,
    VulkanTextureFormat Format,
    VulkanTextureUsage Usage,
    VulkanMemoryUsage MemoryUsage,
    VulkanResourceLayout InitialLayout,
    uint MipLevels = 1,
    uint ArrayLayers = 1,
    string DebugName = "");
