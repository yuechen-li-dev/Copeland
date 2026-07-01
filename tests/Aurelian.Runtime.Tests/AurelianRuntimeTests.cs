using Aurelian.Runtime;
using Xunit;

namespace Aurelian.Runtime.Tests;

public sealed class AurelianRuntimeTests
{
    [Fact]
    public void Tick_AdvancesClock()
    {
        var runtime = new AurelianRuntime();

        runtime.Tick();

        Assert.Equal(1UL, runtime.Clock.Tick);
    }
}
