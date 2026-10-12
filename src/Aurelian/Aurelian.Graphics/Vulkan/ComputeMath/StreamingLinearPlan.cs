namespace Aurelian.Graphics.Vulkan.ComputeMath;

public sealed record LinearWeightTile(int FirstOutput, int OutputCount);

/// <summary>A fixed output-row weight schedule. Weights arrive as [output, input]
/// with one bias value appended to each row. Activations remain resident.</summary>
public sealed class StreamingLinearPlan
{
    public int InputWidth { get; }
    public int OutputWidth { get; }
    public int TileRows { get; }
    public int DispatchesPerTile => (InputWidth + ComputeMathBatch.MatrixKChunk - 1) / ComputeMathBatch.MatrixKChunk;
    public ulong WeightBufferBytes => (ulong)TileRows * (ulong)(InputWidth + 1) * 4;
    public ulong ExpandedFullWeightBytes => (ulong)OutputWidth * (ulong)(InputWidth + 1) * 4;
    public IReadOnlyList<LinearWeightTile> Tiles { get; }

    public StreamingLinearPlan(int inputWidth, int outputWidth, int tileRows)
    {
        ComputeMathBatch.Require(inputWidth is > 0 and <= 32768 && outputWidth is > 0 and <= 65536
            && tileRows > 0 && tileRows <= outputWidth && (long)tileRows * (inputWidth + 1) <= 1048576
            && tileRows <= 4096, "Streaming linear dimensions or weight tile exceed the bounded tensor contract.");
        InputWidth = inputWidth;
        OutputWidth = outputWidth;
        TileRows = tileRows;
        var tiles = new List<LinearWeightTile>();
        for (int first = 0; first < outputWidth; first += tileRows)
        {
            tiles.Add(new(first, Math.Min(tileRows, outputWidth - first)));
        }
        Tiles = tiles.AsReadOnly();
    }
}
