using Aurelian.Runtime.Sessions;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Xunit;

namespace Aurelian.Runtime.Tests;

public sealed class AurelianRuntimeSessionM0Tests
{
    [Fact]
    public void AurelianRuntimeSession_Start_InitializesDominatusSession()
    {
        var session = new AurelianRuntimeSession();

        AurelianRuntimeResult result = session.Start();

        Assert.True(result.Success);
        Assert.True(session.IsStarted);
        Assert.IsType<ActuatorHost>(session.ActuatorHost);
        Assert.Single(session.World.Agents);
    }

    [Fact]
    public void AurelianRuntimeSession_Start_WhenAlreadyStarted_ReturnsDiagnostic()
    {
        var session = new AurelianRuntimeSession();
        Assert.True(session.Start().Success);

        AurelianRuntimeResult result = session.Start();

        Assert.False(result.Success);
        Assert.Equal(AurelianRuntimeDiagnosticCodes.RuntimeAlreadyStarted, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task AurelianRuntimeSession_Tick_WhenNotStarted_ReturnsDiagnostic()
    {
        var session = new AurelianRuntimeSession();

        AurelianRuntimeTickResult result = await session.TickAsync(Input());

        Assert.False(result.Success);
        Assert.Equal(AurelianRuntimeTickStatus.Rejected, result.Status);
        Assert.Equal(AurelianRuntimeDiagnosticCodes.RuntimeNotStarted, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task AurelianRuntimeSession_Tick_WithInvalidDelta_ReturnsDiagnostic()
    {
        var session = new AurelianRuntimeSession();
        Assert.True(session.Start().Success);

        AurelianRuntimeTickResult result = await session.TickAsync(new AurelianRuntimeTickInput(1, TimeSpan.FromTicks(-1)));

        Assert.False(result.Success);
        Assert.Equal(AurelianRuntimeTickStatus.Rejected, result.Status);
        Assert.Equal(AurelianRuntimeDiagnosticCodes.InvalidDeltaTime, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task AurelianRuntimeSession_Tick_WhenStarted_UsesDominatusActuation()
    {
        var handler = new RecordingRuntimeTickHandler();
        var session = new AurelianRuntimeSession(new AurelianRuntimeSessionOptions
        {
            ConfigureActuatorHost = host => host.Register(handler)
        });
        Assert.True(session.Start().Success);

        AurelianRuntimeTickResult result = await session.TickAsync(Input(42));

        Assert.True(result.Success);
        Assert.Equal(AurelianRuntimeTickStatus.Ticked, result.Status);
        AurelianRuntimeTickAct act = Assert.Single(handler.Acts);
        Assert.Equal(42UL, act.TickIndex);
    }

    [Fact]
    public async Task AurelianRuntimeSession_Tick_PropagatesActuationFailure()
    {
        var session = new AurelianRuntimeSession(new AurelianRuntimeSessionOptions
        {
            ConfigureActuatorHost = host => host.Register(new FailingRuntimeTickHandler())
        });
        Assert.True(session.Start().Success);

        AurelianRuntimeTickResult result = await session.TickAsync(Input());

        Assert.False(result.Success);
        Assert.Equal(AurelianRuntimeTickStatus.Failed, result.Status);
        Assert.Equal(AurelianRuntimeDiagnosticCodes.ActuationFailed, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void AurelianRuntimeSession_Stop_TransitionsToStopped()
    {
        var session = new AurelianRuntimeSession();
        Assert.True(session.Start().Success);

        AurelianRuntimeResult result = session.Stop();

        Assert.True(result.Success);
        Assert.False(session.IsStarted);
    }

    [Fact]
    public async Task AurelianRuntimeSession_Tick_AfterStop_ReturnsDiagnostic()
    {
        var session = new AurelianRuntimeSession();
        Assert.True(session.Start().Success);
        Assert.True(session.Stop().Success);

        AurelianRuntimeTickResult result = await session.TickAsync(Input());

        Assert.False(result.Success);
        Assert.Equal(AurelianRuntimeTickStatus.Rejected, result.Status);
        Assert.Equal(AurelianRuntimeDiagnosticCodes.RuntimeNotStarted, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task AurelianRuntimeSession_Tick_WhenCancelled_ReturnsDiagnostic()
    {
        var session = new AurelianRuntimeSession();
        Assert.True(session.Start().Success);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        AurelianRuntimeTickResult result = await session.TickAsync(Input(), cancellation.Token);

        Assert.False(result.Success);
        Assert.Equal(AurelianRuntimeTickStatus.Cancelled, result.Status);
        Assert.Equal(AurelianRuntimeDiagnosticCodes.Cancelled, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task SequentialAurelianAiWorldRunner_RunsDominatusWorldTick()
    {
        var handler = new RecordingRuntimeTickHandler();
        var actuatorHost = new ActuatorHost();
        actuatorHost.Register(handler);
        var world = new AiWorld(actuatorHost);
        var graph = new HfsmGraph { Root = Dominatus.Core.StateId.Of("aurelian.runtime.runner.test.root") };
        graph.Add(graph.Root, RunnerProbeNode);
        var agent = new AiAgent(new HfsmInstance(graph));
        agent.Bb.Set(TestFactsKey, new AurelianRuntimeSessionFacts(7, TimeSpan.Zero));
        world.Add(agent);

        await new SequentialAurelianAiWorldRunner().RunTickAsync(world, new AurelianRuntimeTickInput(7, TimeSpan.FromMilliseconds(16)));

        Assert.Equal(0.016f, world.Clock.Time, precision: 3);
        Assert.Equal(7UL, Assert.Single(handler.Acts).TickIndex);
    }

    [Fact]
    public void ParallelAiWorldRunner_Inspection_IsDocumentedAsDeferredRuntimeIntegration()
    {
        string auditPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../docs/audits/0064-a64-dominatus-backed-runtime-tick-m0.md"));
        string audit = File.ReadAllText(auditPath);

        Assert.Contains("ParallelAiWorldRunner inspection/integration decision", audit, StringComparison.Ordinal);
        Assert.Contains("deferred", audit, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IAurelianAiWorldRunner", audit, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeTick_DoesNotReferenceForbiddenMechanismTerms()
    {
        string runtimeRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Aurelian.Runtime"));
        string source = string.Join('\n', Directory.GetFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        string[] forbidden =
        [
            "Aurelian." + "Gra" + "phics",
            "Sil" + "k",
            "Vul" + "kan",
            "V" + "k",
            "Swap" + "chain",
            "Sur" + "face",
            "Aurelian." + "Sha" + "ders",
            "D" + "X" + "C",
            "S" + "D" + "S" + "L",
            "Vort" + "ice",
            "V" + "M" + "A",
            "Code" + "References",
            "Service" + "Locator"
        ];

        foreach (string term in forbidden)
        {
            Assert.DoesNotContain(term, source, StringComparison.Ordinal);
        }
    }

    private static readonly Dominatus.Core.Blackboard.BbKey<AurelianRuntimeSessionFacts> TestFactsKey = new("aurelian.runtime.runner.test.facts");
    private static readonly Dominatus.Core.Blackboard.BbKey<ActuationId> TestActuationIdKey = new("aurelian.runtime.runner.test.actuationId");

    private static AurelianRuntimeTickInput Input(ulong tickIndex = 1)
        => new(tickIndex, TimeSpan.FromMilliseconds(16));

    private static IEnumerator<AiStep> RunnerProbeNode(AiCtx ctx)
    {
        var facts = ctx.Agent.Bb.GetOrDefault(TestFactsKey, default!);
        yield return new Act(new AurelianRuntimeTickAct(facts.TickIndex), TestActuationIdKey);
        yield return new AwaitActuation(TestActuationIdKey);
        yield return new Succeed("RunnerProbeComplete");
    }

    private sealed class RecordingRuntimeTickHandler : IActuationHandler<AurelianRuntimeTickAct>
    {
        public List<AurelianRuntimeTickAct> Acts { get; } = [];

        public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, AurelianRuntimeTickAct cmd)
        {
            Acts.Add(cmd);
            return ActuatorHost.HandlerResult.CompletedOk();
        }
    }

    private sealed class FailingRuntimeTickHandler : IActuationHandler<AurelianRuntimeTickAct>
    {
        public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, AurelianRuntimeTickAct cmd)
            => ActuatorHost.HandlerResult.CompletedFailure("runtime tick act failed in test");
    }
}
