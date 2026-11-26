namespace Aurelian.Graphics.Vulkan.ComputeMath;

/// <summary>One contiguous host payload copied into complete device tensors in order.
/// Keep the payload unchanged while submitting the plan. The destinations remain caller-owned.</summary>
public sealed class ComputeBatchUpload
{
    internal GpuTensor[] Destinations { get; }
    internal ReadOnlyMemory<byte> Payload { get; }

    public ComputeBatchUpload(IEnumerable<GpuTensor> destinations, ReadOnlyMemory<byte> payload)
    {
        Destinations = destinations.ToArray();
        ComputeMathBatch.Require(Destinations.Length > 0 && Destinations.Distinct().Count() == Destinations.Length,
            "A batch upload needs distinct destination tensors.");
        long length = 0;
        foreach (GpuTensor tensor in Destinations)
        {
            length = checked(length + (long)tensor.Buffer.SizeBytes);
        }
        ComputeMathBatch.Require(length == payload.Length, "The payload must fill every destination.");
        Payload = payload;
    }
}
