using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.ComputeMath;

namespace Aurelian.WhisperBenchmark;

// The first streamed layer defines the reusable device slot. Each layer owns only
// its prepacked host image; queue order protects the slot until its last reader.
internal sealed class EncoderBlockUpload(List<GpuTensor> slot, int blockBytes)
{
    private readonly byte[] payload = new byte[blockBytes];
    private int offset;
    private int index;

    public GpuTensor Add(int rows, int columns, GpuTensorFormat format, float[] values,
        Func<int, int, GpuTensorFormat, GpuTensor> allocate)
    {
        if (index == slot.Count)
        {
            slot.Add(allocate(rows, columns, format));
        }
        var tensor = slot[index++];
        if (tensor.Rows != rows || tensor.Columns != columns || tensor.Format != format || tensor.Elements != values.Length)
        {
            throw new InvalidDataException("Inconsistent encoder block layout.");
        }
        int size = checked((int)tensor.Buffer.SizeBytes);
        Span<byte> destination = payload.AsSpan(offset, size);
        if (format == GpuTensorFormat.PackedFloat16)
        {
            var halves = MemoryMarshal.Cast<byte, Half>(destination);
            for (int i = 0; i < values.Length; i++)
            {
                if (!float.IsFinite(values[i]) || Math.Abs(values[i]) > 65504)
                {
                    throw new InvalidDataException("Invalid FP16 encoder weight.");
                }
                halves[i] = (Half)values[i];
            }
        }
        else
        {
            if (!Array.TrueForAll(values, float.IsFinite))
            {
                throw new InvalidDataException("Invalid encoder weight.");
            }
            MemoryMarshal.AsBytes(values.AsSpan()).CopyTo(destination);
        }
        offset += size;
        return tensor;
    }

    public ComputeBatchUpload Finish()
    {
        if (offset != payload.Length || index != slot.Count)
        {
            throw new InvalidDataException("Encoder block weight footprint differs from its budget.");
        }
        return new(slot, payload);
    }
}
