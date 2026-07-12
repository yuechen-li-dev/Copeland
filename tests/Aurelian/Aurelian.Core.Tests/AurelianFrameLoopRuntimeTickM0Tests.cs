using Aurelian.Core.Compositor;
using Aurelian.Core.Engine;
using Aurelian.Core.Engine.Frames;
using Aurelian.Core.Engine.Runtime;
using Aurelian.Rendering.Contracts.Compositor;
using Aurelian.Runtime.Compositor;
using Aurelian.Runtime.Sessions;
using Xunit;

namespace Aurelian.Core.Tests;

public sealed class AurelianFrameLoopRuntimeTickM0Tests
{
    [Fact]
    public async Task AurelianFrameLoop_RunAsync_WithRuntimeStep_TicksRuntimeBeforeFramePump()
    {
        List<string> calls = [];
        var ticker = new RecordingRuntimeTicker(calls, AurelianRuntimeTickStatus.Ticked);
        var mechanism = new RecordingCompositorMechanism(calls);
        var loop = new AurelianFrameLoop(
            StartedPump(mechanism),
            new SingleFrameInputProvider(Input(9)),
            runtimeTickStep: new AurelianRuntimeTickFrameStep(ticker));

        AurelianFrameLoopResult result = await loop.RunAsync(new AurelianFrameId(9));

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.Equal(["runtime:9", "frame:9"], calls);
    }

    [Fact]
    public async Task AurelianFrameLoop_RunAsync_WithRuntimeStep_StopsWhenRuntimeTickFails()
    {
        List<string> calls = [];
        var ticker = new RecordingRuntimeTicker(calls, AurelianRuntimeTickStatus.Failed);
        var mechanism = new RecordingCompositorMechanism(calls);
        var loop = new AurelianFrameLoop(
            StartedPump(mechanism),
            new SingleFrameInputProvider(Input(10)),
            runtimeTickStep: new AurelianRuntimeTickFrameStep(ticker));

        AurelianFrameLoopResult result = await loop.RunAsync(new AurelianFrameId(10));

        Assert.False(result.Success);
        Assert.Equal(AurelianFrameLoopStatus.Failed, result.Status);
        Assert.Equal(AurelianFrameLoopStopReason.FrameFailed, result.StopReason);
        Assert.Equal(1, result.FramesAttempted);
        Assert.Equal(0, result.FramesCompleted);
        Assert.Empty(result.Iterations);
        Assert.Equal(["runtime:10"], calls);
        Assert.Equal(AurelianFrameLoopDiagnosticCodes.RuntimeTickFailed, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task AurelianFrameLoop_RunAsync_WithRuntimeStep_RecordsRuntimeTickResultWhenSuccessful()
    {
        var ticker = new RecordingRuntimeTicker([], AurelianRuntimeTickStatus.Ticked);
        var loop = new AurelianFrameLoop(
            StartedPump(),
            new SingleFrameInputProvider(Input(11)),
            runtimeTickStep: new AurelianRuntimeTickFrameStep(ticker));

        AurelianFrameLoopResult result = await loop.RunAsync(new AurelianFrameId(11));

        Assert.True(result.Success, FormatDiagnostics(result));
        AurelianFrameLoopIterationResult iteration = Assert.Single(result.Iterations);
        Assert.NotNull(iteration.RuntimeTickResult);
        Assert.True(iteration.RuntimeTickResult.Success);
        Assert.Equal(11UL, iteration.RuntimeTickResult.RuntimeResult!.TickIndex);
    }

    [Fact]
    public async Task AurelianFrameLoop_RunAsync_WithoutRuntimeStep_BehaviorUnchanged()
    {
        List<string> calls = [];
        var loop = new AurelianFrameLoop(StartedPump(new RecordingCompositorMechanism(calls)), new SingleFrameInputProvider(Input(12)));

        AurelianFrameLoopResult result = await loop.RunAsync(new AurelianFrameId(12));

        Assert.True(result.Success, FormatDiagnostics(result));
        AurelianFrameLoopIterationResult iteration = Assert.Single(result.Iterations);
        Assert.Null(iteration.RuntimeTickResult);
        Assert.Equal(["frame:12"], calls);
    }

    [Fact]
    public async Task AurelianFrameLoop_RunAsync_WithRealRuntimeSession_TicksDominatusRuntime()
    {
        var session = new AurelianRuntimeSession();
        Assert.True(session.Start().Success);
        var loop = new AurelianFrameLoop(
            StartedPump(),
            new SingleFrameInputProvider(Input(13)),
            runtimeTickStep: new AurelianRuntimeTickFrameStep(new AurelianRuntimeSessionTickerAdapter(session)));

        AurelianFrameLoopResult result = await loop.RunAsync(new AurelianFrameId(13));

        Assert.True(result.Success, FormatDiagnostics(result));
        AurelianFrameLoopIterationResult iteration = Assert.Single(result.Iterations);
        Assert.True(iteration.FrameResult.Success);
        Assert.NotNull(iteration.RuntimeTickResult);
        Assert.True(iteration.RuntimeTickResult.Success);
        Assert.Equal(AurelianRuntimeTickStatus.Ticked, iteration.RuntimeTickResult.RuntimeResult!.Status);
        Assert.Equal(13UL, iteration.RuntimeTickResult.RuntimeResult.TickIndex);
    }

    private static AurelianFramePump StartedPump(ICompositorMechanism? mechanism = null)
    {
        var engine = new AurelianEngine();
        AurelianEngineResult start = engine.Start();
        Assert.True(start.Success);
        return new AurelianFramePump(engine, new CompositorActuationBridge(mechanism ?? new RecordingCompositorMechanism([])));
    }

    private static AurelianFrameInput Input(ulong frameId)
    {
        var output = new PlantOutputRef(0, frameId, "offscreen");
        var readiness = new PlantOutputReadiness(output, PlantOutputReadinessStatus.Ready, CompletedFenceValue: frameId);
        var target = new PresentationTargetRef(0, 0, frameId);
        var frameFacts = new CompositorFrameFacts(frameId, [readiness], CompositorDiagnostics.Empty);
        var required = new RequiredPlantOutputSet(frameId, CompositorPolicyKind.Passthrough, [output]);
        return new AurelianFrameInput(new AurelianFrameId(frameId), new CompositorPolicyFacts(frameFacts, required, target, CompositorPolicyKind.Passthrough));
    }

    private static string FormatDiagnostics(AurelianFrameLoopResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private sealed class SingleFrameInputProvider : IAurelianFrameInputProvider
    {
        private readonly AurelianFrameInput input;

        public SingleFrameInputProvider(AurelianFrameInput input)
        {
            this.input = input;
        }

        public ValueTask<AurelianFrameInput?> GetNextFrameInputAsync(AurelianFrameId frameId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AurelianFrameInput?>(input);
    }

    private sealed class RecordingRuntimeTicker : IAurelianRuntimeTicker
    {
        private readonly List<string> calls;
        private readonly AurelianRuntimeTickStatus status;

        public RecordingRuntimeTicker(List<string> calls, AurelianRuntimeTickStatus status)
        {
            this.calls = calls;
            this.status = status;
        }

        public Task<AurelianRuntimeTickResult> TickAsync(AurelianRuntimeTickInput input, CancellationToken cancellationToken = default)
        {
            calls.Add($"runtime:{input.TickIndex}");
            IReadOnlyList<AurelianRuntimeDiagnostic> diagnostics = status == AurelianRuntimeTickStatus.Ticked
                ? []
                : [new AurelianRuntimeDiagnostic(AurelianRuntimeDiagnosticCodes.RunnerFailed, AurelianRuntimeDiagnosticSeverity.Error, "Runtime ticker failed in test.")];
            return Task.FromResult(new AurelianRuntimeTickResult(status, input.TickIndex, input.DeltaTime, diagnostics));
        }
    }

    private sealed class RecordingCompositorMechanism : ICompositorMechanism
    {
        private readonly List<string> calls;

        public RecordingCompositorMechanism(List<string> calls)
        {
            this.calls = calls;
        }

        public Task<CompositorDispatchResult> DispatchAsync(CompositorDispatchRequest request, CancellationToken cancellationToken = default)
        {
            calls.Add($"frame:{request.FrameId}");
            return Task.FromResult(new CompositorDispatchResult(CompositorDispatchStatus.Dispatched, request.FrameId, request.Policy, request.Target, CompositorDiagnostics.Empty, []));
        }
    }
}
