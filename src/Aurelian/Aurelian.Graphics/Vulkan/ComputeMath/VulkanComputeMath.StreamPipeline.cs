using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Aurelian.Graphics.Vulkan.ComputeMath;

public sealed unsafe partial class VulkanComputeMath
{
    private StreamingLinearResult LinearPipelined(GpuTensor input, GpuTensor output,
        StreamingLinearPlan plan, Func<LinearWeightTile, float[]> loadWeights, IComputeStreamController controller)
    {
        bool cooperative = UseCooperativeLinear && CooperativeMatrixEnabled
            && input.Rows is >= 16 and <= 8192 && plan.InputWidth % 16 == 0
            && (plan.InputWidth <= 4096 || plan.InputWidth == 8192) && plan.TileRows % 16 == 0;
        int inner = Math.Min(plan.InputWidth, 4096);
        var packedInputs = new List<GpuTensor>();
        using var packedWeights = cooperative ? CreateTensor(inner, plan.TileRows, GpuTensorFormat.PackedFloat16) : null;
        using var product = cooperative ? CreateTensor((input.Rows + 15) / 16 * 16, plan.TileRows) : null;
        try
        {
            if (cooperative)
            {
                var packing = CreateBatch();
                for (int first = 0; first < plan.InputWidth; first += inner)
                {
                    int width = Math.Min(inner, plan.InputWidth - first);
                    var packed = CreateTensor((input.Rows + 15) / 16 * 16, width, GpuTensorFormat.PackedFloat16);
                    packedInputs.Add(packed);
                    packing.PackMatrixHalf(input, packed, first);
                }
                Execute(packing);
            }
            return ExecuteTiles();
        }
        finally
        {
            foreach (var tensor in packedInputs)
            {
                tensor.Dispose();
            }
        }

        StreamingLinearResult ExecuteTiles()
        {
            int dispatchesPerTile = cooperative ? packedInputs.Count * 3 : plan.DispatchesPerTile;
            using var ring = new SubmissionRing(this, controller,
                new(plan.Tiles.Count, dispatchesPerTile, plan.WeightBufferBytes), plan);
            for (int index = 0; index < plan.Tiles.Count; index++)
            {
                var slot = ring.Acquire();
                var timer = Stopwatch.StartNew();
                float[] values = loadWeights(plan.Tiles[index]);
                ComputeMathBatch.Require(values.Length == slot.Weights!.Elements && values.All(float.IsFinite),
                    "Streaming payload must contain exactly the declared finite FP32 weights and bias.");
                Require(slot.Staging!.Write(MemoryMarshal.AsBytes(values.AsSpan())).Success, "Stream staging write failed.");
                double preparationMilliseconds = timer.Elapsed.TotalMilliseconds;
                var tile = plan.Tiles[index];
                ComputeMathBatch batch;
                if (cooperative)
                {
                    batch = CreateBatch();
                    for (int chunk = 0; chunk < packedInputs.Count; chunk++)
                    {
                        batch.PackMatrixHalf(slot.Weights, packedWeights!, chunk * inner, transposeWeights: true);
                        batch.MultiplyCooperative(packedInputs[chunk], packedWeights!, product!);
                        batch.LinearEpilogue(product!, slot.Weights, output, tile,
                            accumulate: chunk != 0, addBias: chunk == packedInputs.Count - 1);
                    }
                }
                else
                {
                    batch = CreateStreamingLinearBatch(input, slot.Weights, output, plan, tile);
                }
                ring.Submit(batch, preparationMilliseconds);
            }
            ring.Drain();
            return new(plan.Tiles.Count, ring.Dispatches, ring.GpuMilliseconds,
                plan.WeightBufferBytes * (ulong)ring.Depth, plan.WeightBufferBytes)
            {
                SubmissionDepth = ring.Depth,
                MaximumOutstanding = ring.MaximumOutstanding,
                MaximumInFlight = ring.MaximumInFlight,
                CompletionWaits = ring.CompletionWaits,
                PreparationMilliseconds = ring.PreparationMilliseconds,
                Cooperative = cooperative,
            };
        }
    }
}
