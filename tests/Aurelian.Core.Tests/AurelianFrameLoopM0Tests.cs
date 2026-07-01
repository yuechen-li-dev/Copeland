using Aurelian.Core.Compositor;
using Aurelian.Core.Engine;
using Aurelian.Core.Engine.Frames;
using Aurelian.Core.Engine.Graphics;
using Aurelian.Rendering.Contracts.Compositor;
using Aurelian.Runtime.Compositor;
using Xunit;

namespace Aurelian.Core.Tests;

public sealed class AurelianFrameLoopM0Tests
{
    [Fact]
    public async Task AurelianFrameLoop_RunAsync_RejectsMissingFramePump()
    {
        var loop = new AurelianFrameLoop(null!, new FakeFrameInputProvider(Input(1)));

        AurelianFrameLoopResult result = await loop.RunAsync(AurelianFrameId.Zero);

        Assert.False(result.Success);
        Assert.Equal(AurelianFrameLoopStatus.Rejected, result.Status);
        Assert.Equal(AurelianFrameLoopStopReason.Rejected, result.StopReason);
        Assert.Equal(AurelianFrameLoopDiagnosticCodes.FramePumpMissing, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task AurelianFrameLoop_RunAsync_RejectsMissingInputProvider()
    {
        var loop = new AurelianFrameLoop(StartedPump(), null!);

        AurelianFrameLoopResult result = await loop.RunAsync(AurelianFrameId.Zero);

        Assert.False(result.Success);
        Assert.Equal(AurelianFrameLoopStatus.Rejected, result.Status);
        Assert.Equal(AurelianFrameLoopDiagnosticCodes.InputProviderMissing, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task AurelianFrameLoop_RunAsync_RejectsInvalidMaxFrames()
    {
        var loop = new AurelianFrameLoop(StartedPump(), new FakeFrameInputProvider(Input(1)), options: new AurelianFrameLoopOptions(MaxFrames: 0));

        AurelianFrameLoopResult result = await loop.RunAsync(AurelianFrameId.Zero);

        Assert.False(result.Success);
        Assert.Equal(AurelianFrameLoopStatus.Rejected, result.Status);
        Assert.Equal(AurelianFrameLoopDiagnosticCodes.InvalidMaxFrames, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task AurelianFrameLoop_RunAsync_CompletesOneFrame()
    {
        var provider = new FakeFrameInputProvider(Input(7));
        var loop = new AurelianFrameLoop(StartedPump(), provider);

        AurelianFrameLoopResult result = await loop.RunAsync(new AurelianFrameId(7));

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.Equal(AurelianFrameLoopStatus.Completed, result.Status);
        Assert.Equal(AurelianFrameLoopStopReason.MaxFramesReached, result.StopReason);
        Assert.Equal(1, result.FramesAttempted);
        Assert.Equal(1, result.FramesCompleted);
        Assert.Equal(new AurelianFrameId(7), Assert.Single(provider.RequestedFrames));
        Assert.False(Assert.Single(result.Iterations).Presented);
    }

    [Fact]
    public async Task AurelianFrameLoop_RunAsync_CompletesMultipleFramesUntilMaxFrames()
    {
        var provider = new SequenceFrameInputProvider(frameId => Input(frameId.Value));
        var loop = new AurelianFrameLoop(StartedPump(), provider, options: new AurelianFrameLoopOptions(MaxFrames: 3));

        AurelianFrameLoopResult result = await loop.RunAsync(new AurelianFrameId(10));

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.Equal(AurelianFrameLoopStopReason.MaxFramesReached, result.StopReason);
        Assert.Equal(3, result.FramesAttempted);
        Assert.Equal(3, result.FramesCompleted);
        Assert.Equal([new AurelianFrameId(10), new AurelianFrameId(11), new AurelianFrameId(12)], provider.RequestedFrames);
    }

    [Fact]
    public async Task AurelianFrameLoop_RunAsync_StopsWhenProviderReturnsNull()
    {
        var provider = new SequenceFrameInputProvider(_ => null);
        var loop = new AurelianFrameLoop(StartedPump(), provider, options: new AurelianFrameLoopOptions(MaxFrames: null));

        AurelianFrameLoopResult result = await loop.RunAsync(new AurelianFrameId(20));

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.Equal(AurelianFrameLoopStopReason.InputProviderCompleted, result.StopReason);
        Assert.Equal(0, result.FramesAttempted);
        Assert.Equal(AurelianFrameLoopDiagnosticCodes.FrameInputMissing, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task AurelianFrameLoop_RunAsync_PresentsAfterCompletedFrameWhenMechanismProvided()
    {
        var presentation = new FakePresentationMechanism();
        var loop = new AurelianFrameLoop(StartedPump(), new FakeFrameInputProvider(Input(30)), presentation);

        AurelianFrameLoopResult result = await loop.RunAsync(new AurelianFrameId(30));

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.Equal(1, presentation.PresentCount);
        Assert.True(Assert.Single(result.Iterations).Presented);
    }

    [Fact]
    public async Task AurelianFrameLoop_RunAsync_DoesNotRequirePresentationMechanism()
    {
        var loop = new AurelianFrameLoop(StartedPump(), new FakeFrameInputProvider(Input(40)), presentationMechanism: null);

        AurelianFrameLoopResult result = await loop.RunAsync(new AurelianFrameId(40));

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.False(Assert.Single(result.Iterations).Presented);
    }

    [Fact]
    public async Task AurelianFrameLoop_RunAsync_StopsOnFrameFailureWhenConfigured()
    {
        var loop = new AurelianFrameLoop(StartedPump(new FakeCompositorMechanism(CompositorDispatchStatus.Failed)), new FakeFrameInputProvider(Input(50)));

        AurelianFrameLoopResult result = await loop.RunAsync(new AurelianFrameId(50));

        Assert.False(result.Success);
        Assert.Equal(AurelianFrameLoopStatus.Failed, result.Status);
        Assert.Equal(AurelianFrameLoopStopReason.FrameFailed, result.StopReason);
        Assert.Equal(1, result.FramesAttempted);
        Assert.Equal(0, result.FramesCompleted);
        Assert.Equal(AurelianFrameLoopDiagnosticCodes.FrameFailed, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task AurelianFrameLoop_RunAsync_CanContinueAfterFrameFailureWhenConfigured()
    {
        var provider = new SequenceFrameInputProvider(frameId => Input(frameId.Value));
        var loop = new AurelianFrameLoop(
            StartedPump(new AlternatingCompositorMechanism()),
            provider,
            options: new AurelianFrameLoopOptions(MaxFrames: 2, StopOnFrameFailure: false));

        AurelianFrameLoopResult result = await loop.RunAsync(new AurelianFrameId(60));

        Assert.False(result.Success);
        Assert.Equal(AurelianFrameLoopStatus.Completed, result.Status);
        Assert.Equal(AurelianFrameLoopStopReason.MaxFramesReached, result.StopReason);
        Assert.Equal(2, result.FramesAttempted);
        Assert.Equal(1, result.FramesCompleted);
        Assert.Equal([AurelianFrameStatus.Failed, AurelianFrameStatus.Completed], result.Iterations.Select(x => x.FrameResult.Status));
    }

    [Fact]
    public async Task AurelianFrameLoop_RunAsync_CancellationReturnsCancelled()
    {
        var provider = new CancellingFrameInputProvider();
        var loop = new AurelianFrameLoop(StartedPump(), provider, options: new AurelianFrameLoopOptions(MaxFrames: null));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        AurelianFrameLoopResult result = await loop.RunAsync(new AurelianFrameId(70), cts.Token);

        Assert.False(result.Success);
        Assert.Equal(AurelianFrameLoopStatus.Cancelled, result.Status);
        Assert.Equal(AurelianFrameLoopStopReason.Cancelled, result.StopReason);
        Assert.Equal(AurelianFrameLoopDiagnosticCodes.Cancelled, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void AurelianFrameLoop_DoesNotCreateVulkanOrWindowResources()
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

    private static AurelianFramePump StartedPump(ICompositorMechanism? mechanism = null)
    {
        var engine = new AurelianEngine();
        AurelianEngineResult start = engine.Start();
        Assert.True(start.Success);
        return new AurelianFramePump(engine, new CompositorActuationBridge(mechanism ?? new FakeCompositorMechanism()));
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

    private sealed class FakeFrameInputProvider : IAurelianFrameInputProvider
    {
        private readonly AurelianFrameInput? input;

        public FakeFrameInputProvider(AurelianFrameInput? input)
        {
            this.input = input;
        }

        public List<AurelianFrameId> RequestedFrames { get; } = [];

        public ValueTask<AurelianFrameInput?> GetNextFrameInputAsync(AurelianFrameId frameId, CancellationToken cancellationToken = default)
        {
            RequestedFrames.Add(frameId);
            return ValueTask.FromResult(input);
        }
    }

    private sealed class SequenceFrameInputProvider : IAurelianFrameInputProvider
    {
        private readonly Func<AurelianFrameId, AurelianFrameInput?> next;

        public SequenceFrameInputProvider(Func<AurelianFrameId, AurelianFrameInput?> next)
        {
            this.next = next;
        }

        public List<AurelianFrameId> RequestedFrames { get; } = [];

        public ValueTask<AurelianFrameInput?> GetNextFrameInputAsync(AurelianFrameId frameId, CancellationToken cancellationToken = default)
        {
            RequestedFrames.Add(frameId);
            return ValueTask.FromResult(next(frameId));
        }
    }

    private sealed class CancellingFrameInputProvider : IAurelianFrameInputProvider
    {
        public ValueTask<AurelianFrameInput?> GetNextFrameInputAsync(AurelianFrameId frameId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<AurelianFrameInput?>(Input(frameId.Value));
        }
    }

    private sealed class FakePresentationMechanism : IPresentationMechanism
    {
        public int PresentCount { get; private set; }

        public Task PresentAsync(CancellationToken cancellationToken = default)
        {
            PresentCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCompositorMechanism : ICompositorMechanism
    {
        private readonly CompositorDispatchStatus status;

        public FakeCompositorMechanism(CompositorDispatchStatus status = CompositorDispatchStatus.Dispatched)
        {
            this.status = status;
        }

        public Task<CompositorDispatchResult> DispatchAsync(CompositorDispatchRequest request, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<CompositorDispatchDiagnostic> diagnostics = status == CompositorDispatchStatus.Failed
                ? [new CompositorDispatchDiagnostic("ACOMP-FAKE", CompositorDispatchDiagnosticSeverity.Error, "Fake compositor dispatch failure.")]
                : [];
            return Task.FromResult(new CompositorDispatchResult(status, request.FrameId, request.Policy, request.Target, CompositorDiagnostics.Empty, diagnostics));
        }
    }

    private sealed class AlternatingCompositorMechanism : ICompositorMechanism
    {
        private int count;

        public Task<CompositorDispatchResult> DispatchAsync(CompositorDispatchRequest request, CancellationToken cancellationToken = default)
        {
            count++;
            CompositorDispatchStatus status = count == 1 ? CompositorDispatchStatus.Failed : CompositorDispatchStatus.Dispatched;
            IReadOnlyList<CompositorDispatchDiagnostic> diagnostics = status == CompositorDispatchStatus.Failed
                ? [new CompositorDispatchDiagnostic("ACOMP-FAKE", CompositorDispatchDiagnosticSeverity.Error, "Fake compositor dispatch failure.")]
                : [];
            return Task.FromResult(new CompositorDispatchResult(status, request.FrameId, request.Policy, request.Target, CompositorDiagnostics.Empty, diagnostics));
        }
    }
}
