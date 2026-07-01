using Aurelian.Graphics.Vulkan.Resources.Textures;

namespace Aurelian.Graphics.Vulkan.Resources.Uploads;

public sealed record VulkanTextureUploadRequest(
    AurelianVulkanTexture Destination,
    ReadOnlyMemory<byte> RgbaBytes,
    string DebugName = "");
