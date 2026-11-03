using System.Collections.Immutable;

namespace Aurelian.Graphics.FramePlans;

public enum FrameQueue { Graphics, Compute, Transfer }
public enum FrameLifetime { Transient, Imported, History }
[Flags]
public enum FrameAccess
{
    None = 0, Sample = 1, Vertex = 2, TransferRead = 4, ColorWrite = 8, DepthWrite = 16,
    ComputeWrite = 32, TransferWrite = 64, HistoryReadWrite = 128,
}
public enum FrameTapeOp { Barrier, Pass }

public sealed record FrameResource(int Id, string Name, string Format, int Width, int Height,
    FrameLifetime Lifetime, bool Initialized = false, bool AllowUnused = false, bool Dedicated = false);
public readonly record struct FrameUse(int Resource, FrameAccess Access)
{
    public bool Writes => (Access & (FrameAccess.ColorWrite | FrameAccess.DepthWrite | FrameAccess.ComputeWrite
        | FrameAccess.TransferWrite | FrameAccess.HistoryReadWrite)) != 0;
}
public sealed record FramePass(int Id, string Name, int Implementation, FrameQueue Queue,
    ImmutableArray<FrameUse> Uses, ImmutableArray<int> After, bool SideEffect = false);
public sealed record FrameGraph(ImmutableArray<FrameResource> Resources, ImmutableArray<FramePass> Passes,
    ImmutableArray<int> Outputs);
public readonly record struct FrameInstruction(FrameTapeOp Op, int Pass, int Resource = -1,
    FrameAccess Before = FrameAccess.Sample, FrameAccess After = FrameAccess.Sample);
public readonly record struct FrameAllocation(int Resource, int Slot, int First, int Last);
public sealed record FrameBatch(int Id, FrameQueue Queue, ImmutableArray<int> Passes, ImmutableArray<int> WaitFor);

/// <summary>C# authoring resolves to data. Recording callbacks and native handles never enter the document.</summary>
public sealed class FrameGraphBuilder
{
    private readonly List<FrameResource> resources = [];
    private readonly List<FramePassBuilder> passes = [];
    private readonly List<int> outputs = [];

    public FrameResource Resource(string name, string format, int width, int height,
        FrameLifetime lifetime = FrameLifetime.Transient, bool initialized = false, bool allowUnused = false, bool dedicated = false)
    {
        var resource = new FrameResource(resources.Count, name, format, width, height, lifetime, initialized, allowUnused, dedicated);
        resources.Add(resource);
        return resource;
    }

    public FramePassBuilder Pass(string name, int implementation, FrameQueue queue = FrameQueue.Graphics, bool sideEffect = false)
    {
        var pass = new FramePassBuilder(passes.Count, name, implementation, queue, sideEffect);
        passes.Add(pass);
        return pass;
    }

    public void Export(FrameResource resource) => outputs.Add(resource.Id);

    public FrameGraph Build() => new(resources.ToImmutableArray(), passes.Select(pass => pass.Build()).ToImmutableArray(),
        outputs.Distinct().ToImmutableArray());
}

public sealed class FramePassBuilder(int id, string name, int implementation, FrameQueue queue, bool sideEffect)
{
    private readonly List<FrameUse> uses = [];
    private readonly List<int> after = [];
    public int Id => id;

    public FramePassBuilder Read(FrameResource resource, FrameAccess access = FrameAccess.Sample)
    {
        uses.Add(new(resource.Id, access));
        return this;
    }

    public FramePassBuilder Write(FrameResource resource, FrameAccess access = FrameAccess.ColorWrite)
    {
        uses.Add(new(resource.Id, access));
        return this;
    }

    public FramePassBuilder DependsOn(FramePassBuilder pass)
    {
        after.Add(pass.Id);
        return this;
    }

    internal FramePass Build() => new(id, name, implementation, queue, uses.Distinct().ToImmutableArray(),
        after.Distinct().ToImmutableArray(), sideEffect);
}
