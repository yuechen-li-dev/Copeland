using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Inference;

namespace Aurelian.WhisperBenchmark;

internal sealed record CheckpointEncoderBlock(GpuTensor AttentionNorm, GpuTensor MlpNorm,
    CheckpointProjection Query, CheckpointProjection Key, CheckpointProjection Value,
    CheckpointProjection Output, CheckpointProjection Up, CheckpointProjection Down)
{
    public static CheckpointEncoderBlock Bind(TinyEnglishCheckpoint checkpoint, string prefix,
        Func<string, GpuTensor> norm)
    {
        CheckpointProjection Bind(string suffix, bool bias = true)
        {
            string name = prefix + suffix;
            var binding = new CheckpointLinearLayer(checkpoint, name + ".weight", bias ? name + ".bias" : null);
            var plan = binding.Plan(maximumTileRows: 256);
            return new(plan, binding.TileLoader(plan));
        }
        return new(norm(prefix + ".attn_ln"), norm(prefix + ".mlp_ln"),
            Bind(".attn.query"), Bind(".attn.key", bias: false), Bind(".attn.value"),
            Bind(".attn.out"), Bind(".mlp.0"), Bind(".mlp.2"));
    }
}

internal sealed record CheckpointProjection(StreamingLinearPlan Plan, Func<LinearWeightTile, float[]> Load)
{
    public void Run(VulkanComputeMath math, GpuTensor input, GpuTensor output)
    {
        math.LinearStreamed(input, output, Plan, Load);
    }
}

// The composite linear operation uses its own physical ring and drains it before returning.
// Keep its counters separate from the outer queue, which also records activation packing.
internal sealed class CheckpointTileController : IComputeStreamController
{
    public CheckpointTileSample Sample { get; private set; } = new();
    public void Begin(bool profile) => Sample = new() { Intervals = profile ? [] : null };
    public int Plan(ComputeStreamForecast forecast) => Math.Min(2, forecast.BatchCount);

    public void Observe(ComputeStreamObservation observation)
    {
        if (!observation.Completed)
        {
            return;
        }
        Sample.Tiles++;
        Sample.Bytes += observation.UploadBytes;
        Sample.LargestUploadBytes = Math.Max(Sample.LargestUploadBytes, observation.UploadBytes);
        Sample.PreparationMs += observation.PreparationMilliseconds;
        Sample.CopyMs += observation.TransferGpuMilliseconds ?? 0;
        Sample.ComputeMs += observation.GpuMilliseconds ?? 0;
        Sample.Intervals?.Add(observation);
    }
}

internal sealed class CheckpointTileSample
{
    public int Tiles { get; set; }
    public ulong Bytes { get; set; }
    public ulong LargestUploadBytes { get; set; }
    public double PreparationMs { get; set; }
    public double CopyMs { get; set; }
    public double ComputeMs { get; set; }
    public List<ComputeStreamObservation>? Intervals { get; init; }
}
