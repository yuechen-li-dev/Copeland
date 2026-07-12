using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Aurelian.Graphics.Vulkan.Resources.Textures;

namespace Aurelian.Integration.Tests.Support;

internal static class VulkanVisibleTriangleIntegrationFixture
{
    public static VulkanInitResult CreatePresentationPlant()
        => Aurelian.Graphics.Vulkan.Device.VulkanPlantInitializer.CreatePlant(
            PlantId.Zero,
            new Aurelian.Graphics.Vulkan.Device.VulkanPlantOptions(EnableValidation: false, EnablePresentation: true));

    public static bool TryMapTextureFormat(string swapchainFormat, out VulkanTextureFormat textureFormat)
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
}
