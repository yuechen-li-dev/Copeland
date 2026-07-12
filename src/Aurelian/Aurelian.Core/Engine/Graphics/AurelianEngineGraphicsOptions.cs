namespace Aurelian.Core.Engine.Graphics;

public sealed record AurelianEngineGraphicsOptions(
    AurelianEngineGraphicsMode Mode,
    AurelianEngineGraphicsOwnership Ownership)
{
    public static AurelianEngineGraphicsOptions Headless { get; } =
        new(AurelianEngineGraphicsMode.Headless, AurelianEngineGraphicsOwnership.External);

    public static AurelianEngineGraphicsOptions PreparedVisible { get; } =
        new(AurelianEngineGraphicsMode.PreparedVisible, AurelianEngineGraphicsOwnership.External);
}
