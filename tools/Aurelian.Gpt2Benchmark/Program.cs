using System.Globalization;
using System.Text.Json;
using Aurelian.Gpt2Benchmark;
using Aurelian.Inference;
using Aurelian.Inference.Benchmarks;

string[] options = args.Skip(4).ToArray();
string? queueOption = options.FirstOrDefault(option => option.StartsWith("queue=", StringComparison.Ordinal));
string? queuePolicy = queueOption?["queue=".Length..];
if (args.Length < 4 || args[3] is not ("tiled" or "gemv")
    || options.Any(option => option != queueOption && option is not ("validate" or "profile" or "persistent-staging" or "width-norm" or "host-logits" or "step" or "prefill-head" or "prefill-down" or "fused-qkv" or "prefill-attention" or "last-row-mlp" or "capture"))
    || (options.Contains("host-logits") && !options.Contains("persistent-staging"))
    || (options.Contains("prefill-head") && args[3] != "gemv")
    || (queuePolicy is not null && options.Contains("profile"))
    || options.Distinct().Count() != options.Length
    || !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count is < 1 or > 100)
{
    throw new ArgumentException("Usage: Aurelian.Gpt2Benchmark CHECKPOINT OUTPUT_JSON SAMPLES tiled|gemv [validate] [profile] [persistent-staging] [width-norm] [host-logits] [prefill-head] [prefill-down] [fused-qkv] [prefill-attention] [last-row-mlp] [capture] [queue=fixed-one|fixed-two|dominatus|dominatus-per-request] [step]");
}

using var checkpoint = new Gpt2Checkpoint(args[0]);
using var session = VulkanInferenceSession.Create(new()
{
    ApplicationName = "Aurelian GPT-2 benchmark",
    MaximumTensorElements = 64 * 1024 * 1024,
    EnableValidation = options.Contains("validate"),
});
bool profile = options.Contains("profile");
bool reuseStaging = options.Contains("persistent-staging");
using var model = new Gpt2Model(session.Compute, checkpoint, vectorProjection: args[3] == "gemv",
    profileDispatches: profile, reuseStaging: reuseStaging, widthNorm: options.Contains("width-norm"), hostLogits: options.Contains("host-logits"),
    prefillHead: options.Contains("prefill-head"), prefillDown: options.Contains("prefill-down"), fusedQkv: options.Contains("fused-qkv"),
    prefillAttentionKernel: options.Contains("prefill-attention"), lastRowMlp: options.Contains("last-row-mlp"));
using var queue = queuePolicy is null ? null : new BenchmarkQueue(session.Compute, queuePolicy, Gpt2Model.EvaluationsPerRequest, count + 2);
var samples = new List<Gpt2Sample>(count);
bool stepRequests = options.Contains("step");
for (int run = -2; run < count; run++)
{
    if (stepRequests && run >= 0)
    {
        BenchmarkQueue.WaitForRequest();
    }
    using var capture = options.Contains("capture") && run == count - 1
        ? File.Create(args[1] + ".logits.f32")
        : null;
    model.LogitsCapture = capture;
    Gpt2Sample sample = model.Run(queue);
    if (run >= 0)
    {
        samples.Add(sample);
    }
    Console.WriteLine(FormattableString.Invariant(
        $"Iteration {run + 1}: prefill {sample.PrefillMs:F2} ms, decode {sample.DecodeMs:F2} ms"));
}
File.WriteAllText(args[1], JsonSerializer.Serialize(new
{
    Options = options,
    QueuePolicy = queuePolicy,
    Controller = queue?.Evidence,
    Device = session.Plant.Facts.PhysicalDeviceName,
    Validation = session.Plant.Facts.EnabledValidationLayers,
    Mode = args[3],
    Profile = profile,
    HostLogits = options.Contains("host-logits"),
    WidthNorm = options.Contains("width-norm"),
    PersistentStaging = reuseStaging,
    Samples = samples,
}, new JsonSerializerOptions { WriteIndented = true }));
