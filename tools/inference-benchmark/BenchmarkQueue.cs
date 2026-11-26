using Aurelian.Inference.Whisper;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Inference.Dominatus;

namespace Aurelian.Inference.Benchmarks;

// Every policy uses the production submission ring and Vulkan timeline. A retained
// scope spans warmups and samples. Request-scoped setup and disposal are timed.
internal sealed class BenchmarkQueue : IWhisperQueue, IDisposable
{
    private readonly VulkanComputeMath math;
    private readonly IComputeStreamController controller;
    private readonly ComputeStreamForecast forecast;
    private readonly DominatusComputeFeedforward? dominatus;
    private readonly int requestsPerPlan;
    private int requestsInPlan;
    public VulkanComputeMath.QueuedComputeScope? Retained { get; private set; }
    public object? Evidence => controller is BenchmarkAdmissionController admission
        ? new { admission.MinimumLimit, admission.MaximumLimit, admission.LimitChanges, admission.PreparationMicroseconds }
        : dominatus?.Evidence;

    public BenchmarkQueue(VulkanComputeMath math, string policy, int batchesPerRequest, int requests, ulong uploadBytes = 0)
    {
        this.math = math;
        forecast = new(batchesPerRequest, math.MaximumDispatches, uploadBytes);
        requestsPerPlan = Math.Min(requests, 65536 / batchesPerRequest);
        if (policy is "fixed-one" or "fixed-two")
        {
            controller = new FixedDepth(policy == "fixed-one" ? 1 : 2);
        }
        else if (policy == "adaptive")
        {
            controller = new BenchmarkAdmissionController("adaptive");
        }
        else if (policy is "dominatus" or "dominatus-per-request")
        {
            dominatus = new();
            controller = dominatus;
        }
        else
        {
            throw new ArgumentException("Unknown inference queue policy.", nameof(policy));
        }
        if (policy != "dominatus-per-request")
        {
            try
            {
                Retained = OpenRetainedScope();
            }
            catch
            {
                dominatus?.Dispose();
                throw;
            }
        }
    }

    public VulkanComputeMath.QueuedComputeScope? OpenRequestScope()
    {
        if (Retained is null)
        {
            return math.BeginQueuedScope(controller, forecast);
        }
        if (requestsInPlan == requestsPerPlan)
        {
            Retained.Dispose();
            Retained = OpenRetainedScope();
            requestsInPlan = 0;
        }
        requestsInPlan++;
        return null;
    }

    private VulkanComputeMath.QueuedComputeScope OpenRetainedScope() =>
        math.BeginQueuedScope(controller, forecast with { BatchCount = forecast.BatchCount * requestsPerPlan });

    // The paired driver admits exactly one warmed model at a time. Waiting is
    // outside the request's latency and allocation counters.
    public static void WaitForRequest()
    {
        Console.WriteLine("Ready for request");
        if (Console.ReadLine() != "run")
        {
            throw new InvalidOperationException("Expected a request from the benchmark driver.");
        }
    }

    public void Dispose()
    {
        try
        {
            Retained?.Dispose();
        }
        finally
        {
            dominatus?.Dispose();
        }
    }

    private sealed class FixedDepth(int depth) : IComputeStreamController
    {
        public int Plan(ComputeStreamForecast _) => depth;
        public void Observe(ComputeStreamObservation _) { }
    }
}
