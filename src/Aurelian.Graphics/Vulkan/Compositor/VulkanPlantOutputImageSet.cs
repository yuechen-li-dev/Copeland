using Aurelian.Rendering.Contracts.Compositor;

namespace Aurelian.Graphics.Vulkan.Compositor;

public sealed class VulkanPlantOutputImageSet
{
    public VulkanPlantOutputImageSet(IReadOnlyList<VulkanPlantOutputImage> outputs)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        Outputs = outputs.ToArray();
    }

    public IReadOnlyList<VulkanPlantOutputImage> Outputs { get; }

    public bool TryGet(PlantOutputRef outputRef, out VulkanPlantOutputImage output)
    {
        output = Outputs.FirstOrDefault(candidate => candidate.Ref == outputRef)!;
        return output is not null;
    }

    internal bool TryGetByFrameAndImage(PlantOutputRef outputRef, out VulkanPlantOutputImage output)
    {
        output = Outputs.FirstOrDefault(candidate =>
            candidate.Ref.FrameId == outputRef.FrameId
            && string.Equals(candidate.Ref.ImageId, outputRef.ImageId, StringComparison.Ordinal))!;
        return output is not null;
    }
}
