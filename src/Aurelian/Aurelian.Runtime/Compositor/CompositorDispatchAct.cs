using Aurelian.Rendering.Contracts.Compositor;
using Dominatus.Core.Runtime;

namespace Aurelian.Runtime.Compositor;

/// <summary>
/// Runtime-only Dominatus actuation command that asks a compositor actuator to execute a neutral request.
/// </summary>
internal sealed record CompositorDispatchAct(
    CompositorDispatchRequest Request)
    : IActuationCommand;
