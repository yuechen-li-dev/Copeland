using Aurelian.Runtime;
using Xunit;

namespace Aurelian.Runtime.Tests;

public sealed class DominatusRuntimeSmokeTests
{
    [Fact]
    public void DominatusSmokeRuntime_TickOnce_CompletesDeterministically()
    {
        var result = DominatusSmokeRuntime.TickOnce();

        Assert.Equal("SucceededThenReinitialized", result.Status);
        Assert.Equal(1, result.ActiveFrameCount);
        Assert.Equal("aurelian.smoke.root", result.LeafState);
        Assert.Equal(1, result.AgentId);
        Assert.Equal(1, result.ActuationId);
        Assert.Equal("aurelian-dominatus-smoke", result.PayloadMessage);
        Assert.Equal(1f / 60f, result.ClockSeconds);
    }

    [Fact]
    public void DominatusSmokeRuntime_TickOnce_ProducesExpectedTrace()
    {
        var report = DominatusSmokeRuntime.TickOnceAndReport();

        Assert.Equal(
            "SucceededThenReinitialized;frames=1;leaf=aurelian.smoke.root;agent=1;act=1;payload=aurelian-dominatus-smoke;time=0.016667",
            report);
    }
}
