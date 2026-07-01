using Aurelian.Graphics.Vulkan.Resources.Buffers;

namespace Aurelian.Graphics.Vulkan.Resources.Uploads;

public sealed record VulkanBufferUploadRequest(
    AurelianVulkanBuffer Destination,
    ReadOnlyMemory<byte> Data,
    ulong DestinationOffset = 0,
    string DebugName = "");
