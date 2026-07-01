using Aurelian.World;

namespace Aurelian.Runtime;

public sealed class AurelianRuntime
{
    public WorldClock Clock { get; private set; }

    public void Tick()
    {
        Clock = new WorldClock(Clock.Tick + 1);
    }
}
