namespace Aurelian.Graphics.Vulkan.Resources.Barriers;

public sealed class VulkanLayoutTracker
{
    private readonly VulkanResourceLayout[] layouts;

    public VulkanLayoutTracker(uint mipLevels, uint arrayLayers, VulkanResourceLayout initialLayout)
    {
        if (mipLevels == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mipLevels), mipLevels, "Mip level count must be greater than zero.");
        }

        if (arrayLayers == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(arrayLayers), arrayLayers, "Array layer count must be greater than zero.");
        }

        MipLevels = mipLevels;
        ArrayLayers = arrayLayers;
        layouts = Enumerable.Repeat(initialLayout, checked((int)(mipLevels * arrayLayers))).ToArray();
    }

    public uint MipLevels { get; }

    public uint ArrayLayers { get; }

    public VulkanResourceLayout Get(uint mipLevel, uint arrayLayer)
        => layouts[ToIndexOrThrow(mipLevel, arrayLayer)];

    public VulkanBarrierPlanResult Transition(
        string resourceName,
        uint mipLevel,
        uint arrayLayer,
        VulkanResourceLayout newLayout)
    {
        if (!TryGetIndex(mipLevel, arrayLayer, out int index))
        {
            return InvalidSubresource(resourceName, mipLevel, arrayLayer);
        }

        VulkanResourceLayout oldLayout = layouts[index];
        if (oldLayout == newLayout)
        {
            return VulkanBarrierPlanResult.NoOp();
        }

        VulkanBarrierPlanResult oldMappingResult = VulkanBarrierMappings.Map(oldLayout);
        VulkanBarrierPlanResult newMappingResult = VulkanBarrierMappings.Map(newLayout);
        List<VulkanBarrierDiagnostic> diagnostics = [.. oldMappingResult.Diagnostics, .. newMappingResult.Diagnostics];
        if (!oldMappingResult.Success || !newMappingResult.Success || oldMappingResult.Mapping is null || newMappingResult.Mapping is null)
        {
            return VulkanBarrierPlanResult.Rejected(diagnostics);
        }

        VulkanBarrierPlan plan = new(
            resourceName,
            oldLayout,
            newLayout,
            oldMappingResult.Mapping,
            newMappingResult.Mapping,
            mipLevel,
            1,
            arrayLayer,
            1);

        layouts[index] = newLayout;
        return VulkanBarrierPlanResult.Planned(plan, diagnostics);
    }

    public bool TryMarkCurrentLayout(uint mipLevel, uint arrayLayer, VulkanResourceLayout layout)
    {
        if (!TryGetIndex(mipLevel, arrayLayer, out int index))
        {
            return false;
        }

        layouts[index] = layout;
        return true;
    }

    public VulkanBarrierBatch TransitionAll(string resourceName, VulkanResourceLayout newLayout)
    {
        List<VulkanBarrierPlan> plans = [];
        for (uint mip = 0; mip < MipLevels; mip++)
        {
            for (uint layer = 0; layer < ArrayLayers; layer++)
            {
                VulkanBarrierPlanResult result = Transition(resourceName, mip, layer, newLayout);
                if (result.Status == VulkanBarrierStatus.Planned && result.Plan is not null)
                {
                    plans.Add(result.Plan);
                }
            }
        }

        return plans.Count == 0 ? VulkanBarrierBatch.Empty : new VulkanBarrierBatch(plans);
    }

    private int ToIndexOrThrow(uint mipLevel, uint arrayLayer)
    {
        if (!TryGetIndex(mipLevel, arrayLayer, out int index))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mipLevel),
                $"Invalid Vulkan subresource mip {mipLevel}, array layer {arrayLayer}; tracker has {MipLevels} mip levels and {ArrayLayers} array layers.");
        }

        return index;
    }

    private bool TryGetIndex(uint mipLevel, uint arrayLayer, out int index)
    {
        if (mipLevel >= MipLevels || arrayLayer >= ArrayLayers)
        {
            index = -1;
            return false;
        }

        index = checked((int)(mipLevel * ArrayLayers + arrayLayer));
        return true;
    }

    private VulkanBarrierPlanResult InvalidSubresource(string resourceName, uint mipLevel, uint arrayLayer)
        => VulkanBarrierPlanResult.Rejected([
            new VulkanBarrierDiagnostic(
                VulkanBarrierDiagnosticCodes.InvalidSubresource,
                VulkanBarrierDiagnosticSeverity.Error,
                $"Invalid Vulkan subresource mip {mipLevel}, array layer {arrayLayer}; tracker has {MipLevels} mip levels and {ArrayLayers} array layers.",
                resourceName,
                mipLevel,
                arrayLayer),
        ]);
}
