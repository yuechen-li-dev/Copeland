using Aurelian.Runtime.Inspection;
using Aurelian.Runtime.Dominatus.Inspection;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Trace;
using Dominatus.Core.Runtime;
using Xunit;

namespace Aurelian.Runtime.Tests;

public sealed class AgentInspectionTests
{
    [Fact]
    public void KernelTraceIsBoundedAndPriorSinkRemainsAttached()
    {
        var runtime = new AurelianAgentRuntime(3);
        var graph = new HfsmGraph { Root = StateId.Of("idle") };
        graph.Add(graph.Root, Complete);
        HfsmInstance brain = new(graph);
        var previous = new CountingSink();
        brain.Trace = previous;
        var agent = runtime.Add("lamp", brain);
        var key = new BbKey<int>("brightness");
        for (int index = 0; index < 10; index++)
        {
            agent.Bb.Set(key, index);
            runtime.Tick(TimeSpan.FromSeconds(0.1));
        }
        AgentInspection inspection = runtime.Inspector.Observe();
        Assert.Equal(3, inspection.Trace.Length);
        Assert.True(inspection.DroppedTraceEntries > 0);
        Assert.Equal(3, inspection.Changes.Length);
        Assert.Equal(7, inspection.DroppedChanges);
        Assert.Empty(agent.BbTracker.Journal);
        Assert.Equal("idle", Assert.Single(Assert.Single(inspection.Agents).ActivePath));
        Assert.Equal("9", Assert.Single(inspection.Agents[0].Blackboard).Value);
        Assert.True(previous.Enters >= 10);
        runtime.Remove("lamp");
        Assert.Same(previous, brain.Trace);
        Assert.Empty(runtime.Inspector.Observe().Agents);
    }

    [Fact]
    public void ColdCheckpointUsesKernelCodecAndRequiresFreshMatchingAgents()
    {
        var original = new AurelianAgentRuntime();
        var agent = original.Add("lamp", Brain());
        var key = new BbKey<int>("brightness");
        agent.Bb.Set(key, 7);
        original.Tick(TimeSpan.FromSeconds(0.25));
        AurelianPolicyCheckpoint checkpoint = original.CapturePolicyCheckpoint();
        Assert.Equal(0.25f, checkpoint.Kernel.WorldTimeSeconds);
        var restored = new AurelianAgentRuntime();
        var copy = restored.Add("lamp", Brain());
        restored.RestorePolicyCheckpoint(checkpoint);
        Assert.Equal(7, copy.Bb.GetOrDefault(key, 0));
        Assert.Equal(agent.Brain.GetActivePath(), copy.Brain.GetActivePath());
        restored.Tick(TimeSpan.FromSeconds(0.25));
        Assert.Equal(0.5f, restored.CapturePolicyCheckpoint().Kernel.WorldTimeSeconds);
        Assert.Throws<InvalidOperationException>(() => original.RestorePolicyCheckpoint(checkpoint));
        var mismatched = new AurelianAgentRuntime();
        mismatched.Add("different-name", Brain());
        Assert.Throws<InvalidOperationException>(() => mismatched.RestorePolicyCheckpoint(checkpoint));
    }

    [Fact]
    public void UnsupportedBlackboardValuesAreVisibleButNeverSilentlyCheckpointed()
    {
        var runtime = new AurelianAgentRuntime();
        var agent = runtime.Add("lamp", Brain());
        agent.Bb.Set(new BbKey<object>("domain-object"), new ThrowingDisplay());
        var value = Assert.Single(Assert.Single(runtime.Inspector.Observe().Agents).Blackboard);
        Assert.False(value.CheckpointSupported);
        NotSupportedException error = Assert.Throws<NotSupportedException>(() => runtime.CapturePolicyCheckpoint());
        Assert.Contains("lamp", error.Message);
        Assert.Contains("domain-object", error.Message);
    }

    private static HfsmInstance Brain()
    {
        var graph = new HfsmGraph { Root = StateId.Of("idle") };
        graph.Add(graph.Root, Idle);
        return new HfsmInstance(graph);
    }

    private static IEnumerator<AiStep> Idle(AiCtx context)
    {
        while (true)
        {
            yield return new Steady("idle");
        }
    }

    private static IEnumerator<AiStep> Complete(AiCtx context)
    {
        yield return new Succeed("completed");
    }

    private sealed class CountingSink : IAiTraceSink
    {
        public int Enters { get; private set; }
        public void OnEnter(StateId state, float time, string reason) => Enters++;
        public void OnExit(StateId state, float time, string reason) { }
        public void OnTransition(StateId from, StateId to, float time, string reason) { }
        public void OnYield(StateId state, float time, object yielded) { }
    }

    private sealed class ThrowingDisplay
    {
        public override string ToString() => throw new InvalidOperationException("Domain getters must not run during inspection.");
    }
}
