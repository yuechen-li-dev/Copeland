namespace Aurelian.Graphics.Vulkan.ComputeMath;

public sealed record StreamingLinearResult(int WeightUploads, int Dispatches, double? GpuMilliseconds,
    ulong WeightBufferBytes, ulong LargestStagingPayloadBytes);

public sealed unsafe partial class VulkanComputeMath
{
    /// <summary>Executes a fixed weight schedule on the existing plant. Each upload
    /// and dispatch batch finishes before reusing the single weight allocation.
    /// Does not read activations back. The callback must return full-sized tile data,
    /// padded with zero rows for a partial final tile, with bias appended per row.</summary>
    public StreamingLinearResult LinearStreamed(GpuTensor input, GpuTensor output,
        StreamingLinearPlan plan, Func<LinearWeightTile, float[]> loadWeights)
    {
        Validate(input, GpuTensorFormat.Float32);
        Validate(output, GpuTensorFormat.Float32);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(loadWeights);
        ComputeMathBatch.Require(input.Columns == plan.InputWidth && output.Columns == plan.OutputWidth
            && input.Rows == output.Rows && !ReferenceEquals(input, output),
            "Streaming linear input/output must match its schedule and use separate allocations.");
        ComputeMathBatch.Require(plan.DispatchesPerTile <= MaximumDispatches, "Weight tile exceeds the owner's dispatch budget.");
        using var weights = CreateTensor(plan.TileRows, plan.InputWidth + 1);
        double elapsed = 0;
        bool hasTiming = true;
        int totalDispatches = 0;
        foreach (var tile in plan.Tiles)
        {
            Upload(weights, loadWeights(tile));
            var batch = CreateBatch();
            for (int offset = 0; offset < plan.InputWidth; offset += 16)
            {
                batch.Dispatches.Add(new("StreamingLinear.v.ts", input, weights, output, new()
                {
                    Rows = (uint)input.Rows,
                    Columns = (uint)tile.OutputCount,
                    Inner = (uint)plan.InputWidth,
                    Offset = (uint)offset,
                    Width = (uint)plan.OutputWidth,
                    Height = (uint)tile.FirstOutput,
                }, (uint)(input.Rows + 15) / 16, (uint)(tile.OutputCount + 15) / 16));
            }
            var result = Execute(batch);
            totalDispatches += result.Dispatches;
            if (result.GpuMilliseconds is { } milliseconds)
            {
                elapsed += milliseconds;
            }
            else
            {
                hasTiming = false;
            }
        }
        return new(plan.Tiles.Count, totalDispatches, hasTiming ? elapsed : null,
            weights.Buffer.SizeBytes, weights.Buffer.SizeBytes);
    }
}
