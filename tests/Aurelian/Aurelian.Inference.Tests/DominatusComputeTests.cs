using Aurelian.Inference.Dominatus;
using Xunit;

namespace Aurelian.Inference.Tests;

public sealed class DominatusComputeTests
{
    [Fact]
    public void ScheduleRunsInOrderAndCanBeReusedWithoutSkippingTheNextModel()
    {
        var schedule = new DominatusComputeSchedule("test.model");
        var calls = new List<int>();
        schedule.Run([new("first", () => calls.Add(1)), new("second", () => calls.Add(2))]);
        Assert.Equal(new[] { 1, 2 }, calls);
        Assert.Equal(new[] { "first", "second" }, schedule.Measurements.Select(item => item.Name));
        schedule.Run([new("third", () => calls.Add(3))]);
        Assert.Equal(new[] { 1, 2, 3 }, calls);
        Assert.Single(schedule.Measurements);
    }

    [Fact]
    public void OriginalFailureDisposesTheModelIteratorAndAllowsRetry()
    {
        var schedule = new DominatusComputeSchedule();
        var expected = new InvalidDataException("named-model-failure");
        bool disposed = false;
        Assert.Same(expected, Assert.Throws<InvalidDataException>(() => schedule.Run(Stages())));
        Assert.True(disposed);
        schedule.Run([new("retry", () => { })]);
        Assert.Single(schedule.Measurements);

        IEnumerable<ComputeStage> Stages()
        {
            try
            {
                yield return new("fail", () => throw expected);
                throw new InvalidOperationException("Failed iterator advanced unexpectedly.");
            }
            finally
            {
                disposed = true;
            }
        }
    }

    [Fact]
    public void CancellationBetweenStagesDisposesTheIteratorBeforeAnotherAction()
    {
        var schedule = new DominatusComputeSchedule();
        using var cancellation = new CancellationTokenSource();
        bool disposed = false;
        bool secondRan = false;
        Assert.Throws<OperationCanceledException>(() => schedule.Run(Stages(), cancellation.Token));
        Assert.True(disposed);
        Assert.False(secondRan);

        IEnumerable<ComputeStage> Stages()
        {
            try
            {
                yield return new("cancel", cancellation.Cancel);
                yield return new("second", () => secondRan = true);
            }
            finally
            {
                disposed = true;
            }
        }
    }

    [Fact]
    public void StageBudgetStopsAnUnboundedAuthorIterator()
    {
        var schedule = new DominatusComputeSchedule(maximumStages: 2);
        int ran = 0;
        bool disposed = false;
        var error = Assert.Throws<InvalidOperationException>(() => schedule.Run(Stages()));
        Assert.StartsWith("AUR-INFERENCE-SCHEDULE-001:", error.Message);
        Assert.Equal(2, ran);
        Assert.True(disposed);

        IEnumerable<ComputeStage> Stages()
        {
            try
            {
                for (int stage = 0; ; stage++)
                {
                    yield return new("stage." + stage, () => ran++);
                }
            }
            finally
            {
                disposed = true;
            }
        }
    }

    [Fact]
    public void FeedforwardRetainsBoundedSlotPlanAndRejectsUseAfterDispose()
    {
        var controller = new DominatusComputeFeedforward();
        Assert.Equal(2, controller.Plan(new(12, 64, 1024 * 1024)));
        Assert.Equal(1, controller.Plan(new(1, 64, 1024 * 1024)));
        Assert.Equal(1, controller.Plan(new(12, 64, 8 * 1024 * 1024)));
        controller.Dispose();
        Assert.Throws<ObjectDisposedException>(() => controller.Plan(new(2, 1, 0)));
    }
}
