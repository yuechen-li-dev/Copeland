namespace Aurelian.Graphics.Vulkan.Resources.Barriers;

public enum VulkanResourceLayout
{
    Undefined,
    General,
    TransferSource,
    TransferDestination,
    ShaderResourceVertex,
    ShaderResourceFragment,
    ShaderResourceCompute,
    ShaderResourceAll,
    StorageReadWrite,
    ColorAttachment,
    DepthStencilAttachment,
    Present,
    CrossPlantTransferSource,
    CrossPlantTransferDestination,
}
