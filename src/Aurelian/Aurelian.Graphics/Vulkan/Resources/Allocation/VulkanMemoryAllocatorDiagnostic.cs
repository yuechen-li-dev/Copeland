using Aurelian.Graphics.Plants;

namespace Aurelian.Graphics.Vulkan.Resources.Allocation;

public sealed record VulkanMemoryAllocatorDiagnostic(
    string Code,
    VulkanMemoryAllocatorDiagnosticSeverity Severity,
    string Message,
    PlantId PlantId,
    string? DebugName = null);

public enum VulkanMemoryAllocatorDiagnosticSeverity
{
    Error,
    Warning,
    Info,
}
