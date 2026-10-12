using System.Diagnostics;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Runtime.Dominatus.Inspection;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;

/// <summary>Dominatus owns the feedforward plan and shadow timeline. Physical
/// buffer leases are released exclusively by the compute owner's Vulkan fence.</summary>
internal sealed class Trellis2ComputeFeedforward : IComputeStreamController, IDisposable
{
    private const string Identity = "trellis.compute.feedforward";
    private static readonly StateId Planning = StateId.Of(Identity + ".plan");
    private static readonly StateId Feeding = StateId.Of(Identity + ".feed");
    private static readonly BbKey<int> DepthKey = new("compute.lookaheadDepth");
    private static readonly BbKey<int> OutstandingKey = new("compute.outstanding");
    private static readonly BbKey<long> CompletedKey = new("compute.completedSequence");
    private static readonly BbKey<double> PredictionKey = new("compute.shadowCompletionMilliseconds");
    private static readonly BbKey<double> CorrectionKey = new("compute.completionErrorMilliseconds");
    private readonly AurelianAgentRuntime runtime = new(traceCapacity: 8192);
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Dictionary<long, double> predictions = [];
    private ComputeStreamForecast? forecast;
    private ComputeStreamObservation? observation;
    private long revision;
    private long handled;
    private double gpuMean;
    private double shadowFinish;
    private int plans;
    private object? inspection;
    private readonly Queue<object> measurements = new();
    private long submissions;
    private long completions;
    private long droppedMeasurements;
    public object Evidence => new
    {
        Plans = plans,
        Submissions = submissions,
        Completions = completions,
        DroppedMeasurements = droppedMeasurements,
        RecentMeasurements = measurements.ToArray(),
        PredictionScope = "Host-observed completion includes harvest delay; predictions never authorize physical slot reuse",
    };

    public Trellis2ComputeFeedforward()
    {
        var graph = new HfsmGraph { Root = Planning };
        graph.Add(Planning, PlanStage);
        graph.Add(Feeding, FeedStage);
        runtime.Add(Identity, new HfsmInstance(graph));
    }

    public int Plan(ComputeStreamForecast next)
    {
        forecast = next;
        Advance();
        return runtime.Agent(Identity).Bb.GetOrDefault(DepthKey, 1);
    }

    public void Observe(ComputeStreamObservation next)
    {
        observation = next;
        Advance();
    }

    private void Advance()
    {
        revision++;
        for (int tick = 0; handled != revision && tick < 8; tick++)
        {
            runtime.Tick(TimeSpan.FromSeconds(1.0 / 60));
        }
        if (handled != revision)
        {
            throw new InvalidOperationException("AUR-TRELLIS-FEED-001: Dominatus did not consume the compute event.");
        }
    }

    private IEnumerator<AiStep> PlanStage(AiCtx context)
    {
        var next = forecast ?? throw new InvalidOperationException("Missing compute forecast.");
        // The complete affine tape is known in advance. Two physical slots keep
        // one submitted tile available while the host prepares its successor.
        // Two device allocations plus two transfer allocations fit 16 MiB.
        int depth = next.BatchCount > 1 && next.UploadBytes <= 4 * 1024 * 1024 ? 2 : 1;
        context.Agent.Bb.Set(DepthKey, depth);
        context.Agent.Bb.Set(OutstandingKey, 0);
        predictions.Clear();
        shadowFinish = clock.Elapsed.TotalMilliseconds;
        forecast = null;
        handled = revision;
        Record(new { Plan = ++plans, Forecast = next, Depth = depth });
        yield return new Goto(Feeding, "Prepared bounded affine tape");
    }

    private IEnumerator<AiStep> FeedStage(AiCtx context)
    {
        while (true)
        {
            if (forecast is not null)
            {
                yield return new Goto(Planning, "Next affine tape");
                yield break;
            }
            if (observation is { } next)
            {
                double now = clock.Elapsed.TotalMilliseconds;
                double correction = 0;
                if (!next.Completed)
                {
                    submissions++;
                    shadowFinish = Math.Max(now, shadowFinish) + gpuMean;
                    predictions.Add(next.Sequence, shadowFinish);
                    context.Agent.Bb.Set(PredictionKey, shadowFinish);
                }
                else
                {
                    completions++;
                    if (predictions.Remove(next.Sequence, out double prediction))
                    {
                        correction = now - prediction;
                    }
                    if (next.GpuMilliseconds is { } cost)
                    {
                        gpuMean = gpuMean == 0 ? cost : gpuMean * .8 + cost * .2;
                    }
                    context.Agent.Bb.Set(CompletedKey, next.Sequence);
                    context.Agent.Bb.Set(CorrectionKey, correction);
                }
                context.Agent.Bb.Set(OutstandingKey, next.Outstanding);
                Record(new { Plan = plans, Observation = next, ShadowErrorMilliseconds = correction });
                observation = null;
                handled = revision;
            }
            yield return new WaitUntil(_ => revision != handled);
        }
    }

    public object Inspection => inspection ?? runtime.Inspector.Observe();

    private void Record(object item)
    {
        if (measurements.Count == 512)
        {
            measurements.Dequeue();
            droppedMeasurements++;
        }
        measurements.Enqueue(item);
    }

    public void Dispose()
    {
        inspection = runtime.Inspector.Observe();
        runtime.Remove(Identity);
    }
}
