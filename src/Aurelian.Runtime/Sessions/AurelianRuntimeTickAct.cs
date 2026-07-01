using Dominatus.Core.Runtime;

namespace Aurelian.Runtime.Sessions;

public sealed record AurelianRuntimeTickAct(
    ulong TickIndex) : IActuationCommand;
