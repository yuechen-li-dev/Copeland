using System.Globalization;
using Aurelian.Runtime.Inspection;
using Dominatus.Core;
using Dominatus.Core.Decision;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Runtime;
using Dominatus.Core.Trace;

namespace Aurelian.Runtime.Dominatus.Inspection;

/// <summary>
/// A projection of live kernel facts. Use at the owning application's stable tick boundary.
/// Trace collection is opt-in and bounded; existing trace sinks continue receiving callbacks.
/// </summary>
public sealed class DominatusInspector
{
    private readonly int capacity;
    private readonly Dictionary<string, Registration> agents = new(StringComparer.Ordinal);
    private readonly Queue<AgentTraceEntry> trace = new();
    private readonly Queue<InspectedDelta> changes = new();
    private long sequence;
    private long droppedTrace;
    private long droppedChanges;

    public DominatusInspector(int traceCapacity = 0)
    {
        if (traceCapacity < 0 || traceCapacity > 100000)
        {
            throw new ArgumentOutOfRangeException(nameof(traceCapacity));
        }
        capacity = traceCapacity;
    }

    public void Attach(string id, AiAgent agent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(agent);
        if (agents.ContainsKey(id) || agents.Values.Any(value => ReferenceEquals(value.Agent, agent)))
        {
            throw new InvalidOperationException($"Agent '{id}' is already inspected.");
        }
        var registration = new Registration(agent, agent.Brain.Trace);
        agents.Add(id, registration);
        if (capacity > 0)
        {
            registration.Sink = new Sink(this, id, registration.Previous);
            agent.Brain.Trace = registration.Sink;
        }
    }

    public void Detach(string id)
    {
        if (!agents.Remove(id, out Registration? registration))
        {
            return;
        }
        CollectChanges(id, registration);
        if (registration.Sink is not null && ReferenceEquals(registration.Agent.Brain.Trace, registration.Sink))
        {
            registration.Agent.Brain.Trace = registration.Previous;
        }
    }

    public AgentInspection Observe()
    {
        foreach ((string id, Registration registration) in agents)
        {
            CollectChanges(id, registration);
        }
        InspectedAgent[] snapshots = agents.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new InspectedAgent(pair.Key, pair.Value.Agent.Id.ToString(),
                pair.Value.Agent.Brain.GetActivePath().Select(state => state.ToString()).ToArray(),
                pair.Value.Agent.Bb.EnumerateSnapshotEntries().OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => new InspectedValue(entry.Key, entry.Value?.GetType().FullName ?? "null",
                        Display(entry.Value), entry.ExpiresAt, IsCheckpointValue(entry.Value))).ToArray()))
            .ToArray();
        return new AgentInspection(snapshots, trace.ToArray(), changes.ToArray(), droppedTrace, droppedChanges);
    }

    internal void CollectOwnedChanges()
    {
        foreach ((string id, Registration registration) in agents)
        {
            CollectChanges(id, registration);
            // AurelianAgentRuntime owns these agents and their journals. External attachments are never cleared.
            registration.Agent.BbTracker.ClearJournal();
            registration.JournalPosition = 0;
        }
    }

    public void RequireCheckpointValues()
    {
        foreach ((string id, Registration registration) in agents)
        {
            foreach (var entry in registration.Agent.Bb.EnumerateSnapshotEntries())
            {
                if (!IsCheckpointValue(entry.Value))
                {
                    throw new NotSupportedException($"Agent '{id}', blackboard '{entry.Key}': kernel checkpoint codec cannot retain {entry.Value?.GetType().FullName ?? "null"}.");
                }
            }
        }
    }

    private static bool IsCheckpointValue(object? value)
    {
        return value is bool or int or long or string or Guid
            || value is float single && float.IsFinite(single)
            || value is double number && double.IsFinite(number);
    }

    private static string? Display(object? value)
    {
        // Arbitrary domain ToString methods can invoke computed getters or mutate state.
        // Project known scalar values only; retain the type and checkpoint limitation for opaque values.
        if (value is null)
        {
            return null;
        }
        if (value is string text)
        {
            return text;
        }
        if (value is bool boolean)
        {
            return boolean ? "true" : "false";
        }
        if (value is int or long or float or double or Guid)
        {
            return ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);
        }
        return $"<opaque:{value.GetType().FullName}>";
    }

    private void CollectChanges(string id, Registration registration)
    {
        var journal = registration.Agent.BbTracker.Journal;
        if (registration.JournalPosition > journal.Count)
        {
            registration.JournalPosition = 0;
        }
        while (registration.JournalPosition < journal.Count)
        {
            var entry = journal[registration.JournalPosition++];
            if (capacity == 0)
            {
                continue;
            }
            if (changes.Count == capacity)
            {
                changes.Dequeue();
                droppedChanges++;
            }
            changes.Enqueue(new InspectedDelta(id, entry.TimeSeconds, entry.KeyId, entry.Op,
                Display(entry.OldValue), Display(entry.NewValue)));
        }
    }

    private void Record(string agent, float time, string kind, StateId state,
        StateId? destination = null, string? reason = null, DecisionReport? decision = null)
    {
        InspectedDecision? report = null;
        if (decision is not null)
        {
            report = new InspectedDecision(decision.Phase, decision.CurrentId, decision.CurrentScore,
                decision.BestId, decision.BestScore, decision.Switched, decision.Reason,
                decision.Scores.Select(score => new InspectedScore(score.Id, score.Score, score.Target.ToString())).ToArray());
        }
        if (trace.Count == capacity)
        {
            trace.Dequeue();
            droppedTrace++;
        }
        trace.Enqueue(new AgentTraceEntry(++sequence, agent, time, kind, state.ToString(),
            destination?.ToString(), reason, report));
    }

    private sealed class Registration(AiAgent agent, IAiTraceSink? previous)
    {
        public AiAgent Agent { get; } = agent;
        public IAiTraceSink? Previous { get; } = previous;
        public IAiTraceSink? Sink { get; set; }
        public int JournalPosition { get; set; }
    }

    private sealed class Sink(DominatusInspector owner, string id, IAiTraceSink? previous) : IAiTraceSink
    {
        public void OnEnter(StateId state, float time, string reason)
        {
            owner.Record(id, time, "enter", state, reason: reason);
            previous?.OnEnter(state, time, reason);
        }

        public void OnExit(StateId state, float time, string reason)
        {
            owner.Record(id, time, "exit", state, reason: reason);
            previous?.OnExit(state, time, reason);
        }

        public void OnTransition(StateId from, StateId to, float time, string reason)
        {
            owner.Record(id, time, "transition", from, to, reason);
            previous?.OnTransition(from, to, time, reason);
        }

        public void OnYield(StateId state, float time, object yielded)
        {
            owner.Record(id, time, yielded is DecisionReport ? "decision" : "yield", state,
                reason: yielded.GetType().FullName, decision: yielded as DecisionReport);
            previous?.OnYield(state, time, yielded);
        }

        public void OnReturn(StateReturn result, StateId? resumedParent, float time)
        {
            owner.Record(id, time, "return", result.State, resumedParent, result.Reason ?? result.Kind.ToString());
            previous?.OnReturn(result, resumedParent, time);
        }
    }
}
