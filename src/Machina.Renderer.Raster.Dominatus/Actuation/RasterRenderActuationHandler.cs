using Dominatus.Core.Runtime;
using Machina.Dominatus.Rendering.Commands;

namespace Machina.Renderer.Raster.Dominatus.Actuation;

public sealed class RasterRenderActuationHandler :
    IActuationHandler<BeginFrameCommand>,
    IActuationHandler<FillRectCommand>,
    IActuationHandler<StrokeRectCommand>,
    IActuationHandler<EndFrameCommand>,
    IActuationHandler<DrawTextCommand>,
    IActuationHandler<PushClipCommand>,
    IActuationHandler<PopClipCommand>
{
    private const string DrawTextMessage = "DrawTextCommand is not supported because no text rasterizer is registered.";

    private readonly RasterRenderRecorder _recorder;
    private readonly RasterRenderOptions _options;

    public RasterRenderActuationHandler(RasterRenderRecorder recorder, RasterRenderOptions options)
    {
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _options = options ?? throw new ArgumentNullException(nameof(options));
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

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, StrokeRectCommand cmd)
    {
        _recorder.StrokeRect(cmd.Id, cmd.Rect, cmd.Color, cmd.Thickness);
        return ActuatorHost.HandlerResult.CompletedOk();
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, EndFrameCommand cmd)
    {
        _recorder.EndFrame();
        return ActuatorHost.HandlerResult.CompletedOk();
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, DrawTextCommand cmd)
    {
        if (_options.TextRasterizer is null)
        {
            throw new NotSupportedException(DrawTextMessage);
        }

        _recorder.DrawText(cmd.Id, cmd.Rect, cmd.Text, cmd.Style, _options.TextRasterizer);
        return ActuatorHost.HandlerResult.CompletedOk();
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, PushClipCommand cmd)
    {
        _recorder.PushClip(cmd.Id, cmd.Rect);
        return ActuatorHost.HandlerResult.CompletedOk();
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, PopClipCommand cmd)
    {
        _recorder.PopClip();
        return ActuatorHost.HandlerResult.CompletedOk();
    }
}
