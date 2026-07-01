namespace Aurelian.Graphics.Vulkan.Resources.Textures;

[Flags]
public enum VulkanTextureUsage
{
    None = 0,
    ShaderResource = 1 << 0,
    ColorAttachment = 1 << 1,
    TransferSource = 1 << 2,
    TransferDestination = 1 << 3,
}
