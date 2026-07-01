using Aurelian.Core.Compositor;
using Aurelian.Core.Engine;
using Aurelian.Core.Engine.Frames;
using Aurelian.Rendering.Contracts.Compositor;
using Aurelian.Runtime.Compositor;
using Xunit;

namespace Aurelian.Core.Tests;

public sealed class AurelianFramePumpM0Tests
{
    [Fact]
    public void AurelianFrameId_ToString_UsesInvariantValue()
    {
        Assert.Equal("1234", new AurelianFrameId(1234).ToString());
    }

    [Fact]
    public void AurelianFrameId_Next_IncrementsValue()
    {
        Assert.Equal(new AurelianFrameId(1), AurelianFrameId.Zero.Next());
    }

    [Fact]
    public async Task AurelianFramePump_RunOneFrame_RejectsWhenEngineNotStarted()
    {
        var pump = new AurelianFramePump(new AurelianEngine(), new CompositorActuationBridge(new FakeCompositorMechanism()));

        AurelianFrameResult result = await pump.RunOneFrameAsync(Input(1, PlantOutputReadinessStatus.Ready));

        Assert.False(result.Success);
        Assert.Equal(AurelianFrameStatus.Rejected, result.Status);
        Assert.Null(result.CompositorResult);
        Assert.Equal(AurelianFrameDiagnosticCodes.EngineNotStarted, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task AurelianFramePump_RunOneFrame_CompletesWhenCompositorDispatchSucceeds()
    {
        var mechanism = new FakeCompositorMechanism();
        var pump = StartedPump(mechanism);

        AurelianFrameResult result = await pump.RunOneFrameAsync(Input(2, PlantOutputReadinessStatus.Ready));

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.Equal(AurelianFrameStatus.Completed, result.Status);
        Assert.NotNull(result.CompositorResult);
        Assert.Equal(CompositorPolicyStatus.Dispatched, result.CompositorResult.Status);
        Assert.NotNull(result.CompositorResult.DispatchResult);
        Assert.Equal(CompositorDispatchStatus.Dispatched, result.CompositorResult.DispatchResult.Status);
        Assert.Equal(2UL, mechanism.LastRequest?.FrameId);
        Assert.Equal("offscreen", Assert.Single(mechanism.LastRequest!.Inputs).ImageId);
    }

    [Fact]
    public async Task AurelianFramePump_RunOneFrame_WaitsWhenCompositorOutputsPending()
    {
        var mechanism = new FakeCompositorMechanism();
        var pump = StartedPump(mechanism);

        AurelianFrameResult result = await pump.RunOneFrameAsync(Input(3, PlantOutputReadinessStatus.Pending));

        Assert.False(result.Success);
        Assert.Equal(AurelianFrameStatus.Waiting, result.Status);
        Assert.NotNull(result.CompositorResult);
        Assert.Equal(CompositorPolicyStatus.WaitingForOutputs, result.CompositorResult.Status);
        Assert.Null(result.CompositorResult.DispatchResult);
        Assert.Null(mechanism.LastRequest);
        Assert.Equal(AurelianFrameDiagnosticCodes.CompositorWaiting, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task AurelianFramePump_RunOneFrame_RejectsUnsupportedCompositorPolicy()
    {
        var mechanism = new FakeCompositorMechanism();
        var pump = StartedPump(mechanism);

        AurelianFrameResult result = await pump.RunOneFrameAsync(Input(4, PlantOutputReadinessStatus.Ready, CompositorPolicyKind.Differential));

        Assert.False(result.Success);
        Assert.Equal(AurelianFrameStatus.Rejected, result.Status);
        Assert.NotNull(result.CompositorResult);
        Assert.Equal(CompositorPolicyStatus.Rejected, result.CompositorResult.Status);
        Assert.Null(mechanism.LastRequest);
        Assert.Equal(AurelianFrameDiagnosticCodes.CompositorRejected, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task AurelianFramePump_RunOneFrame_PropagatesCompositorDispatchFailure()
    {
        var mechanism = new FakeCompositorMechanism(CompositorDispatchStatus.Failed);
        var pump = StartedPump(mechanism);

        AurelianFrameResult result = await pump.RunOneFrameAsync(Input(5, PlantOutputReadinessStatus.Ready));

        Assert.False(result.Success);
        Assert.Equal(AurelianFrameStatus.Failed, result.Status);
        Assert.NotNull(result.CompositorResult);
        Assert.Equal(CompositorPolicyStatus.Failed, result.CompositorResult.Status);
        Assert.NotNull(result.CompositorResult.DispatchResult);
        Assert.Equal(CompositorDispatchStatus.Failed, result.CompositorResult.DispatchResult.Status);
        Assert.Equal(AurelianFrameDiagnosticCodes.CompositorFailed, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task AurelianFramePump_RunOneFrame_ReportsCancellationAsFrameFailure()
    {
        var pump = StartedPump(new FakeCompositorMechanism());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        AurelianFrameResult result = await pump.RunOneFrameAsync(Input(6, PlantOutputReadinessStatus.Ready), cts.Token);

        Assert.False(result.Success);
        Assert.Equal(AurelianFrameStatus.Failed, result.Status);
        Assert.Equal(AurelianFrameDiagnosticCodes.FrameCancelled, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void AurelianFramePump_DoesNotCreateVulkanOrWindowResources()
    {
        string framesRoot = ProjectPath("src/Aurelian.Core/Engine/Frames");
        string source = string.Join('\n', Directory.GetFiles(framesRoot, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        string[] forbidden =
        [
            "Create" + "Vul" + "kan" + "Surface",
            "Window" + ".Create",
            "Vk" + ".GetApi",
            "vk" + "Create",
            "vk" + "Cmd",
            "vk" + "Queue",
            "Swap" + "chain",
            "Surface",
            "Sil" + "k",
            "Vul" + "kan",
        ];

        foreach (string term in forbidden)
        {
            Assert.DoesNotContain(term, source, StringComparison.Ordinal);
        }
    }

    private static AurelianFramePump StartedPump(FakeCompositorMechanism mechanism)
    {
        var engine = new AurelianEngine();
        AurelianEngineResult start = engine.Start();
        Assert.True(start.Success);
        return new AurelianFramePump(engine, new CompositorActuationBridge(mechanism));
    }

    private static AurelianFrameInput Input(
        ulong frameId,
        PlantOutputReadinessStatus status,
        CompositorPolicyKind policy = CompositorPolicyKind.Passthrough)
    {
        var output = new PlantOutputRef(0, frameId, "offscreen");
        var readiness = new PlantOutputReadiness(
            output,
            status,
            CompletedFenceValue: status is PlantOutputReadinessStatus.Ready or PlantOutputReadinessStatus.Reused ? frameId : null);
        var target = new PresentationTargetRef(0, 0, frameId);
        var frameFacts = new CompositorFrameFacts(frameId, [readiness], CompositorDiagnostics.Empty);
        var required = new RequiredPlantOutputSet(frameId, policy, [output]);
        return new AurelianFrameInput(new AurelianFrameId(frameId), new CompositorPolicyFacts(frameFacts, required, target, policy));
    }

    private static string FormatDiagnostics(AurelianFrameResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string ProjectPath(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aurelian.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, relativePath);
    }

    private sealed class FakeCompositorMechanism : ICompositorMechanism
    {
        private readonly CompositorDispatchStatus status;

        public FakeCompositorMechanism(CompositorDispatchStatus status = CompositorDispatchStatus.Dispatched)
        {
            this.status = status;
        }

        public CompositorDispatchRequest? LastRequest { get; private set; }

        public Task<CompositorDispatchResult> DispatchAsync(
            CompositorDispatchRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            IReadOnlyList<CompositorDispatchDiagnostic> diagnostics = status == CompositorDispatchStatus.Failed
                ? [new CompositorDispatchDiagnostic("ACOMP-FAKE", CompositorDispatchDiagnosticSeverity.Error, "Fake compositor dispatch failure.")]
                : [];
            return Task.FromResult(new CompositorDispatchResult(
                status,
                request.FrameId,
                request.Policy,
                request.Target,
                CompositorDiagnostics.Empty,
                diagnostics));
        }
    }
}
