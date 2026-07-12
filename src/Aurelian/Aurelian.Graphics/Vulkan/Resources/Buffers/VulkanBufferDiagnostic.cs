using Aurelian.Graphics.Plants;

namespace Aurelian.Graphics.Vulkan.Resources.Buffers;

public sealed record VulkanBufferDiagnostic(
    string Code,
    VulkanBufferDiagnosticSeverity Severity,
    string Message,
    PlantId PlantId,
    string? DebugName = null);
