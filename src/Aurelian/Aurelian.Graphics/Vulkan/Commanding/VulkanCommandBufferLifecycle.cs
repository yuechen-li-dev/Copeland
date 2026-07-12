namespace Aurelian.Graphics.Vulkan.Commanding;

internal enum VulkanCommandBufferLifecycle
{
    Ready,
    Recording,
    Executable,
    Retired,
    Disposed,
}
