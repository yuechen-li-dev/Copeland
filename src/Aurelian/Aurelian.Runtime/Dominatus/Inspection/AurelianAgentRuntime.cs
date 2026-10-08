using Aurelian.Runtime.Dominatus;
using Aurelian.Runtime.Sessions;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Persistence;
using Dominatus.Core.Runtime;

namespace Aurelian.Runtime.Dominatus.Inspection;

public sealed record PolicyAgentIdentity(string Id, string KernelId, string Root);
public sealed record AurelianPolicyCheckpoint(DominatusCheckpoint Kernel, PolicyAgentIdentity[] Agents);

/// <summary>Named application agents on the existing Dominatus world runner, with opt-in kernel inspection.</summary>
public sealed class AurelianAgentRuntime
{
    private readonly AiWorld world = new();
    private readonly Dictionary<string, AiAgent> agents = new(StringComparer.Ordinal);
    private readonly SequentialAurelianDominatusWorldRunner runner = new();
    private ulong tick;
    private bool restored;
    private readonly bool journalEnabled;

    public AurelianAgentRuntime(int traceCapacity = 0)
    {
        Inspector = new DominatusInspector(traceCapacity);
        journalEnabled = traceCapacity > 0;
    }

    public DominatusInspector Inspector { get; }
    public int Count => agents.Count;

    public AiAgent Add(string id, HfsmInstance brain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (agents.ContainsKey(id))
        {
            throw new InvalidOperationException($"Agent '{id}' is already registered.");
        }
        var agent = new AiAgent(brain);
        agent.BbTracker.JournalEnabled = journalEnabled;
        world.Add(agent);
        agents.Add(id, agent);
        Inspector.Attach(id, agent);
        return agent;
    }

    public AiAgent Agent(string id) => agents[id];

    public void Remove(string id)
    {
        if (agents.Remove(id, out var agent))
        {
            Inspector.Detach(id);
            world.Remove(agent);
        }
    }

    public void Tick(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }
        runner.RunTickAsync(world, new AurelianRuntimeTickInput(++tick, elapsed)).GetAwaiter().GetResult();
        Inspector.CollectOwnedChanges();
    }

    /// <summary>Kernel policy checkpoint only; game state and external effects are not captured here.</summary>
    public AurelianPolicyCheckpoint CapturePolicyCheckpoint()
    {
        Inspector.RequireCheckpointValues();
        return new AurelianPolicyCheckpoint(DominatusCheckpointBuilder.Capture(world), Identities());
    }

    /// <summary>Cold policy restore into a freshly authored runtime. Not an exact coroutine or game rewind.</summary>
    public void RestorePolicyCheckpoint(AurelianPolicyCheckpoint savedCheckpoint)
    {
        ArgumentNullException.ThrowIfNull(savedCheckpoint);
        DominatusCheckpoint checkpoint = savedCheckpoint.Kernel;
        if (restored || tick != 0 || world.Clock.Time != 0)
        {
            throw new InvalidOperationException("Cold policy restore requires a fresh runtime. Use input replay for exact game rewind.");
        }
        if (!Identities().SequenceEqual(savedCheckpoint.Agents))
        {
            throw new InvalidOperationException("Policy restore requires the same named agents, kernel IDs, and authored roots.");
        }
        if (!float.IsFinite(checkpoint.WorldTimeSeconds) || checkpoint.WorldTimeSeconds < 0
            || checkpoint.Version != DominatusSave.CurrentVersion)
        {
            throw new InvalidDataException("Invalid kernel checkpoint version or time.");
        }
        var existing = world.Agents.Select(agent => agent.Id.ToString()).Order(StringComparer.Ordinal);
        var saved = checkpoint.Agents.Select(agent => agent.AgentId).Order(StringComparer.Ordinal);
        if (!existing.SequenceEqual(saved))
        {
            throw new InvalidOperationException("Policy restore requires the same agent topology.");
        }
        if (checkpoint.Agents.Any(agent => EventCursorCodec.Deserialize(agent.EventCursorBlob).Pending.Length > 0))
        {
            throw new NotSupportedException("In-flight actuations require the host's existing completion replay path.");
        }
        world.Clock.Advance(checkpoint.WorldTimeSeconds);
        DominatusCheckpointBuilder.Restore(world, checkpoint);
        restored = true;
    }

    private PolicyAgentIdentity[] Identities()
    {
        return agents.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new PolicyAgentIdentity(pair.Key, pair.Value.Id.ToString(), pair.Value.Brain.Graph.Root.ToString()))
            .ToArray();
    }
}
