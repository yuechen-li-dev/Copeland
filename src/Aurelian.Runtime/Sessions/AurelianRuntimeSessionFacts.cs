namespace Aurelian.Runtime.Sessions;

public sealed record AurelianRuntimeSessionFacts(
    ulong TickIndex,
    TimeSpan DeltaTime);
