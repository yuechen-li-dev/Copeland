using Aurelian.Assets.Tensors;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Xunit;

namespace Aurelian.Inference.Tests;

public sealed class CheckpointBindingTests
{
    [Fact]
    public void BiasFreeLayerPadsPartialWeightTilesWithoutChangingStoredOrder()
    {
        var weights = new Weights();
        var binding = new CheckpointLinearLayer(weights, "matrix");
        var plan = binding.Plan(2);
        var load = binding.TileLoader(plan);
        Assert.Equal(new float[] { 1, 2, 0, 3, 4, 0 }, load(plan.Tiles[0]));
        Assert.Equal(new float[] { 5, 6, 0, 0, 0, 0 }, load(plan.Tiles[1]));
        Assert.Throws<InvalidDataException>(() => load(new(3, 1)));
        Assert.Throws<InvalidDataException>(() => binding.TileLoader(new(3, 3, 2)));
    }

    [Fact]
    public void HigherRankFlatteningIsExplicitAndBiasShapeIsValidated()
    {
        var weights = new Weights();
        Assert.Throws<InvalidDataException>(() => new CheckpointLinearLayer(weights, "kernel"));
        var binding = new CheckpointLinearLayer(weights, "kernel", flattenInnerAxes: true);
        Assert.Equal(2, binding.InputWidth);
        Assert.Equal(new float[] { 1, 2, 0, 3, 4, 0, 5, 6, 0 }, binding.TileLoader(binding.Plan())(new(0, 3)));
        Assert.Throws<InvalidDataException>(() => new CheckpointLinearLayer(weights, "matrix", "wrongBias"));
        Assert.Throws<InvalidDataException>(() => new CheckpointLinearLayer(weights, "absent"));
    }

    [Fact]
    public void AuthoredModelContractRejectsMissingShapeEncodingAndDuplicateNames()
    {
        var weights = new Weights();
        CheckpointContract.Validate(weights, new("matrix", [3, 2], "F32"), new("optional", [1], Optional: true));
        Assert.Throws<InvalidDataException>(() => CheckpointContract.Validate(weights, new ModelTensorRequirement("absent", [3, 2])));
        Assert.Throws<InvalidDataException>(() => CheckpointContract.Validate(weights, new ModelTensorRequirement("matrix", [2, 3])));
        Assert.Throws<InvalidDataException>(() => CheckpointContract.Validate(weights, new ModelTensorRequirement("matrix", [3, 2], "BF16")));
        Assert.Throws<InvalidDataException>(() => CheckpointContract.Validate(weights, new("matrix", [3, 2]), new("matrix", [3, 2])));
    }

    [Fact]
    public void BrokenProviderPayloadCannotSilentlyTruncateOrUseNonfiniteBias()
    {
        var weights = new Weights { Truncated = true };
        var binding = new CheckpointLinearLayer(weights, "matrix");
        var plan = binding.Plan();
        Assert.Throws<InvalidDataException>(() => binding.TileLoader(plan)(plan.Tiles[0]));
        weights.Truncated = false;
        weights.NonfiniteBias = true;
        Assert.Throws<InvalidDataException>(() => new CheckpointLinearLayer(weights, "matrix", "bias").TileLoader(plan));
    }

    private sealed class Weights : ICheckpointWeights
    {
        public bool Truncated { get; set; }
        public bool NonfiniteBias { get; set; }
        public IReadOnlyDictionary<string, CheckpointTensorInfo> Tensors { get; } = new Dictionary<string, CheckpointTensorInfo>
        {
            ["matrix"] = new("matrix", "F32", [3, 2]),
            ["kernel"] = new("kernel", "F32", [3, 1, 1, 1, 2]),
            ["bias"] = new("bias", "F32", [3]),
            ["wrongBias"] = new("wrongBias", "F32", [2]),
        };
        public float[] ReadVector(string name) => NonfiniteBias ? [0, float.NaN, 0] : [1, 2, 3];
        public float[] ReadRows(string name, int firstRow, int rowCount)
        {
            return Truncated ? [1] : Enumerable.Range(firstRow * 2 + 1, rowCount * 2).Select(value => (float)value).ToArray();
        }
        public float[] ReadSlices(string name, int firstSlice, int sliceCount) => ReadRows(name, firstSlice, sliceCount);
    }
}
