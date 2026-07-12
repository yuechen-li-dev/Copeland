using System.Globalization;

namespace Aurelian.Core.Engine.Frames;

public readonly record struct AurelianFrameId(ulong Value)
{
    public static AurelianFrameId Zero { get; } = new(0);

    public AurelianFrameId Next() => new(Value + 1);

    public override string ToString()
        => Value.ToString(CultureInfo.InvariantCulture);
}
