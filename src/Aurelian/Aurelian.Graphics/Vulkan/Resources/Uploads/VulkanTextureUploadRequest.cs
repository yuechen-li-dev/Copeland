using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Resources.Barriers;

namespace Aurelian.Graphics.Vulkan.Resources.Uploads;

public sealed record VulkanTextureUploadRequest(
    AurelianVulkanTexture Destination,
    ReadOnlyMemory<byte> RgbaBytes,
    string DebugName = "",
    VulkanResourceLayout FinalLayout = VulkanResourceLayout.ShaderResourceFragment);
