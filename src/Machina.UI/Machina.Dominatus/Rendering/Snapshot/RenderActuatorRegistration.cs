using Dominatus.Core.Runtime;
using Machina.Dominatus.Rendering.Commands;

namespace Machina.Dominatus.Rendering.Snapshot;

public static class RenderActuatorRegistration
{
    public static ActuatorHost AddSnapshotRenderer(
        this ActuatorHost host,
        RenderSnapshotRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(recorder);

        var handler = new SnapshotRenderActuationHandler(recorder);

        host.Register<BeginFrameCommand>(handler);
        host.Register<EndFrameCommand>(handler);
        host.Register<FillRectCommand>(handler);
        host.Register<StrokeRectCommand>(handler);
        host.Register<DrawTextCommand>(handler);
        host.Register<PushClipCommand>(handler);
        host.Register<PopClipCommand>(handler);

        return host;
    }
}
