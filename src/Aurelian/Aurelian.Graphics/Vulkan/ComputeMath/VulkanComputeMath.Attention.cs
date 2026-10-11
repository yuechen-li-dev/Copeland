namespace Aurelian.Graphics.Vulkan.ComputeMath;

public sealed record TiledAttentionResult(int Submissions, int Dispatches, double? GpuMilliseconds,
    ulong ScratchBytes, int QueryTileRows);

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
        Validate(query, GpuTensorFormat.Float32);
        Validate(key, GpuTensorFormat.Float32);
        Validate(value, GpuTensorFormat.Float32);
        Validate(output, GpuTensorFormat.Float32);
        ComputeMathBatch.Require(headWidth is 64 or 128 && query.Columns % headWidth == 0
            && key.Columns == query.Columns && value.Rows == key.Rows && value.Columns == key.Columns
            && output.Rows == query.Rows && output.Columns == query.Columns && key.Rows <= 4096
            && queryTileRows is > 0 and <= 64
            && !ReferenceEquals(output, query) && !ReferenceEquals(output, key) && !ReferenceEquals(output, value),
            "Tiled attention needs 64/128-wide heads, compatible independent FP32 storage, <=4096 keys and query tiles of 1–64 rows.");
        int dispatchesPerQuery = 1 + (headWidth + 15) / 16 + 1 + (key.Rows + 15) / 16 + 1;
        ComputeMathBatch.Require(dispatchesPerQuery <= MaximumDispatches, "Attention tile exceeds the owner's dispatch budget.");
        using var k = CreateTensor(key.Rows, headWidth);
        using var v = CreateTensor(key.Rows, headWidth);
        int fullRows = Math.Min(queryTileRows, query.Rows);
        int tailRows = query.Rows % fullRows;
        using var q = CreateTensor(fullRows, headWidth);
        using var scores = CreateTensor(fullRows, key.Rows);
        using var probabilities = CreateTensor(fullRows, key.Rows);
        using var result = CreateTensor(fullRows, headWidth);
        using var tailQuery = tailRows == 0 ? null : CreateTensor(tailRows, headWidth);
        using var tailScores = tailRows == 0 ? null : CreateTensor(tailRows, key.Rows);
        using var tailProbabilities = tailRows == 0 ? null : CreateTensor(tailRows, key.Rows);
        using var tailResult = tailRows == 0 ? null : CreateTensor(tailRows, headWidth);
        ulong scratch = q.Buffer.SizeBytes + k.Buffer.SizeBytes + v.Buffer.SizeBytes
            + scores.Buffer.SizeBytes + probabilities.Buffer.SizeBytes + result.Buffer.SizeBytes
            + (tailQuery?.Buffer.SizeBytes ?? 0) + (tailScores?.Buffer.SizeBytes ?? 0)
            + (tailProbabilities?.Buffer.SizeBytes ?? 0) + (tailResult?.Buffer.SizeBytes ?? 0);
        int dispatches = 0;
        int submissions = 0;
        double milliseconds = 0;
        bool timed = true;
        for (int head = 0; head < query.Columns / headWidth; head++)
        {
            var keys = CreateBatch();
            keys.Gather(key, k, firstColumn: head * headWidth);
            keys.Gather(value, v, firstColumn: head * headWidth);
            Record(Execute(keys));
            for (int firstRow = 0; firstRow < query.Rows; firstRow += fullRows)
            {
                bool tail = query.Rows - firstRow < fullRows;
                GpuTensor tileQuery = tail ? tailQuery! : q;
                GpuTensor tileScores = tail ? tailScores! : scores;
                GpuTensor tileProbabilities = tail ? tailProbabilities! : probabilities;
                GpuTensor tileResult = tail ? tailResult! : result;
                var batch = CreateBatch();
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
                Record(Execute(batch));
            }
        }
        return new(submissions, dispatches, timed ? milliseconds : null, scratch, queryTileRows);

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
}
