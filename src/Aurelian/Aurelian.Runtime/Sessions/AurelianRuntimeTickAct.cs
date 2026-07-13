using Dominatus.Core.Runtime;

namespace Aurelian.Runtime.Sessions;

internal sealed record AurelianRuntimeTickAct(
    ulong TickIndex) : IActuationCommand;
