using Aurelian.Assets.Tensors;
using Aurelian.Graphics.Vulkan.ComputeMath;

/// <summary>The pinned dense occupancy VAE. All learned arithmetic stays on
/// Vulkan; CPU work selects checkpoint tiles and receives the final logits.
/// Storage is voxel-major XYZ/channel, with explicit logical volume dimensions.</summary>
internal sealed class Trellis2OccupancyDecoder(VulkanComputeMath math, SafeTensorFile weights)
{
    public List<object> Measurements { get; } = [];
    public float[]? Result { get; private set; }

    public IEnumerable<Trellis2ComputeStage> Decode(float[] latent)
    {
        if (latent.Length != 4096 * 8 || !latent.All(float.IsFinite))
        {
            throw new InvalidDataException("Decoder requires finite 16x16x16x8 voxel-major latent values.");
        }
        using var input = Volume(16, 8);
        math.Upload(input, latent);
        GpuTensor? hidden = null;
        try
        {
            yield return new("decoder.input", () => hidden = Convolve(input, 16, 8, "input_layer", rounded: true));
            foreach (string prefix in new[] { "middle_block.0", "middle_block.1", "blocks.0", "blocks.1" })
            {
                yield return new("decoder." + prefix, () => Replace(Residual(hidden!, 16, 512, prefix)));
            }
            yield return new("decoder.blocks.2", () => Replace(Upsample(hidden!, 16, 512, 128, "blocks.2.conv")));
            foreach (string prefix in new[] { "blocks.3", "blocks.4" })
            {
                yield return new("decoder." + prefix, () => Replace(Residual(hidden!, 32, 128, prefix)));
            }
            yield return new("decoder.blocks.5", () => Replace(Upsample(hidden!, 32, 128, 32, "blocks.5.conv")));
            foreach (string prefix in new[] { "blocks.6", "blocks.7" })
            {
                yield return new("decoder." + prefix, () => Replace(Residual(hidden!, 64, 32, prefix)));
            }
            yield return new("decoder.output", () =>
            {
                // Upstream casts the torso back to the original FP32 input type
                // before its final ChannelLayerNorm/SiLU/Conv3D sequence.
                using var activated = NormalizeActivate(hidden!, 64, 32, "out_layer.0", rounded: false);
                using var output = Convolve(activated, 64, 32, "out_layer.2", rounded: false);
                Result = math.Read(output);
            });
        }
        finally
        {
            hidden?.Dispose();
        }

        void Replace(GpuTensor next)
        {
            hidden!.Dispose();
            hidden = next;
        }
    }

    private GpuTensor Residual(GpuTensor input, int resolution, int channels, string prefix)
    {
        using var first = NormalizeActivate(input, resolution, channels, prefix + ".norm1", rounded: true);
        using var firstConv = Convolve(first, resolution, channels, prefix + ".conv1", rounded: true);
        using var second = NormalizeActivate(firstConv, resolution, channels, prefix + ".norm2", rounded: true);
        var output = Convolve(second, resolution, channels, prefix + ".conv2", rounded: true);
        Execute(batch =>
        {
            batch.Activate(input, output, TensorActivation.Residual, output);
            batch.RoundFloat16(output, output);
        });
        return output;
    }

    private GpuTensor Upsample(GpuTensor input, int resolution, int channels, int outputChannels, string prefix)
    {
        using var expanded = Convolve(input, resolution, channels, prefix, rounded: true);
        var output = Volume(resolution * 2, outputChannels);
        Execute(batch => batch.PixelShuffle3D(expanded, output, resolution, outputChannels));
        return output;
    }

    private GpuTensor NormalizeActivate(GpuTensor input, int resolution, int channels, string prefix, bool rounded)
    {
        using var gammaBeta = math.CreateTensor(1, channels * 2);
        math.Upload(gammaBeta, [.. weights.ReadVector(prefix + ".weight"), .. weights.ReadVector(prefix + ".bias")]);
        var output = Volume(resolution, channels);
        Execute(batch =>
        {
            // nn.LayerNorm's default epsilon, rather than the flow's 1e-6.
            batch.NormalizeChannels(input, gammaBeta, output, channels, epsilon: 1e-5f);
            if (rounded)
            {
                batch.RoundFloat16(output, output);
            }
            batch.Activate(output, output, TensorActivation.Silu);
            if (rounded)
            {
                batch.RoundFloat16(output, output);
            }
        });
        return output;
    }

    private GpuTensor Convolve(GpuTensor input, int resolution, int channels, string prefix, bool rounded)
    {
        var shape = weights.Tensors[prefix + ".weight"].Shape;
        if (shape.Count != 5 || shape[1] != channels || shape[2] != 3 || shape[3] != 3 || shape[4] != 3)
        {
            throw new InvalidDataException("Checkpoint Conv3D shape mismatch: " + prefix);
        }
        int outputChannels = shape[0];
        int voxels = resolution * resolution * resolution;
        int inner = channels * 27;
        int weightTile = Math.Min(outputChannels, inner > 8192 ? 64 : 128);
        var plan = new StreamingLinearPlan(inner, outputChannels, weightTile);
        float[] bias = weights.ReadVector(prefix + ".bias");
        var output = Volume(resolution, outputChannels);
        double? milliseconds = 0;
        int dispatches = 0;
        try
        {
            for (int firstVoxel = 0; firstVoxel < voxels; firstVoxel += 4096)
            {
                int count = Math.Min(4096, voxels - firstVoxel);
                using var columns = math.CreateTensor(count, inner);
                using var tileOutput = math.CreateTensor(count, outputChannels);
                Execute(batch => batch.VolumeColumns(input, columns, resolution, channels, firstVoxel));
                var result = math.LinearStreamed(columns, tileOutput, plan, tile =>
                {
                    float[] selected = weights.ReadSlices(prefix + ".weight", tile.FirstOutput, tile.OutputCount);
                    var packed = new float[plan.TileRows * (inner + 1)];
                    for (int row = 0; row < tile.OutputCount; row++)
                    {
                        Array.Copy(selected, row * inner, packed, row * (inner + 1), inner);
                        packed[row * (inner + 1) + inner] = bias[tile.FirstOutput + row];
                    }
                    return packed;
                });
                milliseconds += result.GpuMilliseconds;
                dispatches += result.Dispatches;
                Execute(batch =>
                {
                    if (rounded)
                    {
                        batch.RoundFloat16(tileOutput, tileOutput);
                    }
                    batch.CopyElements(tileOutput, output, firstVoxel * outputChannels);
                });
            }
            Measurements.Add(new { Operation = prefix, Resolution = resolution, channels, outputChannels,
                Dispatches = dispatches, LinearGpuMilliseconds = milliseconds });
            Console.WriteLine($"{prefix}: {resolution}^3, {channels}->{outputChannels}, {milliseconds:F2} ms matrix GPU");
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    private GpuTensor Volume(int resolution, int channels)
    {
        int voxels = resolution * resolution * resolution;
        int rows = Math.Min(voxels, 4096);
        return math.CreateTensor(rows, voxels / rows * channels);
    }

    private void Execute(Action<ComputeMathBatch> write)
    {
        var batch = math.CreateBatch();
        write(batch);
        math.Execute(batch);
    }
}
