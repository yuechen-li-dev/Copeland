namespace Aurelian.Runtime.Sessions;

public sealed record AurelianRuntimeTickInput(
    ulong TickIndex,
    TimeSpan DeltaTime);
