using Dominatus.Core.Runtime;
using Machina.Dominatus.Rendering.Commands;

namespace Machina.Renderer.Raster.Dominatus.Actuation;

public sealed class RasterRenderActuationHandler :
    IActuationHandler<BeginFrameCommand>,
    IActuationHandler<FillRectCommand>,
    IActuationHandler<EndFrameCommand>,
    IActuationHandler<DrawTextCommand>,
    IActuationHandler<PushClipCommand>,
    IActuationHandler<PopClipCommand>
{
    private const string DrawTextMessage = "DrawTextCommand is not supported by Raster M0b. Text rendering is deferred to M0c.";
    private const string PushClipMessage = "PushClipCommand is not supported by Raster M0b.";
    private const string PopClipMessage = "PopClipCommand is not supported by Raster M0b.";

    private readonly RasterRenderRecorder _recorder;

    public RasterRenderActuationHandler(RasterRenderRecorder recorder)
    {
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, BeginFrameCommand cmd)
    {
        _recorder.BeginFrame(cmd.Width, cmd.Height);
        return ActuatorHost.HandlerResult.CompletedOk();
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, FillRectCommand cmd)
    {
        _recorder.FillRect(cmd.Id, cmd.Rect, cmd.Color);
        return ActuatorHost.HandlerResult.CompletedOk();
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, EndFrameCommand cmd)
    {
        _recorder.EndFrame();
        return ActuatorHost.HandlerResult.CompletedOk();
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, DrawTextCommand cmd)
    {
        throw new NotSupportedException(DrawTextMessage);
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, PushClipCommand cmd)
    {
        throw new NotSupportedException(PushClipMessage);
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, PopClipCommand cmd)
    {
        throw new NotSupportedException(PopClipMessage);
    }
}
