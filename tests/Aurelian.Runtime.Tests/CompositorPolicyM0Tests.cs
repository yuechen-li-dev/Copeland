using Aurelian.Rendering.Contracts.Compositor;
using Aurelian.Runtime.Compositor;
using Dominatus.Core.Runtime;
using Xunit;

namespace Aurelian.Runtime.Tests;

public sealed class CompositorPolicyM0Tests
{
    private static readonly PlantOutputRef Output = new(0, 10, "offscreen");
    private static readonly PresentationTargetRef Target = new(0, 0, 10);

    [Fact]
    public void CompositorPolicySession_Decide_WaitsWhenRequiredOutputPending()
    {
        CompositorPolicyDecision decision = CompositorPolicySession.Decide(Facts(PlantOutputReadinessStatus.Pending));

        Assert.False(decision.ShouldDispatch);
        Assert.Null(decision.Request);
        Assert.Equal("RequiredOutputsNotReady", decision.Reason);
    }

    [Fact]
    public void CompositorPolicySession_Decide_DispatchesWhenRequiredOutputReady()
    {
        CompositorPolicyDecision decision = CompositorPolicySession.Decide(Facts(PlantOutputReadinessStatus.Ready));

        Assert.True(decision.ShouldDispatch);
        CompositorDispatchRequest request = Assert.IsType<CompositorDispatchRequest>(decision.Request);
        Assert.Equal(10UL, request.FrameId);
        Assert.Equal(CompositorPolicyKind.Passthrough, request.Policy);
        Assert.Equal(Target, request.Target);
        Assert.Equal(Output, Assert.Single(request.Inputs));
    }

    [Fact]
    public void CompositorPolicySession_Decide_DispatchesWhenRequiredOutputReused()
    {
        CompositorPolicyDecision decision = CompositorPolicySession.Decide(Facts(PlantOutputReadinessStatus.Reused));

        Assert.True(decision.ShouldDispatch);
        Assert.NotNull(decision.Request);
        Assert.Equal("DispatchPassthrough", decision.Reason);
    }

    [Fact]
    public void CompositorPolicySession_Decide_RejectsUnsupportedDifferentialPolicyInM0()
    {
        CompositorPolicyDecision decision = CompositorPolicySession.Decide(Facts(
            PlantOutputReadinessStatus.Ready,
            CompositorPolicyKind.Differential));

        Assert.False(decision.ShouldDispatch);
        Assert.Null(decision.Request);
        Assert.Equal(CompositorPolicyKind.Differential, decision.Policy);
        Assert.Equal("UnsupportedPolicy", decision.Reason);
    }

    [Fact]
    public async Task CompositorPolicySession_RunOnce_DispatchesThroughDominatusFakeActuator()
    {
        var handler = new FakeCompositorDispatchHandler();
        var actuatorHost = new ActuatorHost();
        actuatorHost.Register(handler);

        CompositorPolicyResult result = await CompositorPolicySession.RunOnceAsync(Facts(PlantOutputReadinessStatus.Ready), actuatorHost);

        Assert.True(result.Success);
        Assert.Equal(CompositorPolicyStatus.Dispatched, result.Status);
        Assert.NotNull(result.DispatchResult);
        CompositorDispatchAct act = Assert.Single(handler.Acts);
        Assert.Equal(10UL, act.Request.FrameId);
        Assert.Equal(CompositorPolicyKind.Passthrough, act.Request.Policy);
        Assert.Equal(Output, Assert.Single(act.Request.Inputs));
        Assert.Equal(Target, act.Request.Target);
    }

    [Fact]
    public async Task CompositorPolicySession_RunOnce_ReturnsWaitingWithoutDispatchWhenOutputPending()
    {
        var handler = new FakeCompositorDispatchHandler();
        var actuatorHost = new ActuatorHost();
        actuatorHost.Register(handler);

        CompositorPolicyResult result = await CompositorPolicySession.RunOnceAsync(Facts(PlantOutputReadinessStatus.Pending), actuatorHost);

        Assert.False(result.Success);
        Assert.Equal(CompositorPolicyStatus.WaitingForOutputs, result.Status);
        Assert.Empty(handler.Acts);
        Assert.Equal(CompositorPolicyDiagnosticCodes.RequiredOutputsNotReady, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task CompositorPolicySession_RunOnce_PropagatesDispatchFailure()
    {
        var handler = new FakeCompositorDispatchHandler(success: false);
        var actuatorHost = new ActuatorHost();
        actuatorHost.Register(handler);

        CompositorPolicyResult result = await CompositorPolicySession.RunOnceAsync(Facts(PlantOutputReadinessStatus.Ready), actuatorHost);

        Assert.False(result.Success);
        Assert.Equal(CompositorPolicyStatus.Failed, result.Status);
        Assert.NotNull(result.DispatchResult);
        Assert.Equal(CompositorDispatchStatus.Failed, result.DispatchResult.Status);
        Assert.Equal(CompositorPolicyDiagnosticCodes.DispatchResultFailed, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void CompositorDispatchAct_ContainsNeutralRequestOnly()
    {
        var request = new CompositorDispatchRequest(10, CompositorPolicyKind.Passthrough, [Output], Target);
        var act = new CompositorDispatchAct(request);

        Assert.Equal(request, act.Request);
    }

    [Fact]
    public void CompositorPolicy_DoesNotReferenceForbiddenMechanismTerms()
    {
        string runtimeRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Aurelian.Runtime"));
        string[] sourceFiles = Directory.GetFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories);
        string source = string.Join('\n', sourceFiles.Select(File.ReadAllText));
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

    private static CompositorPolicyFacts Facts(
        PlantOutputReadinessStatus status,
        CompositorPolicyKind policy = CompositorPolicyKind.Passthrough)
    {
        var output = policy == CompositorPolicyKind.Passthrough
            ? Output
            : new PlantOutputRef(0, 10, "offscreen-differential");

        var readiness = new PlantOutputReadiness(output, status, CompletedFenceValue: status is PlantOutputReadinessStatus.Ready or PlantOutputReadinessStatus.Reused ? 10UL : null);
        var frameFacts = new CompositorFrameFacts(10, [readiness], CompositorDiagnostics.Empty);
        var required = new RequiredPlantOutputSet(10, policy, [output]);
        return new CompositorPolicyFacts(frameFacts, required, Target, policy);
    }

    private sealed class FakeCompositorDispatchHandler : IActuationHandler<CompositorDispatchAct>
    {
        private readonly bool _success;

        public FakeCompositorDispatchHandler(bool success = true)
        {
            _success = success;
        }

        public List<CompositorDispatchAct> Acts { get; } = [];

        public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, CompositorDispatchAct cmd)
        {
            Acts.Add(cmd);

            CompositorDispatchResult result = _success
                ? new CompositorDispatchResult(
                    CompositorDispatchStatus.Dispatched,
                    cmd.Request.FrameId,
                    cmd.Request.Policy,
                    cmd.Request.Target,
                    CompositorDiagnostics.Empty,
                    [])
                : new CompositorDispatchResult(
                    CompositorDispatchStatus.Failed,
                    cmd.Request.FrameId,
                    cmd.Request.Policy,
                    cmd.Request.Target,
                    CompositorDiagnostics.Empty,
                    [new CompositorDispatchDiagnostic("ACOMP-FAKE", CompositorDispatchDiagnosticSeverity.Error, "Fake compositor dispatch failure.")]);

            return ActuatorHost.HandlerResult.CompletedWithPayload(result, ok: true);
        }
    }
}
