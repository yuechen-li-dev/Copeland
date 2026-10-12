using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Inference.Dominatus;
using Xunit;

namespace Aurelian.Inference.Tests;

public sealed class CausalTextGeneratorTests
{
    [Fact]
    public void InvalidCapacityIsRejectedBeforeModelResetOrExecution()
    {
        var model = new RejectingModel();
        var generator = new CausalTextGenerator(model, new DominatusComputeSchedule());
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.Generate([2, 3], 3, new HashSet<int> { 1 }));
        Assert.Equal(0, model.Resets);
    }

    [Fact]
    public void ModelFailureAndCancellationResetStateAndPreserveOriginalFailure()
    {
        var model = new RejectingModel();
        var schedule = new DominatusComputeSchedule();
        var generator = new CausalTextGenerator(model, schedule);
        var failure = Assert.Throws<InvalidOperationException>(() => generator.Generate([2], 1, new HashSet<int> { 1 }));
        Assert.Same(model.Failure, failure);
        Assert.Equal(2, model.Resets);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => generator.Generate([2], 1, new HashSet<int> { 1 }, cancellationToken: cancellation.Token));
        Assert.Equal(4, model.Resets);
    }

    private sealed class RejectingModel : ICausalTextModel
    {
        public int ContextCapacity => 4;
        public int Resets { get; private set; }
        public InvalidOperationException Failure { get; } = new("Test model failure");
        public void Reset() => Resets++;
        public GpuTensor Evaluate(IReadOnlyList<int> tokens) => throw Failure;
        public (int Token, float Score) GreedyToken(GpuTensor hidden) => throw new NotSupportedException();
    }
}
