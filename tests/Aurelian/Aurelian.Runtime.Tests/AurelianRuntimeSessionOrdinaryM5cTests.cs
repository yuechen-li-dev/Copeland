using Aurelian.Runtime.Sessions;
using Xunit;

namespace Aurelian.Runtime.Tests;

public sealed class AurelianRuntimeSessionOrdinaryM5cTests
{
    [Fact]
    public async Task SessionLifecycle_UsesOnlyAurelianContracts()
    {
        var session = new AurelianRuntimeSession();

        Assert.True(session.Start().Success);

        AurelianRuntimeTickResult tick = await session.TickAsync(new AurelianRuntimeTickInput(7, TimeSpan.FromMilliseconds(16)));

        Assert.True(tick.Success);
        Assert.Equal(AurelianRuntimeTickStatus.Ticked, tick.Status);
        Assert.True(session.Stop().Success);
    }

    [Fact]
    public void Stop_RemainsNonIdempotentAndReportsTheExistingDiagnostic()
    {
        var session = new AurelianRuntimeSession();
        Assert.True(session.Start().Success);
        Assert.True(session.Stop().Success);

        AurelianRuntimeResult repeated = session.Stop();

        Assert.False(repeated.Success);
        Assert.Equal(AurelianRuntimeDiagnosticCodes.RuntimeAlreadyStopped, Assert.Single(repeated.Diagnostics).Code);
    }
}
