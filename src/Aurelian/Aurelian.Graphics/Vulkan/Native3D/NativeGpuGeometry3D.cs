using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Resources.Buffers;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>Ready-to-read GPU geometry using the shared 40-byte solid vertex ABI.</summary>
public sealed record NativeGpuGeometry3D(AurelianVulkanBuffer Buffer, uint VertexCount)
{
    public void Validate(AurelianVulkanPlant plant)
    {
        if (Buffer.NativeBuffer.Handle == 0 || Buffer.PlantId != plant.Context.Id ||
            !Buffer.Usage.HasFlag(VulkanBufferUsage.Vertex) || VertexCount == 0 ||
            VertexCount > 1_000_000 || VertexCount % 3 != 0 || Buffer.SizeBytes < VertexCount * 40UL)
        {
            throw new InvalidDataException("GPU geometry needs a live same-plant vertex buffer and complete bounded triangles.");
        }
    }
}
