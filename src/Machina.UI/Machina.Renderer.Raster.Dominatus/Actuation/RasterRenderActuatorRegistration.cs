using Dominatus.Core.Runtime;
using Machina.Dominatus.Rendering.Commands;

namespace Machina.Renderer.Raster.Dominatus.Actuation;

public static class RasterRenderActuatorRegistration
{
    public static ActuatorHost AddRasterRenderer(
        this ActuatorHost host,
        RasterRenderRecorder recorder,
        RasterRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(recorder);

        var handler = new RasterRenderActuationHandler(recorder, options ?? new RasterRenderOptions());

        host.Register<BeginFrameCommand>(handler);
        host.Register<FillRectCommand>(handler);
        host.Register<StrokeRectCommand>(handler);
        host.Register<EndFrameCommand>(handler);
        host.Register<DrawTextCommand>(handler);
        host.Register<PushClipCommand>(handler);
        host.Register<PopClipCommand>(handler);

        return host;
    }
}
