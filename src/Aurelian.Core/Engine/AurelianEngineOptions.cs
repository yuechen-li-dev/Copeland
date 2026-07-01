using Aurelian.Core.Engine.Graphics;

namespace Aurelian.Core.Engine;

public sealed record AurelianEngineOptions
{
    private AurelianEngineGraphicsOptions graphics = AurelianEngineGraphicsOptions.Headless;

    public AurelianEngineOptions(
        string Name = "Aurelian",
        AurelianEngineGraphicsOptions? Graphics = null)
    {
        this.Name = Name;
        this.Graphics = Graphics ?? AurelianEngineGraphicsOptions.Headless;
    }

    public string Name { get; init; }

    public AurelianEngineGraphicsOptions Graphics
    {
        get => graphics;
        init => graphics = value ?? AurelianEngineGraphicsOptions.Headless;
    }
}
