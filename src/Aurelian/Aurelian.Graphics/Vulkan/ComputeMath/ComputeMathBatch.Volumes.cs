namespace Aurelian.Graphics.Vulkan.ComputeMath;

public sealed partial class ComputeMathBatch
{
    public void CopyElements(GpuTensor input, GpuTensor output, int destinationOffset = 0, int sourceOffset = 0, int? count = null)
    {
        Check(input, output);
        int elements = count ?? input.Elements;
        Require(elements > 0 && sourceOffset >= 0 && (long)sourceOffset + elements <= input.Elements
            && destinationOffset >= 0 && (long)destinationOffset + elements <= output.Elements
            && !ReferenceEquals(input, output), "Flat copies require independent buffers and a valid destination range.");
        VolumeMap(input, output, new()
        {
            Rows = 1, Columns = (uint)elements, Mode = 3, Offset = (uint)destinationOffset, Padding = (uint)sourceOffset,
        });
    }

    public void RoundFloat16(GpuTensor input, GpuTensor output)
    {
        Check(input, output);
        Require(SameShape(input, output), "FP16 rounding preserves the flat FP32 storage shape.");
        VolumeMap(input, output, new() { Rows = (uint)input.Rows, Columns = (uint)input.Columns });
    }

    /// <summary>Convert a contiguous range of voxel-major channels to rows whose
    /// inner axes follow checkpoint OIDHW: channel, kernel X, Y, Z.</summary>
    public void VolumeColumns(GpuTensor input, GpuTensor output, int resolution, int channels,
        int firstVoxel = 0, int kernel = 3)
    {
        Check(input, output);
        Require(resolution is > 0 and <= 64 && channels is > 0 and <= 512 && kernel is 1 or 3
            && input.Elements == (long)resolution * resolution * resolution * channels
            && output.Columns == channels * kernel * kernel * kernel && firstVoxel >= 0
            && (long)firstVoxel + output.Rows <= (long)resolution * resolution * resolution
            && !ReferenceEquals(input, output), "Volume im2col requires bounded cubic voxel/channel storage and an independent valid voxel range.");
        VolumeMap(input, output, new()
        {
            Rows = (uint)output.Rows, Columns = (uint)output.Columns, Mode = 1,
            Width = (uint)resolution, Inner = (uint)channels, Offset = (uint)firstVoxel,
            Height = (uint)kernel, Padding = (uint)(kernel / 2),
        });
    }

    public void PixelShuffle3D(GpuTensor input, GpuTensor output, int resolution, int channels)
    {
        Check(input, output);
        Require(resolution is > 0 and <= 32 && channels is > 0 and <= 128
            && input.Elements == (long)resolution * resolution * resolution * channels * 8
            && output.Elements == input.Elements && !ReferenceEquals(input, output),
            "3D pixel shuffle requires 2x upsampling and independent voxel-major storage with eight subchannels per output channel.");
        VolumeMap(input, output, new()
        {
            Rows = (uint)output.Rows, Columns = (uint)output.Columns, Mode = 2,
            Width = (uint)resolution, Inner = (uint)channels,
        });
    }

    public void NormalizeChannels(GpuTensor input, GpuTensor gammaBeta, GpuTensor output,
        int channels, float epsilon = 1e-6f)
    {
        Check(input, gammaBeta, output);
        Require(channels is > 0 and <= 512 && input.Elements % channels == 0
            && SameShape(input, output) && gammaBeta.Elements == channels * 2
            && !ReferenceEquals(gammaBeta, output) && float.IsFinite(epsilon) && epsilon > 0,
            "Channel LayerNorm requires bounded voxel-major channels and one gamma/beta per channel.");
        int voxels = input.Elements / channels;
        uint groupsX = (uint)Math.Min(voxels, 4096);
        Add([new("Normalization.v.ts", input, gammaBeta, output, new()
        {
            Rows = (uint)voxels, Columns = (uint)channels, Mode = 1, Epsilon = epsilon, Height = groupsX,
        }, groupsX, ((uint)voxels + groupsX - 1) / groupsX)]);
    }

    private void VolumeMap(GpuTensor input, GpuTensor output, KernelParameters parameters)
    {
        uint groups = Groups((int)(parameters.Rows * parameters.Columns), 64);
        uint groupsX = Math.Min(groups, 65535);
        parameters.Stride = groupsX * 64;
        Add([new("VolumeMap.v.ts", input, input, output, parameters, groupsX, (groups + 65534) / 65535)]);
    }
}
