using Aurelian.Core.Engine.Graphics;
using Aurelian.Core.Engine.Runtime;
using Aurelian.Diagnostics;
using Aurelian.Rendering.Contracts.Presentation;

namespace Aurelian.Core.Engine.Frames;

public sealed class AurelianFrameLoop
{
    private readonly AurelianFramePump? framePump;
    private readonly IAurelianFrameInputProvider? inputProvider;
    private readonly IPresentationMechanism? presentationMechanism;
    private readonly AurelianFrameLoopOptions options;
    private readonly AurelianRuntimeTickFrameStep? runtimeTickStep;

    public AurelianFrameLoop(
        AurelianFramePump framePump,
        IAurelianFrameInputProvider inputProvider,
        IPresentationMechanism? presentationMechanism = null,
        AurelianFrameLoopOptions? options = null,
        AurelianRuntimeTickFrameStep? runtimeTickStep = null)
    {
        this.framePump = framePump;
        this.inputProvider = inputProvider;
        this.presentationMechanism = presentationMechanism;
        this.options = options ?? new AurelianFrameLoopOptions();
        this.runtimeTickStep = runtimeTickStep;
    }

    /// <summary>Runs with bounded completion diagnostics and no frame transcript.</summary>
    public Task<AurelianFrameLoopResult> RunAsync(
        AurelianFrameId startFrame,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(startFrame, iterationSink: null, cancellationToken);
    }

    /// <summary>Runs a finite evidence harness and retains every attempted iteration explicitly.</summary>
    public async Task<AurelianFrameLoopHarnessResult> RunHarnessAsync(
        AurelianFrameId startFrame,
        CancellationToken cancellationToken = default)
    {
        if (options.MaxFrames is null)
        {
            var diagnostic = new AurelianFrameLoopDiagnostic(
                AurelianFrameLoopDiagnosticCodes.HarnessRequiresFiniteMaxFrames,
                AurelianDiagnosticSeverity.Error,
                "Full transcript capture requires an explicit finite MaxFrames value.");
            var rejected = new AurelianFrameLoopResult(
                AurelianFrameLoopStatus.Rejected,
                AurelianFrameLoopStopReason.Rejected,
                0,
                0,
                [diagnostic]);
            return new AurelianFrameLoopHarnessResult(rejected, []);
        }

        var collector = new TranscriptCollector(options.MaxFrames.Value);
        AurelianFrameLoopResult completion = await ExecuteAsync(startFrame, collector, cancellationToken)
            .ConfigureAwait(false);
        return new AurelianFrameLoopHarnessResult(completion, collector.Materialize());
    }

    /// <summary>Streams iteration evidence to a caller-owned sink without retaining it in the loop.</summary>
    public Task<AurelianFrameLoopResult> RunWithSinkAsync(
        AurelianFrameId startFrame,
        IAurelianFrameLoopIterationSink iterationSink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(iterationSink);
        return ExecuteAsync(startFrame, iterationSink, cancellationToken);
    }

    private async Task<AurelianFrameLoopResult> ExecuteAsync(
        AurelianFrameId startFrame,
        IAurelianFrameLoopIterationSink? iterationSink,
        CancellationToken cancellationToken)
    {
        var diagnostics = new BoundedDiagnosticBuffer(options.MaxRetainedDiagnostics);
        AurelianFrameLoopResult? rejected = Validate(diagnostics);
        if (rejected is not null)
        {
            return rejected;
        }

        int framesAttempted = 0;
        int framesCompleted = 0;
        AurelianFrameId frameId = startFrame;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (options.MaxFrames is int maxFrames && framesAttempted >= maxFrames)
                {
                    return Result(
                        AurelianFrameLoopStatus.Completed,
                        AurelianFrameLoopStopReason.MaxFramesReached,
                        framesAttempted,
                        framesCompleted,
                        diagnostics);
                }

                AurelianFrameInput? input = await inputProvider!
                    .GetNextFrameInputAsync(frameId, cancellationToken)
                    .ConfigureAwait(false);

                if (input is null)
                {
                    diagnostics.Add(new AurelianFrameLoopDiagnostic(
                        AurelianFrameLoopDiagnosticCodes.FrameInputMissing,
                        AurelianDiagnosticSeverity.Info,
                        $"Frame input provider completed before frame {frameId}."));

                    return Result(
                        AurelianFrameLoopStatus.Completed,
                        AurelianFrameLoopStopReason.InputProviderCompleted,
                        framesAttempted,
                        framesCompleted,
                        diagnostics);
                }

                if (input.CloseRequest is not null)
                {
                    AurelianEngineResult closeResult = framePump!.AcceptCloseRequest(input.CloseRequest);
                    if (!closeResult.Success)
                    {
                        diagnostics.Add(new AurelianFrameLoopDiagnostic(
                            AurelianFrameLoopDiagnosticCodes.CloseRejected,
                            AurelianDiagnosticSeverity.Error,
                            "Aurelian engine rejected the explicit close request."));

                        return Result(
                            AurelianFrameLoopStatus.Failed,
                            AurelianFrameLoopStopReason.FrameFailed,
                            framesAttempted,
                            framesCompleted,
                            diagnostics);
                    }

                    diagnostics.Add(new AurelianFrameLoopDiagnostic(
                        AurelianFrameLoopDiagnosticCodes.CloseAccepted,
                        AurelianDiagnosticSeverity.Info,
                        "Aurelian engine accepted the explicit close request before a new frame began."));

                    return Result(
                        AurelianFrameLoopStatus.Completed,
                        AurelianFrameLoopStopReason.CloseRequested,
                        framesAttempted,
                        framesCompleted,
                        diagnostics);
                }

                framesAttempted++;
                AurelianRuntimeTickFrameStepResult? runtimeTickResult = null;
                if (runtimeTickStep is not null)
                {
                    runtimeTickResult = await runtimeTickStep
                        .RunAsync(input.FrameId, options.RuntimeDeltaTime, cancellationToken)
                        .ConfigureAwait(false);

                    if (!runtimeTickResult.Success)
                    {
                        diagnostics.Add(CreateRuntimeTickFailureDiagnostic(runtimeTickResult));

                        return Result(
                            runtimeTickResult.Status == AurelianRuntimeTickFrameStepStatus.Cancelled
                                ? AurelianFrameLoopStatus.Cancelled
                                : AurelianFrameLoopStatus.Failed,
                            runtimeTickResult.Status == AurelianRuntimeTickFrameStepStatus.Cancelled
                                ? AurelianFrameLoopStopReason.Cancelled
                                : AurelianFrameLoopStopReason.FrameFailed,
                            framesAttempted,
                            framesCompleted,
                            diagnostics);
                    }
                }

                AurelianFrameResult frameResult = await framePump!
                    .RunOneFrameAsync(input, cancellationToken)
                    .ConfigureAwait(false);

                bool presented = false;
                if (frameResult.Success)
                {
                    framesCompleted++;
                    if (options.PresentAfterCompletedFrame && presentationMechanism is not null)
                    {
                        try
                        {
                            await presentationMechanism.PresentAsync(cancellationToken).ConfigureAwait(false);
                            presented = true;
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            diagnostics.Add(new AurelianFrameLoopDiagnostic(
                                AurelianFrameLoopDiagnosticCodes.PresentationFailed,
                                AurelianDiagnosticSeverity.Error,
                                $"Frame {input.FrameId} presentation failed: {ex.Message}"));

                            return Result(
                                AurelianFrameLoopStatus.Failed,
                                AurelianFrameLoopStopReason.FrameFailed,
                                framesAttempted,
                                framesCompleted,
                                diagnostics);
                        }
                    }
                }

                if (iterationSink is not null)
                {
                    iterationSink.OnIteration(new AurelianFrameLoopIterationResult(
                        input.FrameId,
                        runtimeTickResult,
                        frameResult,
                        presented));
                }

                if (!frameResult.Success)
                {
                    diagnostics.Add(new AurelianFrameLoopDiagnostic(
                        AurelianFrameLoopDiagnosticCodes.FrameFailed,
                        AurelianDiagnosticSeverity.Error,
                        $"Frame {input.FrameId} ended with status {frameResult.Status}."));

                    if (options.StopOnFrameFailure)
                    {
                        return Result(
                            AurelianFrameLoopStatus.Failed,
                            AurelianFrameLoopStopReason.FrameFailed,
                            framesAttempted,
                            framesCompleted,
                            diagnostics);
                    }
                }

                frameId = input.FrameId.Next();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            diagnostics.Add(new AurelianFrameLoopDiagnostic(
                AurelianFrameLoopDiagnosticCodes.Cancelled,
                AurelianDiagnosticSeverity.Warning,
                "Aurelian frame loop run was canceled."));

            return Result(
                AurelianFrameLoopStatus.Cancelled,
                AurelianFrameLoopStopReason.Cancelled,
                framesAttempted,
                framesCompleted,
                diagnostics);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Add(new AurelianFrameLoopDiagnostic(
                AurelianFrameLoopDiagnosticCodes.FrameFailed,
                AurelianDiagnosticSeverity.Error,
                $"Frame loop failed: {ex.Message}"));

            return Result(
                AurelianFrameLoopStatus.Failed,
                AurelianFrameLoopStopReason.FrameFailed,
                framesAttempted,
                framesCompleted,
                diagnostics);
        }
    }

    private AurelianFrameLoopResult? Validate(BoundedDiagnosticBuffer diagnostics)
    {
        if (options.MaxRetainedDiagnostics <= 0)
        {
            diagnostics.Add(new AurelianFrameLoopDiagnostic(
                AurelianFrameLoopDiagnosticCodes.InvalidDiagnosticCapacity,
                AurelianDiagnosticSeverity.Error,
                "Aurelian frame loop MaxRetainedDiagnostics must be greater than zero."));
            return Result(AurelianFrameLoopStatus.Rejected, AurelianFrameLoopStopReason.Rejected, 0, 0, diagnostics);
        }

        if (framePump is null)
        {
            diagnostics.Add(new AurelianFrameLoopDiagnostic(
                AurelianFrameLoopDiagnosticCodes.FramePumpMissing,
                AurelianDiagnosticSeverity.Error,
                "Aurelian frame loop requires an existing frame pump."));
            return Result(AurelianFrameLoopStatus.Rejected, AurelianFrameLoopStopReason.Rejected, 0, 0, diagnostics);
        }

        if (inputProvider is null)
        {
            diagnostics.Add(new AurelianFrameLoopDiagnostic(
                AurelianFrameLoopDiagnosticCodes.InputProviderMissing,
                AurelianDiagnosticSeverity.Error,
                "Aurelian frame loop requires a frame input provider."));
            return Result(AurelianFrameLoopStatus.Rejected, AurelianFrameLoopStopReason.Rejected, 0, 0, diagnostics);
        }

        if (options.MaxFrames is <= 0)
        {
            diagnostics.Add(new AurelianFrameLoopDiagnostic(
                AurelianFrameLoopDiagnosticCodes.InvalidMaxFrames,
                AurelianDiagnosticSeverity.Error,
                "Aurelian frame loop MaxFrames must be greater than zero when provided."));
            return Result(AurelianFrameLoopStatus.Rejected, AurelianFrameLoopStopReason.Rejected, 0, 0, diagnostics);
        }

        return null;
    }

    private static AurelianFrameLoopDiagnostic CreateRuntimeTickFailureDiagnostic(
        AurelianRuntimeTickFrameStepResult runtimeTickResult)
    {
        string code = runtimeTickResult.Status switch
        {
            AurelianRuntimeTickFrameStepStatus.Rejected => AurelianFrameLoopDiagnosticCodes.RuntimeTickRejected,
            AurelianRuntimeTickFrameStepStatus.Cancelled => AurelianFrameLoopDiagnosticCodes.RuntimeTickCancelled,
            _ => AurelianFrameLoopDiagnosticCodes.RuntimeTickFailed,
        };

        AurelianDiagnosticSeverity severity = runtimeTickResult.Status == AurelianRuntimeTickFrameStepStatus.Cancelled
            ? AurelianDiagnosticSeverity.Warning
            : AurelianDiagnosticSeverity.Error;
        string detail = runtimeTickResult.Diagnostics.Count > 0
            ? string.Join("; ", runtimeTickResult.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"))
            : $"Runtime tick frame step ended with status {runtimeTickResult.Status}.";

        return new AurelianFrameLoopDiagnostic(
            code,
            severity,
            $"Frame {runtimeTickResult.FrameId} runtime tick did not complete successfully: {detail}");
    }

    private static AurelianFrameLoopResult Result(
        AurelianFrameLoopStatus status,
        AurelianFrameLoopStopReason stopReason,
        int framesAttempted,
        int framesCompleted,
        BoundedDiagnosticBuffer diagnostics)
    {
        return new AurelianFrameLoopResult(
            status,
            stopReason,
            framesAttempted,
            framesCompleted,
            diagnostics.Materialize(),
            diagnostics.DroppedCount);
    }

    private sealed class TranscriptCollector : IAurelianFrameLoopIterationSink
    {
        private readonly List<AurelianFrameLoopIterationResult> iterations;

        public TranscriptCollector(int capacity)
        {
            iterations = new List<AurelianFrameLoopIterationResult>(capacity);
        }

        public void OnIteration(AurelianFrameLoopIterationResult iteration)
        {
            iterations.Add(iteration);
        }

        public IReadOnlyList<AurelianFrameLoopIterationResult> Materialize()
        {
            return iterations.ToArray();
        }
    }

    private sealed class BoundedDiagnosticBuffer
    {
        private readonly int capacity;
        private readonly List<AurelianFrameLoopDiagnostic> diagnostics;

        public BoundedDiagnosticBuffer(int capacity)
        {
            this.capacity = Math.Max(1, capacity);
            diagnostics = new List<AurelianFrameLoopDiagnostic>(Math.Min(this.capacity, 16));
        }

        public int DroppedCount { get; private set; }

        public void Add(AurelianFrameLoopDiagnostic diagnostic)
        {
            if (diagnostics.Count < capacity)
            {
                diagnostics.Add(diagnostic);
                return;
            }

            DroppedCount++;
        }

        public IReadOnlyList<AurelianFrameLoopDiagnostic> Materialize()
        {
            return diagnostics.Count == 0
                ? Array.Empty<AurelianFrameLoopDiagnostic>()
                : diagnostics.ToArray();
        }
    }
}
