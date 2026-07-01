namespace Aurelian.Graphics.Vulkan.Device;

public sealed record VulkanPlantOptions(
    bool EnableValidation = true,
    bool RequireTimelineSemaphores = true,
    string ApplicationName = "Aurelian",
    string EngineName = "Aurelian",
    bool EnablePresentation = false);
