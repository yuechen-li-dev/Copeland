using System.Collections.Immutable;
using Aurelian.Graphics.FramePlans;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class FramePlanTests
{
    [Fact]
    public void DeclarativeGraphCompilesToStableFlatTapeAndRoundTripsAsData()
    {
        var graph = new FrameGraphBuilder();
        var input = graph.Resource("input", "rgba16f", 32, 32, FrameLifetime.Imported, initialized: true);
        var scratch = graph.Resource("scratch", "rgba16f", 32, 32);
        var output = graph.Resource("output", "rgba16f", 32, 32);
        graph.Pass("shade", 10).Read(input).Write(scratch);
        graph.Pass("display", 20).Read(scratch).Write(output);
        graph.Export(output);
        FramePlan plan = FramePlanCompiler.Compile(graph.Build());
        Assert.Equal([10, 20], plan.Order.Select(id => plan.Graph.Passes[id].Implementation));
        Assert.Contains(plan.Tape, op => op.Op == FrameTapeOp.Barrier && op.Resource == scratch.Id);
        Assert.Equal(plan.Describe(), FramePlan.Load(plan.Describe()).Describe());
        Assert.Equal(plan.Hash, FramePlanCompiler.Compile(graph.Build()).Hash);
    }

    [Fact]
    public void UninitializedReadIsRejected()
    {
        var graph = new FrameGraphBuilder();
        var data = graph.Resource("uninitialized", "rgba16f", 32, 32);
        graph.Pass("read", 1, sideEffect: true).Read(data);
        Assert.Equal("AUR-PLAN-002", Assert.Throws<FramePlanException>(() => FramePlanCompiler.Compile(graph.Build())).Code);
    }

    [Fact]
    public void TamperedTapeWithoutItsHazardBarrierIsRejected()
    {
        FramePlan plan = Chain();
        plan = plan with { Tape = plan.Tape.Where(op => op.Op != FrameTapeOp.Barrier).ToImmutableArray() };
        Assert.Equal("AUR-PLAN-004", Assert.Throws<FramePlanException>(() => FramePlanVerifier.Verify(plan)).Code);
    }

    [Fact]
    public void ComputeQueueCannotOwnRasterAttachments()
    {
        var graph = new FrameGraphBuilder();
        var output = graph.Resource("output", "rgba16f", 32, 32);
        graph.Pass("invalid-raster", 1, FrameQueue.Compute).Write(output);
        graph.Export(output);
        Assert.Equal("AUR-PLAN-005", Assert.Throws<FramePlanException>(() => FramePlanCompiler.Compile(graph.Build())).Code);
    }

    [Fact]
    public void LiveAliasIsRejectedAndOrderedTransientStorageIsReused()
    {
        FramePlan plan = Chain();
        var first = plan.Allocations[0];
        var second = plan.Allocations[1];
        Assert.NotEqual(first.Slot, second.Slot);
        plan = plan with { Allocations = [first, second with { Slot = first.Slot }] };
        Assert.Equal("AUR-PLAN-009", Assert.Throws<FramePlanException>(() => FramePlanVerifier.Verify(plan)).Code);

        var graph = new FrameGraphBuilder();
        var a = graph.Resource("a", "rgba16f", 32, 32);
        var b = graph.Resource("b", "rgba16f", 32, 32);
        graph.Pass("write-a", 1).Write(a);
        graph.Pass("consume-a", 2, sideEffect: true).Read(a);
        graph.Pass("write-b", 3).Write(b);
        graph.Export(b);
        FramePlan reused = FramePlanCompiler.Compile(graph.Build());
        Assert.Equal(reused.Allocations[0].Slot, reused.Allocations[1].Slot);
    }

    [Fact]
    public void IndependentQueuesCannotAliasMerelyBecauseTheirTapeIndicesDiffer()
    {
        var graph = new FrameGraphBuilder();
        var a = graph.Resource("a", "rgba16f", 32, 32);
        var b = graph.Resource("b", "rgba16f", 32, 32);
        graph.Pass("graphics", 1).Write(a);
        graph.Pass("compute", 2, FrameQueue.Compute).Write(b, FrameAccess.ComputeWrite);
        graph.Export(a);
        graph.Export(b);
        FramePlan plan = FramePlanCompiler.Compile(graph.Build());
        Assert.NotEqual(plan.Allocations[0].Slot, plan.Allocations[1].Slot);
    }

    [Fact]
    public void CrossQueueWaitBeginsAtTheConsumerBatchAndIsVerified()
    {
        var graph = new FrameGraphBuilder();
        var particles = graph.Resource("particles", "buffer", 16, 1);
        var scene = graph.Resource("scene", "rgba16f", 32, 32);
        var output = graph.Resource("output", "rgba16f", 32, 32);
        graph.Pass("particle-update", 1, FrameQueue.Compute).Write(particles, FrameAccess.ComputeWrite);
        graph.Pass("independent-opaque", 2).Write(scene);
        graph.Pass("transparent", 3).Read(particles, FrameAccess.Vertex).Read(scene).Write(output);
        graph.Export(output);
        FramePlan plan = FramePlanCompiler.Compile(graph.Build());
        Assert.Equal(3, plan.Batches.Length);
        Assert.Empty(plan.Batches.Single(batch => batch.Passes.Contains(1)).WaitFor);
        FrameBatch consumer = plan.Batches.Single(batch => batch.Passes.Contains(2));
        Assert.Single(consumer.WaitFor);
        plan = plan with { Batches = plan.Batches.Select(batch => batch.Id == consumer.Id
            ? batch with { WaitFor = [] } : batch).ToImmutableArray() };
        Assert.Equal("AUR-PLAN-008", Assert.Throws<FramePlanException>(() => FramePlanVerifier.Verify(plan)).Code);
    }

    [Fact]
    public void UnusedPassesAreRemovedAndOutputsRemainRequired()
    {
        var graph = new FrameGraphBuilder();
        var unused = graph.Resource("unused", "rgba16f", 32, 32);
        var output = graph.Resource("output", "rgba16f", 32, 32);
        graph.Pass("dead", 1).Write(unused);
        graph.Pass("live", 2).Write(output);
        graph.Export(output);
        Assert.Equal([1], FramePlanCompiler.Compile(graph.Build()).Order.ToArray());
    }

    [Fact]
    public void ReusingStorageRequiresAnAliasOwnershipBarrier()
    {
        var graph = new FrameGraphBuilder();
        var a = graph.Resource("a", "rgba16f", 32, 32);
        var b = graph.Resource("b", "rgba16f", 32, 32);
        graph.Pass("write-a", 1).Write(a);
        graph.Pass("consume-a", 2, sideEffect: true).Read(a);
        graph.Pass("write-b", 3).Write(b);
        graph.Export(b);
        FramePlan plan = FramePlanCompiler.Compile(graph.Build());
        plan = plan with { Tape = plan.Tape.Where(op => op.Op != FrameTapeOp.Barrier || op.Resource != b.Id).ToImmutableArray() };
        Assert.Equal("AUR-PLAN-009", Assert.Throws<FramePlanException>(() => FramePlanVerifier.Verify(plan)).Code);
    }

    [Fact]
    public void WriteAfterMultipleReadersWaitsForAllTheirAccesses()
    {
        var graph = new FrameGraphBuilder();
        var input = graph.Resource("input", "buffer", 32, 1, FrameLifetime.Imported, initialized: true);
        graph.Pass("sample", 1, sideEffect: true).Read(input);
        graph.Pass("vertices", 2, sideEffect: true).Read(input, FrameAccess.Vertex);
        graph.Pass("replace", 3).Write(input, FrameAccess.TransferWrite);
        graph.Export(input);
        FramePlan plan = FramePlanCompiler.Compile(graph.Build());
        var barrier = plan.Tape.Single(op => op.Op == FrameTapeOp.Barrier);
        Assert.Equal(FrameAccess.Sample | FrameAccess.Vertex, barrier.Before);
        plan = plan with { Tape = plan.Tape.Select(op => op == barrier ? op with { Before = FrameAccess.Vertex } : op).ToImmutableArray() };
        Assert.Equal("AUR-PLAN-004", Assert.Throws<FramePlanException>(() => FramePlanVerifier.Verify(plan)).Code);
    }

    [Fact]
    public void CyclesUnconsumedWritesAndHashChangesAreRejected()
    {
        var graph = new FrameGraphBuilder();
        var input = graph.Resource("input", "buffer", 32, 1);
        var a = graph.Pass("a", 1, sideEffect: true).Write(input);
        var b = graph.Pass("b", 2, sideEffect: true).Read(input);
        a.DependsOn(b);
        Assert.Equal("AUR-PLAN-003", Assert.Throws<FramePlanException>(() => FramePlanCompiler.Compile(graph.Build())).Code);

        graph = new();
        input = graph.Resource("unused", "buffer", 32, 1);
        graph.Pass("unused-write", 1, sideEffect: true).Write(input);
        Assert.Equal("AUR-PLAN-006", Assert.Throws<FramePlanException>(() => FramePlanCompiler.Compile(graph.Build())).Code);
        FramePlan plan = Chain() with { Hash = "tampered" };
        Assert.Equal("AUR-PLAN-007", Assert.Throws<FramePlanException>(() => FramePlanVerifier.Verify(plan)).Code);
    }

    [Fact]
    public void ReorderedSubmissionPassesCannotBypassTapeVerification()
    {
        FramePlan plan = Chain();
        plan = plan with { Batches = [plan.Batches[0] with { Passes = [1, 0] }] };
        Assert.Equal("AUR-PLAN-008", Assert.Throws<FramePlanException>(() => FramePlanVerifier.Verify(plan)).Code);
    }

    private static FramePlan Chain()
    {
        var graph = new FrameGraphBuilder();
        var a = graph.Resource("a", "rgba16f", 32, 32);
        var b = graph.Resource("b", "rgba16f", 32, 32);
        graph.Pass("producer", 1).Write(a);
        graph.Pass("consumer", 2).Read(a).Write(b);
        graph.Export(b);
        return FramePlanCompiler.Compile(graph.Build());
    }
}
