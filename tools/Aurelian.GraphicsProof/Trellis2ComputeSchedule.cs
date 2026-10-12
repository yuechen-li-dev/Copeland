using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Aurelian.Runtime.Dominatus.Inspection;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;

internal sealed record Trellis2ComputeStage(string Name, Action Execute);

/// <summary>Host-ticked Dominatus coroutine, not a second state-machine runner.
/// A stage completes synchronously on the owning Vulkan plant before the next
/// tick. Blackboard/trace contain identities and costs, never native buffers.</summary>
internal sealed class Trellis2ComputeSchedule(string identity = "trellis.occupancy")
{
    private static readonly BbKey<string> StageKey = new("trellis.stage");
    private static readonly BbKey<int> CompletedKey = new("trellis.completedStages");
    private static readonly BbKey<double> CostKey = new("trellis.lastStageMilliseconds");
    private static readonly BbKey<string> ErrorKey = new("trellis.failure");
    private readonly AurelianAgentRuntime runtime = new(traceCapacity: 8192);
    private long tick;
    private bool completed;
    private object? inspection;
    private ExceptionDispatchInfo? failure;
    public List<object> Measurements { get; } = [];

    public void Run(IEnumerable<Trellis2ComputeStage> stages)
    {
        var root = StateId.Of(identity + ".execute");
        var graph = new HfsmGraph { Root = root };
        graph.Add(root, Execute);
        runtime.Add(identity, new HfsmInstance(graph));
        try
        {
            while (!completed)
            {
                tick++;
                runtime.Tick(TimeSpan.FromSeconds(1.0 / 60));
            }
            failure?.Throw();
        }
        finally
        {
            inspection = runtime.Inspector.Observe();
            // Cancels/disposes the kernel-owned iterator, including partially
            // evaluated graphs if a stage throws. Resource owners do the cleanup.
            runtime.Remove(identity);
        }

        IEnumerator<AiStep> Execute(AiCtx context)
        {
            int count = 0;
            using var enumerator = stages.GetEnumerator();
            while (true)
            {
                try
                {
                    if (!enumerator.MoveNext())
                    {
                        break;
                    }
                    Trellis2ComputeStage stage = enumerator.Current;
                    context.Agent.Bb.Set(StageKey, stage.Name);
                    var timer = Stopwatch.StartNew();
                    stage.Execute();
                    double milliseconds = timer.Elapsed.TotalMilliseconds;
                    context.Agent.Bb.Set(CostKey, milliseconds);
                    context.Agent.Bb.Set(CompletedKey, ++count);
                    Measurements.Add(new { stage.Name, Milliseconds = milliseconds });
                    Console.WriteLine($"Stage {count}: {stage.Name}, {milliseconds:F1} ms");
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
            yield return new Steady("OccupancyExecutionComplete");
        }
    }

    public object Inspection => inspection ?? runtime.Inspector.Observe();
}
