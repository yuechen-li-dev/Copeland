using Aurelian.Assets.Tensors;
using Aurelian.Graphics.Vulkan.ComputeMath;

namespace Aurelian.Inference;

/// <summary>Explicit output/input checkpoint binding. Flattening higher rank
/// inner axes is opt-in and preserves stored order; it never infers a layout.</summary>
public sealed class CheckpointLinearLayer
{
    private readonly ICheckpointWeights weights;
    private readonly string weightName;
    private readonly string? biasName;
    private readonly bool flattenInnerAxes;
    public int InputWidth { get; }
    public int OutputWidth { get; }

    public CheckpointLinearLayer(ICheckpointWeights weights, string weightName, string? biasName = null,
        bool flattenInnerAxes = false)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentException.ThrowIfNullOrWhiteSpace(weightName);
        if (!weights.Tensors.TryGetValue(weightName, out var tensor)
            || tensor.Shape.Count is < 2 or > 5 || !flattenInnerAxes && tensor.Shape.Count != 2
            || tensor.Shape.Any(value => value <= 0))
        {
            throw Stop("Weight must be an explicit rank-2 matrix or opted-in flattened outer slices: " + weightName);
        }
        int input = 1;
        foreach (int dimension in tensor.Shape.Skip(1))
        {
            input = checked(input * dimension);
        }
        InputWidth = input;
        OutputWidth = tensor.Shape[0];
        if (InputWidth is < 1 or > 32768 || OutputWidth is < 1 or > 65536)
        {
            throw Stop("Weight dimensions exceed the streamed linear profile: " + weightName);
        }
        if (biasName is not null && (!weights.Tensors.TryGetValue(biasName, out var bias)
            || bias.Shape.Count != 1 || bias.Shape[0] != OutputWidth))
        {
            throw Stop("Bias must have one value per output channel: " + biasName);
        }
        this.weights = weights;
        this.weightName = weightName;
        this.biasName = biasName;
        this.flattenInnerAxes = flattenInnerAxes;
    }

    public StreamingLinearPlan Plan(int maximumTileRows = 256)
    {
        if (maximumTileRows is < 1 or > 4096)
        {
            throw Stop("Weight tile limit must be 1..4096 rows.");
        }
        int rows = Math.Min(OutputWidth, Math.Min(maximumTileRows, 1048576 / (InputWidth + 1)));
        return new(InputWidth, OutputWidth, rows);
    }

    public Func<LinearWeightTile, float[]> TileLoader(StreamingLinearPlan plan)
    {
        if (plan.InputWidth != InputWidth || plan.OutputWidth != OutputWidth)
        {
            throw Stop("Weight plan does not match this layer binding.");
        }
        float[] bias = biasName is null ? new float[OutputWidth] : weights.ReadVector(biasName);
        if (bias.Length != OutputWidth || !bias.All(float.IsFinite))
        {
            throw Stop("Bias provider returned an invalid shape or nonfinite values.");
        }
        return tile =>
        {
            if (tile.FirstOutput < 0 || tile.OutputCount < 1 || tile.OutputCount > plan.TileRows
                || (long)tile.FirstOutput + tile.OutputCount > OutputWidth)
            {
                throw Stop("Selected weight tile is outside the layer binding.");
            }
            float[] selected = flattenInnerAxes
                ? weights.ReadSlices(weightName, tile.FirstOutput, tile.OutputCount)
                : weights.ReadRows(weightName, tile.FirstOutput, tile.OutputCount);
            if (selected.Length != tile.OutputCount * InputWidth)
            {
                throw Stop("Weight provider returned a different element count than its selected tile.");
            }
            var packed = new float[plan.TileRows * (InputWidth + 1)];
            for (int row = 0; row < tile.OutputCount; row++)
            {
                Array.Copy(selected, row * InputWidth, packed, row * (InputWidth + 1), InputWidth);
                packed[row * (InputWidth + 1) + InputWidth] = bias[tile.FirstOutput + row];
            }
            return packed;
        };
    }

    private static InvalidDataException Stop(string message) => new("AUR-INFERENCE-WEIGHTS-001: " + message);
}
