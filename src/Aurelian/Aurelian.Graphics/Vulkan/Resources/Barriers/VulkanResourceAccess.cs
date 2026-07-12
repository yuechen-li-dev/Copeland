namespace Aurelian.Graphics.Vulkan.Resources.Barriers;

[Flags]
public enum VulkanResourceAccess
{
    None = 0,
    TransferRead = 1 << 0,
    TransferWrite = 1 << 1,
    ShaderRead = 1 << 2,
    ShaderWrite = 1 << 3,
    ColorAttachmentRead = 1 << 4,
    ColorAttachmentWrite = 1 << 5,
    DepthStencilRead = 1 << 6,
    DepthStencilWrite = 1 << 7,
    HostRead = 1 << 8,
    HostWrite = 1 << 9,
    PresentRead = 1 << 10,
}
