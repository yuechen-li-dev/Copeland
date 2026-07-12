using Aurelian.Graphics.Plants;

namespace Aurelian.Graphics.Vulkan.Resources.Buffers;

public sealed record VulkanBufferWriteDiagnostic(
    string Code,
    VulkanBufferDiagnosticSeverity Severity,
    string Message,
    PlantId PlantId,
    string? DebugName = null);
