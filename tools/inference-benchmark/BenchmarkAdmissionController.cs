using Aurelian.Graphics.Vulkan.ComputeMath;

namespace Aurelian.Inference.Benchmarks;

internal sealed class BenchmarkAdmissionController(string mode) : IComputeStreamController
{
    private double serviceMicroseconds;
    private int lastLimit;
    public double PreparationMicroseconds { get; set; }
    public int LimitChanges { get; private set; }
    public int MinimumLimit { get; private set; } = 4;
    public int MaximumLimit { get; private set; }

    public int Plan(ComputeStreamForecast forecast) => mode == "adaptive" ? 4 : int.Parse(mode[5..]);

    public int AdmissionLimit(int capacity)
    {
        // First bounded experiment: cover an explicitly modeled 50-us host
        // feedback/submission floor, plus preparation, using GPU service costs.
        // This is a feedforward heuristic, not a qualified Smith controller.
        int limit = capacity;
        if (mode == "adaptive" && serviceMicroseconds > 0)
        {
            limit = Math.Clamp(1 + (int)Math.Ceiling((50 + PreparationMicroseconds) / serviceMicroseconds), 1, capacity);
        }
        if (lastLimit != 0 && limit != lastLimit)
        {
            LimitChanges++;
        }
        lastLimit = limit;
        MinimumLimit = Math.Min(MinimumLimit, limit);
        MaximumLimit = Math.Max(MaximumLimit, limit);
        return limit;
    }

    public void Observe(ComputeStreamObservation observation)
    {
        if (observation.Completed && observation.GpuMilliseconds is { } cost)
        {
            double measured = cost * 1000;
            serviceMicroseconds = serviceMicroseconds == 0 ? measured : serviceMicroseconds * .8 + measured * .2;
        }
    }
}
