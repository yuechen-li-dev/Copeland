namespace Aurelian.Graphics.Vulkan.Resources.Barriers;

[Flags]
public enum VulkanBarrierStage
{
    None = 0,
    Host = 1 << 0,
    Transfer = 1 << 1,
    VertexShader = 1 << 2,
    FragmentShader = 1 << 3,
    ComputeShader = 1 << 4,
    ColorAttachmentOutput = 1 << 5,
    EarlyFragmentTests = 1 << 6,
    LateFragmentTests = 1 << 7,
    BottomOfPipe = 1 << 8,
    AllGraphics = 1 << 9,
    AllCommands = 1 << 10,
}
