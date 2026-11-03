using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Uploads;
using Aurelian.Graphics.Vulkan.Sync;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>Retained device-local indexed geometry, uploaded once per immutable mesh.</summary>
internal sealed class GpuMesh3D : IDisposable
{
    public AurelianVulkanBuffer Vertices { get; private set; } = null!;
    public AurelianVulkanBuffer Indices { get; private set; } = null!;
    public uint VertexCount { get; }
    public uint IndexCount { get; }
    public ulong Bytes => Vertices.SizeBytes + Indices.SizeBytes;

    public GpuMesh3D(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator,
        VulkanCommandBufferPool commands, VulkanFenceBundle fences, NativeMesh3D mesh)
    {
        VertexCount = (uint)mesh.Vertices.Length;
        IndexCount = (uint)mesh.Indices.Length;
        try
        {
            Vertices = Upload(MemoryMarshal.AsBytes(mesh.Vertices.AsSpan()).ToArray(), VulkanBufferUsage.Vertex);
            Indices = Upload(MemoryMarshal.AsBytes(mesh.Indices.AsSpan()).ToArray(), VulkanBufferUsage.Index);
        }
        catch
        {
            Dispose();
            throw;
        }

        AurelianVulkanBuffer Upload(byte[] bytes, VulkanBufferUsage usage)
        {
            var created = VulkanBufferFactory.Create(plant, allocator, new(plant.Context.Id, (ulong)bytes.Length,
                usage | VulkanBufferUsage.TransferDestination, VulkanMemoryUsage.GpuOnly, mesh.Identity));
            if (!created.Success)
            {
                throw new InvalidOperationException("AUR-MESH-003: Device mesh allocation failed.");
            }
            var buffer = created.Buffer!;
            try
            {
                using var uploader = new VulkanBufferUploader(plant, allocator, commands, fences);
                var result = uploader.Upload(new(buffer, bytes, DebugName: mesh.Identity));
                if (!result.Success)
                {
                    throw new InvalidOperationException("AUR-MESH-004: Device mesh upload failed: "
                        + string.Join("; ", result.Diagnostics.Select(item => item.Message)));
                }
                return buffer;
            }
            catch
            {
                buffer.Dispose();
                throw;
            }
        }
    }

    public void Dispose()
    {
        Indices?.Dispose();
        Vertices?.Dispose();
    }
}
