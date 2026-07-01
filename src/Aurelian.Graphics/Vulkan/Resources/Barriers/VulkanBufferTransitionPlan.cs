namespace Aurelian.Graphics.Vulkan.Resources.Barriers;

public sealed record VulkanBufferTransitionPlan(
    string ResourceName,
    VulkanResourceAccess OldAccess,
    VulkanResourceAccess NewAccess,
    VulkanBarrierStage OldStages,
    VulkanBarrierStage NewStages,
    ulong Offset,
    ulong SizeBytes);

public static class VulkanBufferTransitionPlanner
{
    public static VulkanBufferTransitionPlan HostWriteToTransferRead(string resourceName, ulong sizeBytes)
        => new(
            resourceName,
            VulkanResourceAccess.HostWrite,
            VulkanResourceAccess.TransferRead,
            VulkanBarrierStage.Host,
            VulkanBarrierStage.Transfer,
            0,
            sizeBytes);

    public static VulkanBufferTransitionPlan TransferWriteToVertexRead(string resourceName, ulong sizeBytes)
        => new(
            resourceName,
            VulkanResourceAccess.TransferWrite,
            VulkanResourceAccess.ShaderRead,
            VulkanBarrierStage.Transfer,
            VulkanBarrierStage.VertexShader,
            0,
            sizeBytes);

    public static VulkanBufferTransitionPlan TransferWriteToShaderRead(string resourceName, ulong sizeBytes)
        => new(
            resourceName,
            VulkanResourceAccess.TransferWrite,
            VulkanResourceAccess.ShaderRead,
            VulkanBarrierStage.Transfer,
            VulkanBarrierStage.AllGraphics | VulkanBarrierStage.ComputeShader,
            0,
            sizeBytes);
}
