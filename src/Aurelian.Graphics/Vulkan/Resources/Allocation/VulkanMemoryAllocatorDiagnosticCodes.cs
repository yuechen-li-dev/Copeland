namespace Aurelian.Graphics.Vulkan.Resources.Allocation;

public static class VulkanMemoryAllocatorDiagnosticCodes
{
    public const string InvalidAllocationSize = "AGM1001";
    public const string InvalidMemoryTypeBits = "AGM1002";
    public const string PlantMismatch = "AGM1003";
    public const string NoSuitableMemoryType = "AGM1004";
    public const string AllocationFailed = "AGM1005";
    public const string AllocatorDisposed = "AGM1006";
    public const string AllocationFreed = "AGM1007";
    public const string MappingNotSupportedForUsage = "AGM1008";
    public const string MapMemoryFailed = "AGM1009";
}
