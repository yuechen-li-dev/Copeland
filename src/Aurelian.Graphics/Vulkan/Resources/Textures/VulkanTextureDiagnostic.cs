using Aurelian.Graphics.Plants;

namespace Aurelian.Graphics.Vulkan.Resources.Textures;

public enum VulkanTextureDiagnosticSeverity
{
    Error,
    Warning,
    Info,
}

public sealed record VulkanTextureDiagnostic(
    string Code,
    VulkanTextureDiagnosticSeverity Severity,
    string Message,
    PlantId PlantId,
    string? DebugName = null);
