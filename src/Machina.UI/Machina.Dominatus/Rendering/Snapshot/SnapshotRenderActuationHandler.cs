using Dominatus.Core.Runtime;
using Machina.Dominatus.Rendering.Commands;

namespace Machina.Dominatus.Rendering.Snapshot;

public sealed class SnapshotRenderActuationHandler :
    IActuationHandler<BeginFrameCommand>,
    IActuationHandler<EndFrameCommand>,
    IActuationHandler<FillRectCommand>,
    IActuationHandler<StrokeRectCommand>,
    IActuationHandler<DrawTextCommand>,
    IActuationHandler<PushClipCommand>,
    IActuationHandler<PopClipCommand>
{
    private readonly RenderSnapshotRecorder _recorder;

    public SnapshotRenderActuationHandler(RenderSnapshotRecorder recorder)
    {
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, BeginFrameCommand cmd)
    {
        _recorder.Record(cmd);
        return ActuatorHost.HandlerResult.CompletedOk();
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, EndFrameCommand cmd)
    {
        _recorder.Record(cmd);
        return ActuatorHost.HandlerResult.CompletedOk();
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, FillRectCommand cmd)
    {
        _recorder.Record(cmd);
        return ActuatorHost.HandlerResult.CompletedOk();
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, StrokeRectCommand cmd)
    {
        _recorder.Record(cmd);
        return ActuatorHost.HandlerResult.CompletedOk();
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, DrawTextCommand cmd)
    {
        _recorder.Record(cmd);
        return ActuatorHost.HandlerResult.CompletedOk();
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, PushClipCommand cmd)
    {
        _recorder.Record(cmd);
        return ActuatorHost.HandlerResult.CompletedOk();
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, PopClipCommand cmd)
    {
        _recorder.Record(cmd);
        return ActuatorHost.HandlerResult.CompletedOk();
    }
}
