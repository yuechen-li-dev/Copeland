using Dominatus.Core.Blackboard;
using Dominatus.Core.Runtime;

namespace Aurelian.Runtime.Sessions;

internal static class AurelianRuntimeSessionKeys
{
    public static readonly BbKey<AurelianRuntimeSessionFacts> Facts = new("aurelian.runtime.session.facts");
    public static readonly BbKey<ActuationId> TickActuationId = new("aurelian.runtime.session.tickActuationId");
}
