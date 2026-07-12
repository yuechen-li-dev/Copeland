using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Rendering.Contracts.Compositor;

namespace Aurelian.Graphics.Vulkan.Compositor;

/// <summary>
/// Non-owning wrapper for an offscreen Vulkan texture that can be consumed as a compositor source.
/// </summary>
/// <remarks>
/// The wrapper never owns or disposes the texture, image, image view, or backing memory. It only
/// validates that the texture is still live, belongs to the referenced plant, and can be used as a
/// transfer source for the A53 passthrough copy mechanism.
/// </remarks>
public sealed class VulkanPlantOutputImage
{
    public VulkanPlantOutputImage(PlantOutputRef outputRef, AurelianVulkanTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (texture.IsDisposed)
        {
            throw new ObjectDisposedException(nameof(AurelianVulkanTexture), "Cannot wrap a disposed texture as a Vulkan plant output image.");
        }

        if ((texture.Usage & VulkanTextureUsage.TransferSource) == 0)
        {
            throw new ArgumentException("A Vulkan plant output texture must include TransferSource usage for passthrough copy M0.", nameof(texture));
        }

        if (texture.PlantId.Value != outputRef.PlantId)
        {
            throw new ArgumentException(
                $"Plant output ref plant {outputRef.PlantId} does not match texture plant {texture.PlantId.Value}.",
                nameof(outputRef));
        }

        Ref = outputRef;
        PlantId = texture.PlantId;
        Width = texture.Width;
        Height = texture.Height;
        Format = MapFormat(texture.Format);
        Texture = texture;
    }

    public PlantOutputRef Ref { get; }

    public PlantId PlantId { get; }

    public uint Width { get; }

    public uint Height { get; }

    public string Format { get; }

    public AurelianVulkanTexture Texture { get; }

    private static string MapFormat(VulkanTextureFormat format)
        => format switch
        {
            VulkanTextureFormat.Rgba8Unorm => "R8G8B8A8Unorm",
            VulkanTextureFormat.Bgra8Unorm => "B8G8R8A8Unorm",
            VulkanTextureFormat.Rgba8Srgb => "R8G8B8A8Srgb",
            VulkanTextureFormat.Bgra8Srgb => "B8G8R8A8Srgb",
            _ => format.ToString(),
        };
}
