using Aurelian.Assets.Tensors;
using Aurelian.Graphics.Vulkan.ComputeMath;

internal sealed class Trellis2FlowModel(VulkanComputeMath math, SafeTensorFile weights, GpuTensor? coordinates = null,
    Action<int, GpuTensor>? inspectBlock = null)
{
    public int Tokens => coordinates?.Rows ?? 4096;
    public int InputChannels => coordinates is null ? 8 : 32;
    public IEnumerable<Trellis2ComputeStage> Evaluate(float[] sample, GpuTensor context,
        float timestep, string label, Action<float[]> complete)
    {
        using var input = math.CreateTensor(Tokens, InputChannels);
        math.Upload(input, sample);
        var shared = new Trellis2BlockGraph(math, weights);
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
                    var graph = new Trellis2BlockGraph(math, weights, block);
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
