using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Aurelian.Graphics.Vulkan.ComputeMath;

namespace Aurelian.Inference.Gemma4;

/// <summary>Single-token Q4B32 vocabulary projection on the host. Only the final normalized row crosses devices.</summary>
internal sealed class Gemma4CpuVocabularyHead
{
    private const int SliceRows = 8192;
    private const int BlocksPerRow = Gemma4TextModel.HiddenWidth / BlockQ4.ValuesPerBlock;
    private const int WordsPerRow = BlocksPerRow * BlockQ4.WordsPerBlock;
    internal const long WeightBytes = (long)WordsPerRow * sizeof(uint) * Gemma4TextModel.VocabularySize;
    private readonly uint[][] slices;
    private readonly float[] hidden = new float[Gemma4TextModel.HiddenWidth];
    private readonly Vector512<float>[] even = new Vector512<float>[BlocksPerRow];
    private readonly Vector512<float>[] odd = new Vector512<float>[BlocksPerRow];
    private readonly float[] scoreTable;
    private readonly (int Token, float Score)[] winners;
    private readonly ParallelOptions options;
    public double PreparationMilliseconds { get; }
    public double LastReadbackMilliseconds { get; private set; }
    public double LastProjectionMilliseconds { get; private set; }

    public Gemma4CpuVocabularyHead(QuantizedCheckpoint weights, int workers)
    {
        if (!Avx512F.IsSupported)
        {
            throw new PlatformNotSupportedException("The experimental CPU vocabulary head requires AVX-512F.");
        }
        long start = Stopwatch.GetTimestamp();
        options = new() { MaxDegreeOfParallelism = workers };
        slices = new uint[Gemma4TextModel.VocabularySize / SliceRows][];
        for (int slice = 0; slice < slices.Length; slice++)
        {
            slices[slice] = weights.ReadRows(Gemma4TextModel.Embeddings, slice * SliceRows, SliceRows);
        }
        // More work items than workers let an available core take another contiguous range.
        int rangeCount = (int)Math.Min((long)workers * 8, Gemma4TextModel.VocabularySize);
        winners = new (int, float)[rangeCount];
        scoreTable = Gemma4TextModel.CreateHeadScoreTable();
        PreparationMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    public (int Token, float Score) Predict(VulkanComputeMath compute, GpuTensor normalized, float[]? scores)
    {
        long start = Stopwatch.GetTimestamp();
        compute.ReadInto(normalized, hidden);
        LastReadbackMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = Stopwatch.GetTimestamp();
        Span<float> evenValues = MemoryMarshal.Cast<Vector512<float>, float>(even.AsSpan());
        Span<float> oddValues = MemoryMarshal.Cast<Vector512<float>, float>(odd.AsSpan());
        for (int index = 0; index < hidden.Length / 2; index++)
        {
            evenValues[index] = hidden[2 * index];
            oddValues[index] = hidden[2 * index + 1];
        }
        Parallel.For(0, winners.Length, options, range => ProjectRange(range, scores));
        var best = (Token: -1, Score: float.NegativeInfinity);
        foreach (var candidate in winners)
        {
            // Ranges and rows are in token order, so an equal BF16 score keeps the first token.
            if (candidate.Score > best.Score) { best = candidate; }
        }
        if (best.Score == float.MaxValue || best.Token < 0)
        {
            throw new InvalidDataException("AUR-GEMMA-004: Invalid CPU vocabulary score.");
        }
        LastProjectionMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return best;
    }

    private void ProjectRange(int range, float[]? scores)
    {
        int first = (int)((long)range * Gemma4TextModel.VocabularySize / winners.Length);
        int end = (int)((long)(range + 1) * Gemma4TextModel.VocabularySize / winners.Length);
        var best = (Token: first, Score: float.NegativeInfinity);
        for (int token = first; token < end; token++)
        {
            uint[] words = slices[token / SliceRows];
            ref uint row = ref words[(token % SliceRows) * WordsPerRow];
            float sum = ProjectRow(ref row);
            float score = float.IsNaN(sum)
                ? float.MaxValue
                : scoreTable[BitConverter.SingleToUInt32Bits(Gemma4TextModel.Round(sum)) >> 16];
            if (scores is not null) { scores[token] = score; }
            if (score > best.Score) { best = (token, score); }
        }
        winners[range] = best;
    }

    private float ProjectRow(ref uint row)
    {
        // Each 20-byte block has a scale and sixteen bytes containing two
        // neighboring weights each. The normalized input is split just once
        // per token, so both halves use contiguous vector loads for every row.
        var sums = Vector512<float>.Zero;
        var mask = Vector512.Create(15);
        var zeroPoint = Vector512.Create(8);
        for (int block = 0; block < BlocksPerRow; block++)
        {
            ref uint scaleWord = ref Unsafe.Add(ref row, block * BlockQ4.WordsPerBlock);
            var scale = Vector512.Create(BitConverter.UInt32BitsToSingle(scaleWord));
            ref byte packed = ref Unsafe.As<uint, byte>(ref Unsafe.Add(ref scaleWord, 1));
            var bytes = Avx512F.ConvertToVector512Int32(Vector128.LoadUnsafe(ref packed));
            var low = Vector512.ConvertToSingle((bytes & mask) - zeroPoint);
            var high = Vector512.ConvertToSingle((bytes >> 4) - zeroPoint);
            var products = Avx512F.FusedMultiplyAdd(high, odd[block], low * even[block]);
            sums = Avx512F.FusedMultiplyAdd(products, scale, sums);
        }
        return Vector512.Sum(sums);
    }
}
