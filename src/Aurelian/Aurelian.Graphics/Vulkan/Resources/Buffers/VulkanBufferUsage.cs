namespace Aurelian.Graphics.Vulkan.Resources.Buffers;

[Flags]
public enum VulkanBufferUsage
{
    None = 0,
    Vertex = 1 << 0,
    Index = 1 << 1,
    Uniform = 1 << 2,
    Storage = 1 << 3,
    TransferSource = 1 << 4,
    TransferDestination = 1 << 5,
}
