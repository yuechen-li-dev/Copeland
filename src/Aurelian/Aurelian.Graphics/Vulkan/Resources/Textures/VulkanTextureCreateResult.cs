namespace Aurelian.Graphics.Vulkan.Resources.Textures;

public sealed record VulkanTextureCreateResult(
    VulkanTextureStatus Status,
    AurelianVulkanTexture? Texture,
    IReadOnlyList<VulkanTextureDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanTextureStatus.Created && Texture is not null;
}
