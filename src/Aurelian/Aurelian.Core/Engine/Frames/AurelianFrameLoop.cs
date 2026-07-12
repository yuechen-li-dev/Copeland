using Aurelian.Core.Engine.Graphics;
using Aurelian.Core.Engine.Runtime;

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

    public async Task<AurelianFrameLoopResult> RunAsync(
        AurelianFrameId startFrame,
        CancellationToken cancellationToken = default)
    {
        List<AurelianFrameLoopIterationResult> iterations = [];
        List<AurelianFrameLoopDiagnostic> diagnostics = [];

        AurelianFrameLoopResult? rejected = Validate(iterations, diagnostics);
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
                        iterations,
                        diagnostics);
                }

                AurelianFrameInput? input = await inputProvider!
                    .GetNextFrameInputAsync(frameId, cancellationToken)
                    .ConfigureAwait(false);

                if (input is null)
                {
                    diagnostics.Add(new AurelianFrameLoopDiagnostic(
                        AurelianFrameLoopDiagnosticCodes.FrameInputMissing,
                        AurelianFrameLoopDiagnosticSeverity.Info,
                        $"Frame input provider completed before frame {frameId}."));

                    return Result(
                        AurelianFrameLoopStatus.Completed,
                        AurelianFrameLoopStopReason.InputProviderCompleted,
                        framesAttempted,
                        framesCompleted,
                        iterations,
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
                        diagnostics.Add(MapRuntimeTickDiagnostic(runtimeTickResult));

                        return Result(
                            runtimeTickResult.Status == AurelianRuntimeTickFrameStepStatus.Cancelled
                                ? AurelianFrameLoopStatus.Cancelled
                                : AurelianFrameLoopStatus.Failed,
                            runtimeTickResult.Status == AurelianRuntimeTickFrameStepStatus.Cancelled
                                ? AurelianFrameLoopStopReason.Cancelled
                                : AurelianFrameLoopStopReason.FrameFailed,
                            framesAttempted,
                            framesCompleted,
                            iterations,
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
                                AurelianFrameLoopDiagnosticSeverity.Error,
                                $"Frame {input.FrameId} presentation failed: {ex.Message}"));

                            return Result(
                                AurelianFrameLoopStatus.Failed,
                                AurelianFrameLoopStopReason.FrameFailed,
                                framesAttempted,
                                framesCompleted,
                                iterations,
                                diagnostics);
                        }
                    }
                }

                iterations.Add(new AurelianFrameLoopIterationResult(input.FrameId, runtimeTickResult, frameResult, presented));

                if (!frameResult.Success)
                {
                    diagnostics.Add(new AurelianFrameLoopDiagnostic(
                        AurelianFrameLoopDiagnosticCodes.FrameFailed,
                        AurelianFrameLoopDiagnosticSeverity.Error,
                        $"Frame {input.FrameId} ended with status {frameResult.Status}."));

                    if (options.StopOnFrameFailure)
                    {
                        return Result(
                            AurelianFrameLoopStatus.Failed,
                            AurelianFrameLoopStopReason.FrameFailed,
                            framesAttempted,
                            framesCompleted,
                            iterations,
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
                AurelianFrameLoopDiagnosticSeverity.Warning,
                "Aurelian frame loop run was canceled."));

            return Result(
                AurelianFrameLoopStatus.Cancelled,
                AurelianFrameLoopStopReason.Cancelled,
                framesAttempted,
                framesCompleted,
                iterations,
                diagnostics);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Add(new AurelianFrameLoopDiagnostic(
                AurelianFrameLoopDiagnosticCodes.FrameFailed,
                AurelianFrameLoopDiagnosticSeverity.Error,
                $"Frame loop failed: {ex.Message}"));

            return Result(
                AurelianFrameLoopStatus.Failed,
                AurelianFrameLoopStopReason.FrameFailed,
                framesAttempted,
                framesCompleted,
                iterations,
                diagnostics);
        }
    }

    private AurelianFrameLoopResult? Validate(
        List<AurelianFrameLoopIterationResult> iterations,
        List<AurelianFrameLoopDiagnostic> diagnostics)
    {
        if (framePump is null)
        {
            diagnostics.Add(new AurelianFrameLoopDiagnostic(
                AurelianFrameLoopDiagnosticCodes.FramePumpMissing,
                AurelianFrameLoopDiagnosticSeverity.Error,
                "Aurelian frame loop requires an existing frame pump."));
            return Result(AurelianFrameLoopStatus.Rejected, AurelianFrameLoopStopReason.Rejected, 0, 0, iterations, diagnostics);
        }

        if (inputProvider is null)
        {
            diagnostics.Add(new AurelianFrameLoopDiagnostic(
                AurelianFrameLoopDiagnosticCodes.InputProviderMissing,
                AurelianFrameLoopDiagnosticSeverity.Error,
                "Aurelian frame loop requires a frame input provider."));
            return Result(AurelianFrameLoopStatus.Rejected, AurelianFrameLoopStopReason.Rejected, 0, 0, iterations, diagnostics);
        }

        if (options.MaxFrames is <= 0)
        {
            diagnostics.Add(new AurelianFrameLoopDiagnostic(
                AurelianFrameLoopDiagnosticCodes.InvalidMaxFrames,
                AurelianFrameLoopDiagnosticSeverity.Error,
                "Aurelian frame loop MaxFrames must be greater than zero when provided."));
            return Result(AurelianFrameLoopStatus.Rejected, AurelianFrameLoopStopReason.Rejected, 0, 0, iterations, diagnostics);
        }

        return null;
    }

    private static AurelianFrameLoopDiagnostic MapRuntimeTickDiagnostic(AurelianRuntimeTickFrameStepResult runtimeTickResult)
    {
        string code = runtimeTickResult.Status switch
        {
            AurelianRuntimeTickFrameStepStatus.Rejected => AurelianFrameLoopDiagnosticCodes.RuntimeTickRejected,
            AurelianRuntimeTickFrameStepStatus.Cancelled => AurelianFrameLoopDiagnosticCodes.RuntimeTickCancelled,
            _ => AurelianFrameLoopDiagnosticCodes.RuntimeTickFailed,
        };

        AurelianFrameLoopDiagnosticSeverity severity = runtimeTickResult.Status == AurelianRuntimeTickFrameStepStatus.Cancelled
            ? AurelianFrameLoopDiagnosticSeverity.Warning
            : AurelianFrameLoopDiagnosticSeverity.Error;
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
        IReadOnlyList<AurelianFrameLoopIterationResult> iterations,
        IReadOnlyList<AurelianFrameLoopDiagnostic> diagnostics) =>
        new(status, stopReason, framesAttempted, framesCompleted, iterations.ToArray(), diagnostics.ToArray());
}
