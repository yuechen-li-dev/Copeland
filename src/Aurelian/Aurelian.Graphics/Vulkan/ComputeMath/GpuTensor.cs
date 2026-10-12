using Aurelian.Graphics.Vulkan.Resources.Buffers;

namespace Aurelian.Graphics.Vulkan.ComputeMath;

public enum GpuTensorFormat
{
    Float32,
    Complex32,
    PackedFloat16,
    UInt32,
}

/// <summary>Caller-visible GPU-resident allocation; shape and representation are explicit.</summary>
public sealed class GpuTensor : IDisposable
{
    internal VulkanComputeMath Owner { get; }
    internal bool Disposed { get; private set; }
    public AurelianVulkanBuffer Buffer { get; }
    public int Rows { get; }
    public int Columns { get; }
    public int Elements => Rows * Columns;
    public GpuTensorFormat Format { get; }

    internal GpuTensor(VulkanComputeMath owner, AurelianVulkanBuffer buffer, int rows, int columns, GpuTensorFormat format)
    {
        Owner = owner;
        Buffer = buffer;
        Rows = rows;
        Columns = columns;
        Format = format;
    }

    public void Dispose()
    {
        if (Disposed)
        {
            return;
        }
        Disposed = true;
        Owner.Release(this);
    }
}
