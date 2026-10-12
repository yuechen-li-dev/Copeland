using Aurelian.Inference;
using Aurelian.Assets.Tensors;
using Aurelian.Graphics.Vulkan.ComputeMath;

namespace Aurelian.Inference.Trellis2;

public sealed class Trellis2FlowModel(VulkanComputeMath math, SafeTensorFile weights, GpuTensor? coordinates = null,
    Action<int, GpuTensor>? inspectBlock = null, float[]? concatenatedConditioning = null)
{
    public Action<string>? Log { get; set; }

    public int Tokens => coordinates?.Rows ?? 4096;
    public int InputChannels => weights.Tensors["input_layer.weight"].Shape[1];
    public int SampleChannels => weights.Tensors["out_layer.weight"].Shape[0];
    public IEnumerable<ComputeStage> Evaluate(float[] sample, GpuTensor context,
        float timestep, string label, Action<float[]> complete)
    {
        using var input = math.CreateTensor(Tokens, InputChannels);
        if (sample.Length != Tokens * SampleChannels)
        {
            throw new InvalidDataException("Flow sample does not match the learned output channels.");
        }
        if (InputChannels == SampleChannels)
        {
            math.Upload(input, sample);
        }
        else
        {
            int conditionChannels = InputChannels - SampleChannels;
            if (concatenatedConditioning is null || concatenatedConditioning.Length != Tokens * conditionChannels)
            {
                throw new InvalidDataException("AUR-TRELLIS-MATERIAL-001: Shape-conditioned flow requires explicit matching concatenated features.");
            }
            var values = new float[input.Elements];
            for (int row = 0; row < Tokens; row++)
            {
                Array.Copy(sample, row * SampleChannels, values, row * InputChannels, SampleChannels);
                Array.Copy(concatenatedConditioning, row * conditionChannels, values,
                    row * InputChannels + SampleChannels, conditionChannels);
            }
            math.Upload(input, values);
        }
        var shared = new Trellis2BlockGraph(math, weights) { Log = Log };
        GpuTensor? hidden = null;
        GpuTensor? modulation = null;
        try
        {
            yield return new(label + ".input", () =>
            {
                hidden = shared.Affine(input, "input_layer", rounded: true);
                modulation = shared.Conditioning(timestep);
            });
            for (int index = 0; index < 30; index++)
            {
                int block = index;
                yield return new(label + $".block.{block}", () =>
                {
                    var graph = new Trellis2BlockGraph(math, weights, block) { Log = Log };
                    GpuTensor next = graph.Run(hidden!, context, modulation!, coordinates);
                    hidden!.Dispose();
                    hidden = next;
                    inspectBlock?.Invoke(block, hidden);
                });
            }
            yield return new(label + ".output", () =>
            {
                using var affine = math.CreateTensor(1, 3072);
                math.Upload(affine, [.. Enumerable.Repeat(1f, 1536), .. new float[1536]]);
                var batch = math.CreateBatch();
                batch.Normalize(hidden!, affine, hidden!, Normalization.LayerNorm, 1e-5f);
                math.Execute(batch);
                using var output = shared.Affine(hidden!, "out_layer", rounded: false);
                complete(math.Read(output));
            });
        }
        finally
        {
            hidden?.Dispose();
            modulation?.Dispose();
        }
    }
}
