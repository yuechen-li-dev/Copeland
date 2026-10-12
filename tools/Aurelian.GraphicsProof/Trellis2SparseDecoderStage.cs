using Aurelian.Assets.Tensors;
using Aurelian.Graphics.Vulkan.ComputeMath;

/// <summary>Actual first-resolution learned decoder execution and subdivision
/// prediction. It deliberately stops at the owning row budget before trying
/// to run unsupported larger-resolution decoder stages.</summary>
internal sealed class Trellis2SparseDecoderStage(VulkanComputeMath math, SafeTensorFile weights)
{
    public float[]? Features { get; private set; }
    public float[]? SubdivisionLogits { get; private set; }
    public List<object> Measurements { get; } = [];

    public IEnumerable<Trellis2ComputeStage> Decode(float[] latent, SparseVoxelTopology topology)
    {
        int rows = topology.Coordinates.Length;
        if (rows is < 1 or > 8192 || latent.Length != rows * 32 || !latent.All(float.IsFinite))
        {
            throw new InvalidDataException("AUR-TRELLIS-DECODER-001: First decoder stage needs finite N x 32 latent values and 1..8192 coordinate rows.");
        }
        using var neighbors = math.CreateTensor(rows, 27, GpuTensorFormat.UInt32);
        math.UploadUInt32(neighbors, topology.Neighbors27.Select(index => index < 0 ? uint.MaxValue : (uint)index).ToArray());
        using var input = math.CreateTensor(rows, 32);
        math.Upload(input, latent);
        GpuTensor? hidden = null;
        try
        {
            yield return new("shape.decoder.from_latent", () => hidden = Affine(input, "from_latent"));
            for (int block = 0; block < 4; block++)
            {
                int selected = block;
                yield return new($"shape.decoder.blocks.0.{selected}", () =>
                {
                    GpuTensor next = ConvNext(hidden!, neighbors, $"blocks.0.{selected}");
                    hidden!.Dispose();
                    hidden = next;
                });
            }
            yield return new("shape.decoder.subdivision", () =>
            {
                using var logits = Affine(hidden!, "blocks.0.4.to_subdiv");
                SubdivisionLogits = math.Read(logits);
                // Explicit qualification-only boundary, never a hidden operation
                // in the reusable sparse gather/matrix APIs.
                Features = math.Read(hidden!);
            });
        }
        finally
        {
            hidden?.Dispose();
        }
    }

    private GpuTensor ConvNext(GpuTensor input, GpuTensor neighbors, string prefix)
    {
        using var convolved = Convolve(input, neighbors, prefix + ".conv");
        using var gammaBeta = math.CreateTensor(1, input.Columns * 2);
        math.Upload(gammaBeta, [.. weights.ReadVector(prefix + ".norm.weight"), .. weights.ReadVector(prefix + ".norm.bias")]);
        Execute(batch =>
        {
            batch.Normalize(convolved, gammaBeta, convolved, Normalization.LayerNorm, epsilon: 1e-6f);
            batch.RoundFloat16(convolved, convolved);
        });
        using var expanded = Affine(convolved, prefix + ".mlp.0");
        Execute(batch =>
        {
            batch.Activate(expanded, expanded, TensorActivation.Silu);
            batch.RoundFloat16(expanded, expanded);
        });
        var output = Affine(expanded, prefix + ".mlp.2");
        Execute(batch =>
        {
            batch.Activate(input, output, TensorActivation.Residual, output);
            batch.RoundFloat16(output, output);
        });
        return output;
    }

    private GpuTensor Affine(GpuTensor input, string prefix)
    {
        var graph = new Trellis2BlockGraph(math, weights);
        GpuTensor result = graph.Affine(input, prefix, rounded: false);
        Execute(batch => batch.RoundFloat16(result, result));
        return result;
    }

    private GpuTensor Convolve(GpuTensor input, GpuTensor neighbors, string prefix)
    {
        var shape = weights.Tensors[prefix + ".weight"].Shape;
        if (shape.Count != 5 || shape[0] != input.Columns || shape[1] != 3 || shape[2] != 3
            || shape[3] != 3 || shape[4] != input.Columns)
        {
            throw new InvalidDataException("Sparse ConvNeXt checkpoint layout mismatch: " + prefix);
        }
        int channels = input.Columns;
        int inner = channels * 27;
        int weightTile = Math.Min(32, channels);
        var plan = new StreamingLinearPlan(inner, channels, weightTile);
        float[] bias = weights.ReadVector(prefix + ".bias");
        var output = math.CreateTensor(input.Rows, channels);
        double? gpuMilliseconds = 0;
        int dispatches = 0;
        try
        {
            for (int first = 0; first < input.Rows; first += 2048)
            {
                int rows = Math.Min(2048, input.Rows - first);
                using var columns = math.CreateTensor(rows, inner);
                using var tileOutput = math.CreateTensor(rows, channels);
                Execute(batch => batch.SparseColumns(input, neighbors, columns, first));
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
                gpuMilliseconds += result.GpuMilliseconds;
                dispatches += result.Dispatches;
                Execute(batch =>
                {
                    batch.RoundFloat16(tileOutput, tileOutput);
                    batch.CopyElements(tileOutput, output, first * channels);
                });
            }
            Measurements.Add(new { Operation = prefix, Dispatches = dispatches, LinearGpuMilliseconds = gpuMilliseconds });
            Console.WriteLine($"{prefix}: {input.Rows} x {channels}, {gpuMilliseconds:F1} ms matrix GPU");
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    private void Execute(Action<ComputeMathBatch> write)
    {
        var batch = math.CreateBatch();
        write(batch);
        math.Execute(batch);
    }
}
