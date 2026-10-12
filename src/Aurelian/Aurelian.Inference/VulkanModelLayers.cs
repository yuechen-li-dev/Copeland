using Aurelian.Assets.Tensors;
using Aurelian.Graphics.Vulkan.ComputeMath;

namespace Aurelian.Inference;

public enum TensorRounding { Float32, Float16, BFloat16 }
public sealed record ModelLayerMeasurement(string Weight, StreamingLinearResult Execution);

/// <summary>Reusable learned layers over caller-owned tensors and checkpoint.
/// Returned allocations belong to the caller; failed operations release them.
/// Rounding is an explicit model boundary, separate from cooperative math policy.</summary>
public sealed class VulkanModelLayers(VulkanComputeMath compute, ICheckpointWeights weights)
{
    public GpuTensor Linear(GpuTensor input, string weightName, string? biasName = null,
        TensorRounding rounding = TensorRounding.Float32, bool flattenInnerAxes = false,
        int maximumTileRows = 256, Action<ModelLayerMeasurement>? observe = null)
    {
        if (!Enum.IsDefined(rounding))
        {
            throw new ArgumentOutOfRangeException(nameof(rounding));
        }
        var binding = new CheckpointLinearLayer(weights, weightName, biasName, flattenInnerAxes);
        if (input.Columns != binding.InputWidth)
        {
            throw new InvalidDataException("AUR-INFERENCE-WEIGHTS-001: Activation width differs from its explicit weight binding.");
        }
        var plan = binding.Plan(maximumTileRows);
        var load = binding.TileLoader(plan);
        var output = compute.CreateTensor(input.Rows, binding.OutputWidth);
        try
        {
            var result = compute.LinearStreamed(input, output, plan, load);
            if (rounding != TensorRounding.Float32)
            {
                var batch = compute.CreateBatch();
                if (rounding == TensorRounding.Float16)
                {
                    batch.RoundFloat16(output, output);
                }
                else
                {
                    batch.RoundBFloat16(output, output);
                }
                compute.Execute(batch);
            }
            observe?.Invoke(new(weightName, result));
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }
}
