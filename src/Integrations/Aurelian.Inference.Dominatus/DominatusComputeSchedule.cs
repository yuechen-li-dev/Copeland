using Aurelian.Inference;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Aurelian.Runtime.Dominatus.Inspection;
using global::Dominatus.Core;
using global::Dominatus.Core.Blackboard;
using global::Dominatus.Core.Hfsm;
using global::Dominatus.Core.Nodes;
using global::Dominatus.Core.Nodes.Steps;
using global::Dominatus.Core.Runtime;

namespace Aurelian.Inference.Dominatus;

/// <summary>Host-ticked Dominatus coroutine, not a second state-machine runner.
/// A stage completes synchronously on the owning Vulkan plant before the next
/// tick. Blackboard/trace contain identities and costs, never native buffers.</summary>
public sealed class DominatusComputeSchedule(string identity = "inference.compute", int maximumStages = 4096) : IComputeSchedule
{
    private static readonly BbKey<string> StageKey = new("compute.stage");
    private static readonly BbKey<int> CompletedKey = new("compute.completedStages");
    private static readonly BbKey<double> CostKey = new("compute.lastStageMilliseconds");
    private static readonly BbKey<string> ErrorKey = new("compute.failure");
    private readonly AurelianAgentRuntime runtime = new(traceCapacity: 8192);
    private long tick;
    private bool completed;
    private object? inspection;
    private ExceptionDispatchInfo? failure;
    private readonly List<ComputeStageMeasurement> measurements = [];
    public IReadOnlyList<ComputeStageMeasurement> Measurements => measurements.AsReadOnly();
    public Action<ComputeStageMeasurement>? Observe { get; set; }

    public void Run(IEnumerable<ComputeStage> stages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        if (maximumStages < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumStages));
        }
        if (runtime.Count != 0)
        {
            throw new InvalidOperationException("AUR-INFERENCE-SCHEDULE-001: A compute schedule cannot run reentrantly.");
        }
        completed = false;
        failure = null;
        inspection = null;
        measurements.Clear();
        using var enumerator = stages.GetEnumerator();
        var root = StateId.Of(identity + ".execute");
        var graph = new HfsmGraph { Root = root };
        graph.Add(root, Execute);
        runtime.Add(identity, new HfsmInstance(graph));
        try
        {
            while (!completed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                tick++;
                runtime.Tick(TimeSpan.FromSeconds(1.0 / 60));
            }
            failure?.Throw();
        }
        finally
        {
            inspection = runtime.Inspector.Observe();
            // Remove the kernel coroutine before the host-owned model iterator
            // unwinds. Its using scope releases partially evaluated model tensors.
            runtime.Remove(identity);
        }

        IEnumerator<AiStep> Execute(AiCtx context)
        {
            int count = 0;
            while (true)
            {
                try
                {
                    if (!enumerator.MoveNext())
                    {
                        break;
                    }
                    ComputeStage stage = enumerator.Current;
                    cancellationToken.ThrowIfCancellationRequested();
                    if (count == maximumStages || stage is null || string.IsNullOrWhiteSpace(stage.Name) || stage.Execute is null)
                    {
                        throw new InvalidOperationException("AUR-INFERENCE-SCHEDULE-001: Stage count exceeds its budget or stage identity/action is invalid.");
                    }
                    context.Agent.Bb.Set(StageKey, stage.Name);
                    var timer = Stopwatch.StartNew();
                    stage.Execute();
                    double milliseconds = timer.Elapsed.TotalMilliseconds;
                    context.Agent.Bb.Set(CostKey, milliseconds);
                    context.Agent.Bb.Set(CompletedKey, ++count);
                    var measurement = new ComputeStageMeasurement(stage.Name, milliseconds);
                    measurements.Add(measurement);
                    Observe?.Invoke(measurement);
                }
                catch (Exception exception)
                {
                    // Dominatus deliberately maps iterator exceptions to Failed.
                    // Preserve the original exception for this command's caller,
                    // instead of spinning host ticks after a failed coroutine.
                    failure = ExceptionDispatchInfo.Capture(exception);
                    context.Agent.Bb.Set(ErrorKey, exception.Message);
                    break;
                }
                long observed = tick;
                yield return new WaitUntil(_ => tick != observed);
            }
            completed = true;
            if (failure is not null)
            {
                yield return new Fail(failure.SourceException.Message);
                yield break;
            }
            yield return new Steady("ComputeExecutionComplete");
        }
    }

    public object Inspection => inspection ?? runtime.Inspector.Observe();
}
