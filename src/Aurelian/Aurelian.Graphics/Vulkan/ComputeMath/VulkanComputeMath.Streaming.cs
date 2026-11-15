namespace Aurelian.Graphics.Vulkan.ComputeMath;

public sealed record StreamingLinearResult(int WeightUploads, int Dispatches, double? GpuMilliseconds,
    ulong WeightBufferBytes, ulong LargestStagingPayloadBytes)
{
    public int SubmissionDepth { get; init; } = 1;
    public int MaximumOutstanding { get; init; } = 1;
    public int MaximumInFlight { get; init; } = 1;
    public int CompletionWaits { get; init; }
    public double PreparationMilliseconds { get; init; }
    public bool Cooperative { get; init; }
}

public sealed unsafe partial class VulkanComputeMath
{
    /// <summary>Replays resident output-row tiles with exactly the streamed linear shader,
    /// dispatch shapes and ring semantics. All tiles remain caller owned; no weight copy is submitted.</summary>
    public StreamingLinearResult LinearResidentTiles(GpuTensor input, GpuTensor output, StreamingLinearPlan plan,
        IReadOnlyList<GpuTensor> weights, IComputeStreamController controller)
    {
        Validate(input, GpuTensorFormat.Float32);
        Validate(output, GpuTensorFormat.Float32);
        ComputeMathBatch.Require(weights.Count == plan.Tiles.Count && input.Columns == plan.InputWidth
            && output.Columns == plan.OutputWidth && input.Rows == output.Rows && !ReferenceEquals(input, output),
            "Resident affine tiles must match their explicit plan and independent activations.");
        foreach (var weight in weights)
        {
            Validate(weight, GpuTensorFormat.Float32);
            ComputeMathBatch.Require(weight.Rows == plan.TileRows && weight.Columns == plan.InputWidth + 1,
                "Resident affine weight tile shape mismatch.");
        }
        // Preserve dependencies when callers use the owner's queued-compute scope.
        queuedScope?.Flush();
        using var ring = new SubmissionRing(this, controller, new(plan.Tiles.Count, plan.DispatchesPerTile, 0));
        for (int index = 0; index < plan.Tiles.Count; index++)
        {
            _ = ring.Acquire();
            ring.Submit(CreateStreamingLinearBatch(input, weights[index], output, plan, plan.Tiles[index]));
        }
        ring.Drain();
        return new(0, ring.Dispatches, ring.GpuMilliseconds, weights.Aggregate(0UL, (bytes, weight) => bytes + weight.Buffer.SizeBytes), 0)
        {
            SubmissionDepth = ring.Depth, CompletionWaits = ring.CompletionWaits,
            MaximumInFlight = ring.MaximumInFlight, MaximumOutstanding = ring.MaximumOutstanding
        };
    }
    /// <summary>Executes a fixed weight schedule on the existing plant. An attached
    /// controller enables bounded lookahead; physical slots wait only before reuse
    /// or final drain. Otherwise uploads and dispatches complete synchronously.
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
            && input.Rows == output.Rows && input.Rows <= 1048560 && !ReferenceEquals(input, output),
            "Streaming linear input/output must match its schedule and use separate allocations.");
        ComputeMathBatch.Require(plan.DispatchesPerTile <= MaximumDispatches, "Weight tile exceeds the owner's dispatch budget.");
        if (StreamController is { } controller)
        {
            // This composite operation owns and drains its physical ring.
            queuedScope?.Flush();
            return LinearPipelined(input, output, plan, loadWeights, controller);
        }
        using var weights = CreateTensor(plan.TileRows, plan.InputWidth + 1);
        double elapsed = 0;
        bool hasTiming = true;
        int totalDispatches = 0;
        foreach (var tile in plan.Tiles)
        {
            Upload(weights, loadWeights(tile));
            var batch = CreateStreamingLinearBatch(input, weights, output, plan, tile);
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
            weights.Buffer.SizeBytes, weights.Buffer.SizeBytes)
        {
            CompletionWaits = plan.Tiles.Count * 2,
        };
    }

    private ComputeMathBatch CreateStreamingLinearBatch(GpuTensor input, GpuTensor weights,
        GpuTensor output, StreamingLinearPlan plan, LinearWeightTile tile)
    {
        var batch = CreateBatch();
        for (int offset = 0; offset < plan.InputWidth; offset += ComputeMathBatch.MatrixKChunk)
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
        return batch;
    }
}
