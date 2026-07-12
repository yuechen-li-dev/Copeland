using Aurelian.Graphics.Plants;

namespace Aurelian.Graphics.Vulkan.Resources.Uploads;

public sealed record VulkanTextureUploadDiagnostic(
    string Code,
    string Message,
    PlantId PlantId,
    string? DebugName = null);
