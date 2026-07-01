using Aurelian.Rendering.Contracts.Compositor;
using Aurelian.Runtime.Compositor;
using Dominatus.Core.Runtime;

namespace Aurelian.Integration.Tests.Support;

internal sealed class CapturingCompositorDispatchHandler : IActuationHandler<CompositorDispatchAct>
{
    private readonly Func<CompositorDispatchAct, CompositorDispatchResult> _dispatch;

    public CapturingCompositorDispatchHandler(Func<CompositorDispatchAct, CompositorDispatchResult> dispatch)
    {
        _dispatch = dispatch;
    }

    public List<CompositorDispatchAct> Acts { get; } = [];

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, CompositorDispatchAct cmd)
    {
        Acts.Add(cmd);
        return ActuatorHost.HandlerResult.CompletedWithPayload(_dispatch(cmd), ok: true);
    }
}
