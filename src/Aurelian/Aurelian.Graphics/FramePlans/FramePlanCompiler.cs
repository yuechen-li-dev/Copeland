using System.Collections.Immutable;

namespace Aurelian.Graphics.FramePlans;

/// <summary>Resolves dependencies, dead passes, synchronization and compatible transient storage once.</summary>
public static class FramePlanCompiler
{
    public static FramePlan Compile(FrameGraph graph, bool aliasTransients = true)
    {
        ValidateDeclarations(graph);
        var edges = Dependencies(graph);
        int[] order = TopologicalOrder(graph, edges);
        var live = LivePasses(graph, edges);
        order = order.Where(live.Contains).ToArray();
        var allocations = Allocate(graph, order, edges, aliasTransients);
        var slots = allocations.ToDictionary(allocation => allocation.Resource, allocation => allocation.Slot);
        var owners = new Dictionary<int, int>();
        var tape = new List<FrameInstruction>();
        var prior = new Dictionary<int, FrameUse>();
        foreach (int id in order)
        {
            FramePass pass = graph.Passes[id];
            foreach (FrameUse use in pass.Uses)
            {
                FrameAccess before = FrameAccess.None;
                if (prior.TryGetValue(use.Resource, out var previous) && (previous.Writes || use.Writes))
                {
                    before |= previous.Access;
                }
                if (slots.TryGetValue(use.Resource, out int slot))
                {
                    if (owners.TryGetValue(slot, out int owner) && owner != use.Resource)
                    {
                        before |= prior[owner].Access;
                    }
                    owners[slot] = use.Resource;
                }
                if (before != FrameAccess.None)
                {
                    tape.Add(new(FrameTapeOp.Barrier, id, use.Resource, before, use.Access));
                }
                prior[use.Resource] = previous.Resource == use.Resource && !previous.Writes && !use.Writes
                    ? new(use.Resource, previous.Access | use.Access) : use;
            }
            tape.Add(new(FrameTapeOp.Pass, id));
        }
        var plan = new FramePlan(FramePlan.CurrentFormat, graph, order.ToImmutableArray(), tape.ToImmutableArray(),
            allocations, Schedule(graph, order, edges), "");
        plan = plan with { Hash = plan.ComputeHash() };
        FramePlanVerifier.Verify(plan);
        return plan;
    }

    private static ImmutableArray<FrameBatch> Schedule(FrameGraph graph, int[] order, HashSet<int>[] edges)
    {
        var queues = new Dictionary<FrameQueue, int>();
        var passBatches = new Dictionary<int, int>();
        var batches = new List<(FrameQueue Queue, List<int> Passes, HashSet<int> Waits)>();
        foreach (int pass in order)
        {
            FrameQueue queue = graph.Passes[pass].Queue;
            var waits = edges[pass].Where(parent => passBatches.ContainsKey(parent) && graph.Passes[parent].Queue != queue)
                .Select(parent => passBatches[parent]).ToHashSet();
            if (!queues.TryGetValue(queue, out int batch) || !waits.IsSubsetOf(batches[batch].Waits))
            {
                batch = batches.Count;
                batches.Add((queue, [], waits));
                queues[queue] = batch;
            }
            batches[batch].Passes.Add(pass);
            passBatches.Add(pass, batch);
        }
        return batches.Select((batch, id) => new FrameBatch(id, batch.Queue,
            batch.Passes.ToImmutableArray(), batch.Waits.Order().ToImmutableArray())).ToImmutableArray();
    }

    internal static void ValidateDeclarations(FrameGraph graph)
    {
        if (graph.Resources.Length > 256 || graph.Passes.Length > 128 || graph.Passes.IsEmpty
            || !graph.Resources.Select(resource => resource.Id).SequenceEqual(Enumerable.Range(0, graph.Resources.Length))
            || !graph.Passes.Select(pass => pass.Id).SequenceEqual(Enumerable.Range(0, graph.Passes.Length))
            || graph.Resources.Select(resource => resource.Name).Distinct().Count() != graph.Resources.Length
            || graph.Passes.Select(pass => pass.Name).Distinct().Count() != graph.Passes.Length)
        {
            throw new FramePlanException("AUR-PLAN-001", "Invalid, duplicate or oversized graph declarations.");
        }
        foreach (FrameResource resource in graph.Resources)
        {
            if (string.IsNullOrWhiteSpace(resource.Name) || string.IsNullOrWhiteSpace(resource.Format)
                || resource.Width <= 0 || resource.Height <= 0 || !Enum.IsDefined(resource.Lifetime)
                || resource.Lifetime == FrameLifetime.Transient && resource.Initialized)
            {
                throw new FramePlanException("AUR-PLAN-001", "Invalid resource: " + resource.Name);
            }
        }
        foreach (FramePass pass in graph.Passes)
        {
            if (string.IsNullOrWhiteSpace(pass.Name) || !Enum.IsDefined(pass.Queue)
                || pass.Uses.Any(use => use.Resource < 0 || use.Resource >= graph.Resources.Length
                    || use.Access == FrameAccess.None || !Enum.IsDefined(use.Access))
                || pass.After.Any(id => id < 0 || id >= graph.Passes.Length || id == pass.Id)
                || pass.Uses.GroupBy(use => use.Resource).Any(group => group.Count() > 1))
            {
                throw new FramePlanException("AUR-PLAN-001", "Invalid pass: " + pass.Name);
            }
            if (pass.Queue != FrameQueue.Graphics && pass.Uses.Any(use => use.Access is FrameAccess.ColorWrite or FrameAccess.DepthWrite or FrameAccess.Vertex or FrameAccess.Indirect)
                || pass.Queue == FrameQueue.Transfer && pass.Uses.Any(use => use.Access is not (FrameAccess.TransferRead or FrameAccess.TransferWrite)))
            {
                throw new FramePlanException("AUR-PLAN-005", "Queue cannot execute " + pass.Name);
            }
        }
        if (graph.Outputs.Any(id => id < 0 || id >= graph.Resources.Length))
        {
            throw new FramePlanException("AUR-PLAN-001", "Invalid output resource.");
        }
    }

    internal static HashSet<int>[] Dependencies(FrameGraph graph)
    {
        var edges = graph.Passes.Select(pass => pass.After.ToHashSet()).ToArray();
        foreach (FrameResource resource in graph.Resources)
        {
            int? writer = null;
            var readers = new HashSet<int>();
            foreach (FramePass pass in graph.Passes)
            {
                FrameUse? use = pass.Uses.Where(use => use.Resource == resource.Id).Cast<FrameUse?>().SingleOrDefault();
                if (use is null)
                {
                    continue;
                }
                if (writer is { } prior)
                {
                    edges[pass.Id].Add(prior);
                }
                if (use.Value.Writes)
                {
                    edges[pass.Id].UnionWith(readers);
                    readers.Clear();
                    writer = pass.Id;
                }
                else
                {
                    if (writer is null && !resource.Initialized)
                    {
                        throw new FramePlanException("AUR-PLAN-002", pass.Name + " reads uninitialized " + resource.Name);
                    }
                    readers.Add(pass.Id);
                }
            }
        }
        return edges;
    }

    private static int[] TopologicalOrder(FrameGraph graph, HashSet<int>[] edges)
    {
        var result = new List<int>();
        var remaining = graph.Passes.Select(pass => pass.Id).ToHashSet();
        while (remaining.Count > 0)
        {
            int next = remaining.Order().FirstOrDefault(id => edges[id].All(result.Contains), -1);
            if (next < 0)
            {
                throw new FramePlanException("AUR-PLAN-003", "Frame dependencies contain a cycle.");
            }
            result.Add(next);
            remaining.Remove(next);
        }
        return result.ToArray();
    }

    private static HashSet<int> LivePasses(FrameGraph graph, HashSet<int>[] edges)
    {
        var live = graph.Passes.Where(pass => pass.SideEffect || pass.Uses.Any(use => use.Writes && graph.Outputs.Contains(use.Resource)))
            .Select(pass => pass.Id).ToHashSet();
        var work = new Stack<int>(live);
        while (work.TryPop(out int pass))
        {
            foreach (int dependency in edges[pass])
            {
                if (live.Add(dependency))
                {
                    work.Push(dependency);
                }
            }
        }
        return live;
    }

    private static ImmutableArray<FrameAllocation> Allocate(FrameGraph graph, int[] order, HashSet<int>[] dependencies, bool alias)
    {
        // Queue order is also a happens-before edge. Topological indices alone do not order different queues.
        var edges = dependencies.Select(set => set.ToHashSet()).ToArray();
        foreach (FrameQueue queue in Enum.GetValues<FrameQueue>())
        {
            int[] queuePasses = order.Where(id => graph.Passes[id].Queue == queue).ToArray();
            for (int index = 1; index < queuePasses.Length; index++)
            {
                edges[queuePasses[index]].Add(queuePasses[index - 1]);
            }
        }
        var allocations = new List<FrameAllocation>();
        var slots = new List<List<int>>();
        foreach (FrameResource resource in graph.Resources.Where(resource => resource.Lifetime == FrameLifetime.Transient))
        {
            int[] uses = order.Where(id => graph.Passes[id].Uses.Any(use => use.Resource == resource.Id)).ToArray();
            if (uses.Length == 0)
            {
                continue;
            }
            int slot = -1;
            if (alias && !resource.Dedicated)
            {
                slot = slots.FindIndex(occupants => occupants.All(other => !graph.Resources[other].Dedicated && Compatible(resource, graph.Resources[other])
                    && OrderedResources(graph, order, edges, other, resource.Id)));
            }
            if (slot < 0)
            {
                slot = slots.Count;
                slots.Add([]);
            }
            slots[slot].Add(resource.Id);
            allocations.Add(new(resource.Id, slot, Array.IndexOf(order, uses[0]), Array.IndexOf(order, uses[^1])));
        }
        return allocations.ToImmutableArray();
    }

    internal static bool Compatible(FrameResource left, FrameResource right) => left.Format == right.Format
        && left.Width == right.Width && left.Height == right.Height;

    internal static bool OrderedResources(FrameGraph graph, IReadOnlyList<int> order, HashSet<int>[] edges, int left, int right)
    {
        int[] a = order.Where(id => graph.Passes[id].Uses.Any(use => use.Resource == left)).ToArray();
        int[] b = order.Where(id => graph.Passes[id].Uses.Any(use => use.Resource == right)).ToArray();
        return a.All(first => b.All(second => Reaches(first, second, edges)))
            || b.All(first => a.All(second => Reaches(first, second, edges)));
    }

    internal static bool Reaches(int before, int after, HashSet<int>[] edges)
    {
        if (before == after)
        {
            return false;
        }
        var visited = new HashSet<int>();
        var work = new Stack<int>(edges[after]);
        while (work.TryPop(out int current))
        {
            if (current == before)
            {
                return true;
            }
            if (visited.Add(current))
            {
                foreach (int parent in edges[current]) work.Push(parent);
            }
        }
        return false;
    }
}
