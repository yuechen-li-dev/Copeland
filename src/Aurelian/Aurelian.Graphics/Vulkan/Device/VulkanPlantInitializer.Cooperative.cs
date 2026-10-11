using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Device;

public static unsafe partial class VulkanPlantInitializer
{
    // Adapted from Prometheus M40a. Capability absence preserves normal device
    // creation. Admit only the audited tuple, without vendor-name guesses.
    private static bool QueryCooperativeMatrices(Vk vk, Instance instance, PhysicalDevice physicalDevice,
        uint apiVersion, out string diagnostic)
    {
        diagnostic = "RequiresVulkan13AndKHRCooperativeMatrix";
        if (apiVersion < Vk.Version13 || !IsDeviceExtensionAvailable(vk, physicalDevice, "VK_KHR_cooperative_matrix"))
        {
            return false;
        }
        PhysicalDeviceCooperativeMatrixFeaturesKHR cooperative = new()
        {
            SType = StructureType.PhysicalDeviceCooperativeMatrixFeaturesKhr,
        };
        PhysicalDeviceShaderFloat16Int8Features half = new()
        {
            SType = StructureType.PhysicalDeviceShaderFloat16Int8Features, PNext = &cooperative,
        };
        PhysicalDeviceVulkanMemoryModelFeatures memory = new()
        {
            SType = StructureType.PhysicalDeviceVulkanMemoryModelFeatures, PNext = &half,
        };
        PhysicalDeviceSubgroupSizeControlFeatures control = new()
        {
            SType = StructureType.PhysicalDeviceSubgroupSizeControlFeatures, PNext = &memory,
        };
        PhysicalDeviceFeatures2 features = new() { SType = StructureType.PhysicalDeviceFeatures2, PNext = &control };
        vk.GetPhysicalDeviceFeatures2(physicalDevice, &features);
        PhysicalDeviceSubgroupProperties subgroup = new() { SType = StructureType.PhysicalDeviceSubgroupProperties };
        PhysicalDeviceProperties2 properties = new() { SType = StructureType.PhysicalDeviceProperties2, PNext = &subgroup };
        vk.GetPhysicalDeviceProperties2(physicalDevice, &properties);
        diagnostic = "RequiresCooperativeFloat16MemoryModelAndFull32LaneSubgroups";
        if (!cooperative.CooperativeMatrix || !half.ShaderFloat16 || !memory.VulkanMemoryModel
            || !control.ComputeFullSubgroups || subgroup.SubgroupSize != 32
            || (subgroup.SupportedStages & ShaderStageFlags.ComputeBit) == 0)
        {
            return false;
        }
        var query = (delegate* unmanaged<PhysicalDevice, uint*, CooperativeMatrixPropertiesKHR*, Result>)
            (nint)vk.GetInstanceProcAddr(instance, "vkGetPhysicalDeviceCooperativeMatrixPropertiesKHR");
        diagnostic = "AuditedCooperativeTupleUnavailable";
        if (query == null)
        {
            return false;
        }
        uint count = 0;
        if (query(physicalDevice, &count, null) != Result.Success || count == 0 || count > 4096)
        {
            return false;
        }
        var tuples = new CooperativeMatrixPropertiesKHR[count];
        for (int index = 0; index < tuples.Length; index++)
        {
            tuples[index].SType = StructureType.CooperativeMatrixPropertiesKhr;
        }
        fixed (CooperativeMatrixPropertiesKHR* pointer = tuples)
        {
            if (query(physicalDevice, &count, pointer) != Result.Success)
            {
                return false;
            }
        }
        bool supported = tuples.Take((int)count).Any(tuple => tuple.MSize == 16 && tuple.NSize == 16 && tuple.KSize == 16
            && (int)tuple.AType == 0 && (int)tuple.BType == 0 && (int)tuple.CType == 1 && (int)tuple.ResultType == 1
            && (int)tuple.Scope == 3 && !tuple.SaturatingAccumulation);
        diagnostic = supported ? "F16F32M16N16K16Subgroup32" : "AuditedCooperativeTupleUnavailable";
        return supported;
    }
}
