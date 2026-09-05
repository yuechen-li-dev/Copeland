using Aurelian.Simulation;
using Xunit;

namespace Aurelian.Simulation.Tests;

public sealed class CadenceSchedulerTests
{
    private static readonly CadenceDefinition[] SecondConsumerCadences =
    [
        new(new CadenceId("physics-like"), RationalRate.PerSecond(30), 0),
        new(new CadenceId("agent"), RationalRate.PerSecond(5), 1),
        new(new CadenceId("pulse"), RationalRate.PerSecond(2), 2)
    ];

    [Fact]
    public void SingleCadenceProducesExactTicks()
    {
        var scheduler = Create([new CadenceDefinition(new CadenceId("tick"), RationalRate.PerSecond(10), 0)]);
        CadenceAdvanceResult result = scheduler.Advance(TimeSpan.FromSeconds(2), SimulationExecutionRate.Normal);
        Assert.Equal(20, result.DueWork.Count);
        Assert.Equal(Enumerable.Range(1, 20).Select(value => (long)value), result.DueWork.Select(item => item.Tick));
    }

    [Fact]
    public void SimultaneousCadencesUseDeclaredOrder()
    {
        CadenceAdvanceResult result = Create(SecondConsumerCadences)
            .Advance(TimeSpan.FromSeconds(1), SimulationExecutionRate.Normal);
        Assert.Equal(
            ["physics-like", "agent", "pulse"],
            result.DueWork.Where(item => item.SemanticOffsetTicks == TimeSpan.TicksPerSecond).Select(item => item.Cadence.Value));
    }

    [Fact]
    public void PauseResumeAndFastForwardOnlyChangeDueProduction()
    {
        var scheduler = Create(SecondConsumerCadences);
        CadenceAdvanceResult paused = scheduler.Advance(TimeSpan.FromSeconds(1), SimulationExecutionRate.Paused);
        CadenceAdvanceResult resumed = scheduler.Advance(TimeSpan.FromSeconds(1), SimulationExecutionRate.Normal);
        CadenceAdvanceResult fast = scheduler.Advance(TimeSpan.FromSeconds(1), SimulationExecutionRate.FastForward(4));
        Assert.Empty(paused.DueWork);
        Assert.Equal(37, resumed.DueWork.Count);
        Assert.Equal(148, fast.DueWork.Count);
    }

    [Fact]
    public void ClampReportsAcceptedAndDiscardedTime()
    {
        var scheduler = Create(SecondConsumerCadences, TimeSpan.FromSeconds(2));
        CadenceAdvanceResult result = scheduler.Advance(TimeSpan.FromSeconds(7), SimulationExecutionRate.Normal);
        Assert.Equal(TimeSpan.FromSeconds(2).Ticks, result.HostTicksAccepted);
        Assert.Equal(TimeSpan.FromSeconds(5).Ticks, result.HostTicksDiscarded);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    [InlineData(173)]
    public void DistinctRenderPartitionsProduceSameSecondConsumerTrace(int partitions)
    {
        string baseline = Trace(Create(SecondConsumerCadences), TimeSpan.FromSeconds(1), 1);
        string partitioned = Trace(Create(SecondConsumerCadences), TimeSpan.FromSeconds(1), partitions);
        Assert.Equal(baseline, partitioned);
    }

    [Fact]
    public void ConfigurationIdentityIsStableAndRateSensitive()
    {
        string first = Create(SecondConsumerCadences).ConfigurationIdentity;
        string second = Create(SecondConsumerCadences).ConfigurationIdentity;
        string changed = Create([new CadenceDefinition(new CadenceId("physics-like"), RationalRate.PerSecond(31), 0)]).ConfigurationIdentity;
        Assert.Equal(first, second);
        Assert.NotEqual(first, changed);
    }

    private static CadenceScheduler Create(
        IEnumerable<CadenceDefinition> definitions,
        TimeSpan? clamp = null)
    {
        return new CadenceScheduler(definitions, clamp ?? TimeSpan.FromSeconds(5));
    }

    private static string Trace(CadenceScheduler scheduler, TimeSpan total, int partitions)
    {
        long quotient = total.Ticks / partitions;
        long remainder = total.Ticks % partitions;
        var trace = new List<string>();
        for (int index = 0; index < partitions; index++)
        {
            long ticks = quotient + (index < remainder ? 1 : 0);
            CadenceAdvanceResult result = scheduler.Advance(TimeSpan.FromTicks(ticks), SimulationExecutionRate.Normal);
            trace.AddRange(result.DueWork.Select(item => $"{item.Cadence.Value}:{item.Tick}"));
        }
        return string.Join('|', trace);
    }
}
