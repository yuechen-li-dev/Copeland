using Aurelian.World;
using Xunit;

namespace Aurelian.World.Tests;

public sealed class WorldClockTests
{
    [Fact]
    public void WorldClock_StoresTick()
    {
        Assert.Equal(11UL, new WorldClock(11).Tick);
    }
}
