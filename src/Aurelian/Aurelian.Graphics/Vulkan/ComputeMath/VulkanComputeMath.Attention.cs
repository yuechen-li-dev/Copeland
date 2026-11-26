namespace Aurelian.Graphics.Vulkan.ComputeMath;

public sealed record TiledAttentionResult(int Submissions, int Dispatches, double? GpuMilliseconds,
    ulong ScratchBytes, int QueryTileRows)
{
    public int MaximumInFlight { get; init; } = 1;
    public int CompletionWaits { get; init; }
}

public sealed unsafe partial class VulkanComputeMath
{
    /// <summary>Noncausal multi-head attention over all keys, evaluated one query
    /// tile/head at a time. Score/probability scratch is O(tileRows * keys), never
    /// O(allQueries * keys * heads). Q/K/V use token-major interleaved heads.
    /// Intermediate scores/probabilities are FP32. Output rounding is explicit;
    /// the default preserves the TRELLIS BF16 boundary.</summary>
    public TiledAttentionResult AttentionTiled(GpuTensor query, GpuTensor key, GpuTensor value,
        GpuTensor output, int headWidth = 128, int queryTileRows = 16, bool roundOutput = true)
    {
        return AttentionTiled(query, key, value, output, headWidth, queryTileRows, roundOutput, batchAcrossHeads: false);
    }

    /// <summary>Batch across heads using the same dispatches and scratch, within
    /// the existing dispatch budget.</summary>
    public TiledAttentionResult AttentionTiled(GpuTensor query, GpuTensor key, GpuTensor value,
        GpuTensor output, int headWidth, int queryTileRows, bool roundOutput, bool batchAcrossHeads)
    {
        return AttentionTiled(query, key, value, output, headWidth, queryTileRows, roundOutput, batchAcrossHeads, null);
    }

    /// <summary>The caller can retain shape-matched scratch across serial calls on this owner.</summary>
    public TiledAttentionResult AttentionTiled(GpuTensor query, GpuTensor key, GpuTensor value,
        GpuTensor output, int headWidth, int queryTileRows, bool roundOutput, bool batchAcrossHeads,
        TiledAttentionWorkspace? workspace)
    {
        Validate(query, GpuTensorFormat.Float32);
        Validate(key, GpuTensorFormat.Float32);
        Validate(value, GpuTensorFormat.Float32);
        Validate(output, GpuTensorFormat.Float32);
        ComputeMathBatch.Require(headWidth is 64 or 128 && query.Columns % headWidth == 0
            && key.Columns == query.Columns && value.Rows == key.Rows && value.Columns == key.Columns
            && output.Rows == query.Rows && output.Columns == query.Columns && key.Rows <= 8192
            && queryTileRows is > 0 and <= 256
            && !ReferenceEquals(output, query) && !ReferenceEquals(output, key) && !ReferenceEquals(output, value),
            "Tiled attention needs 64/128-wide heads, compatible independent FP32 storage, <=8192 keys and query tiles of 1–256 rows.");
        int dispatchesPerQuery = 1 + (headWidth + ComputeMathBatch.MatrixKChunk - 1) / ComputeMathBatch.MatrixKChunk
            + 1 + (key.Rows + ComputeMathBatch.MatrixKChunk - 1) / ComputeMathBatch.MatrixKChunk + 1;
        ComputeMathBatch.Require(dispatchesPerQuery <= MaximumDispatches, "Attention tile exceeds the owner's dispatch budget.");
        int fullRows = Math.Min(queryTileRows, query.Rows);
        int tailRows = query.Rows % fullRows;
        using var temporary = workspace is null ? CreateTiledAttentionWorkspace(query.Rows, key.Rows, headWidth, queryTileRows) : null;
        var storage = workspace ?? temporary!;
        Validate(storage.Query, GpuTensorFormat.Float32);
        ComputeMathBatch.Require(storage.Query.Rows == fullRows && storage.Query.Columns == headWidth
            && storage.Key.Rows == key.Rows && (storage.TailQuery?.Rows ?? 0) == tailRows,
            "Attention workspace must match the tile rows, tail, key count and head width.");
        var k = storage.Key;
        var v = storage.Value;
        var q = storage.Query;
        var scores = storage.Scores;
        var probabilities = storage.Probabilities;
        var result = storage.Result;
        var tailQuery = storage.TailQuery;
        var tailScores = storage.TailScores;
        var tailProbabilities = storage.TailProbabilities;
        var tailResult = storage.TailResult;
        ulong scratch = q.Buffer.SizeBytes + k.Buffer.SizeBytes + v.Buffer.SizeBytes
            + scores.Buffer.SizeBytes + probabilities.Buffer.SizeBytes + result.Buffer.SizeBytes
            + (tailQuery?.Buffer.SizeBytes ?? 0) + (tailScores?.Buffer.SizeBytes ?? 0)
            + (tailProbabilities?.Buffer.SizeBytes ?? 0) + (tailResult?.Buffer.SizeBytes ?? 0);
        int dispatches = 0;
        int submissions = 0;
        double milliseconds = 0;
        bool timed = true;
        int queriesPerBatch = MaximumDispatches / dispatchesPerQuery;
        int queryTiles = (query.Rows + fullRows - 1) / fullRows;
        int firstBatchQueries = (MaximumDispatches - 2) / dispatchesPerQuery;
        int remainingQueries = Math.Max(0, queryTiles - firstBatchQueries);
        int batchesPerHead = 1 + (remainingQueries + queriesPerBatch - 1) / queriesPerBatch;
        if (StreamController is not null)
        {
            // Preserve the outer scope's ownership before this composite ring.
            queuedScope?.Flush();
        }
        using var ring = StreamController is { } controller
            ? new SubmissionRing(this, controller, new(batchesPerHead * (query.Columns / headWidth), MaximumDispatches, 0))
            : null;
        var batch = CreateBatch();
        for (int head = 0; head < query.Columns / headWidth; head++)
        {
            if (batch.DispatchCount + 2 > MaximumDispatches)
            {
                SubmitBatch();
            }
            batch.Gather(key, k, firstColumn: head * headWidth);
            batch.Gather(value, v, firstColumn: head * headWidth);
            for (int firstRow = 0; firstRow < query.Rows; firstRow += fullRows)
            {
                if (batch.DispatchCount + dispatchesPerQuery > MaximumDispatches)
                {
                    SubmitBatch();
                }
                bool tail = query.Rows - firstRow < fullRows;
                GpuTensor tileQuery = tail ? tailQuery! : q;
                GpuTensor tileScores = tail ? tailScores! : scores;
                GpuTensor tileProbabilities = tail ? tailProbabilities! : probabilities;
                GpuTensor tileResult = tail ? tailResult! : result;
                batch.Gather(query, tileQuery, firstRow, head * headWidth);
                batch.Multiply(tileQuery, k, tileScores, 1 / MathF.Sqrt(headWidth), transposeB: true);
                batch.ReduceRows(tileScores, tileProbabilities);
                batch.Multiply(tileProbabilities, v, tileResult);
                if (roundOutput)
                {
                    batch.ScatterRounded(tileResult, output, firstRow, head * headWidth);
                }
                else
                {
                    batch.Scatter(tileResult, output, firstRow, head * headWidth);
                }
            }
            if (!batchAcrossHeads)
            {
                SubmitBatch();
            }
        }
        if (batch.DispatchCount > 0)
        {
            SubmitBatch();
        }
        if (ring is not null)
        {
            ring.Drain();
            submissions = ring.Submissions;
            dispatches = ring.Dispatches;
            milliseconds = ring.GpuMilliseconds ?? 0;
            timed = ring.GpuMilliseconds is not null;
        }
        return new(submissions, dispatches, timed ? milliseconds : null, scratch, queryTileRows)
        {
            MaximumInFlight = ring?.MaximumInFlight ?? 1,
            CompletionWaits = ring?.CompletionWaits ?? submissions,
        };

        void SubmitBatch()
        {
            if (ring is not null)
            {
                ring.Submit(batch);
            }
            else
            {
                Record(Execute(batch));
            }
            batch = CreateBatch();
        }

        void Record(ComputeMathResult execution)
        {
            submissions++;
            dispatches += execution.Dispatches;
            if (execution.GpuMilliseconds is { } time)
            {
                milliseconds += time;
            }
            else
            {
                timed = false;
            }
        }
    }

    public TiledAttentionWorkspace CreateTiledAttentionWorkspace(int queryRows, int keyRows, int headWidth, int queryTileRows)
    {
        ComputeMathBatch.Require(queryRows > 0 && keyRows is > 0 and <= 8192
            && headWidth is 64 or 128 && queryTileRows is > 0 and <= 256,
            "Attention workspace needs positive bounded rows, tile rows and a 64/128-wide head.");
        int fullRows = Math.Min(queryRows, queryTileRows);
        return new(this, keyRows, headWidth, fullRows, queryRows % fullRows);
    }

    public sealed class TiledAttentionWorkspace : IDisposable
    {
        private readonly List<GpuTensor> tensors = [];
        internal GpuTensor Key { get; }
        internal GpuTensor Value { get; }
        internal GpuTensor Query { get; }
        internal GpuTensor Scores { get; }
        internal GpuTensor Probabilities { get; }
        internal GpuTensor Result { get; }
        internal GpuTensor? TailQuery { get; }
        internal GpuTensor? TailScores { get; }
        internal GpuTensor? TailProbabilities { get; }
        internal GpuTensor? TailResult { get; }

        internal TiledAttentionWorkspace(VulkanComputeMath owner, int keys, int width, int rows, int tailRows)
        {
            try
            {
                Key = Allocate(keys, width);
                Value = Allocate(keys, width);
                Query = Allocate(rows, width);
                Scores = Allocate(rows, keys);
                Probabilities = Allocate(rows, keys);
                Result = Allocate(rows, width);
                if (tailRows > 0)
                {
                    TailQuery = Allocate(tailRows, width);
                    TailScores = Allocate(tailRows, keys);
                    TailProbabilities = Allocate(tailRows, keys);
                    TailResult = Allocate(tailRows, width);
                }
            }
            catch
            {
                Dispose();
                throw;
            }

            GpuTensor Allocate(int tensorRows, int columns)
            {
                GpuTensor tensor = owner.CreateTensor(tensorRows, columns);
                tensors.Add(tensor);
                return tensor;
            }
        }

        public void Dispose()
        {
            for (int index = tensors.Count - 1; index >= 0; index--)
            {
                tensors[index].Dispose();
            }
            tensors.Clear();
        }
    }
}
