using System.Text.Json.Serialization;

namespace Aurelian.Actuation.Host;

/// <summary>Stable semantic identity for one Aurelian gameplay entity.</summary>
public readonly record struct AgentId
{
    [JsonConstructor]
    public AgentId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Agent identity cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public override string ToString() => Value.ToString("D");
}

/// <summary>
/// Backend-neutral materialized-body identity. Its value is opaque to policy
/// and must not encode a native pointer or require FormID parsing.
/// </summary>
public readonly record struct BodyId
{
    [JsonConstructor]
    public BodyId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public enum BodyBindingKind
{
    ExclusiveControl,
    PresentationOnly,
    ObservationOnly,
}

public enum BodyBindingState
{
    Unbound,
    Binding,
    Bound,
    Releasing,
    Released,
    Lost,
    Failed,
    RestoreRequired,
}

public sealed record BodyBinding(
    AgentId Agent,
    BodyId Body,
    BodyBindingKind Kind,
    BodyBindingState State,
    ulong Generation);

public sealed record BodyCapabilities(
    bool CanMove,
    bool CanLook,
    bool CanAnimate,
    bool CanReceiveInput,
    bool CanBeExclusiveBound,
    bool CanRestore);

public sealed record BodyObservation(
    BodyId Id,
    bool IsLoaded,
    bool IsAlive,
    HostPosition3 Position,
    BodyCapabilities Capabilities,
    BodyBindingState BindingState,
    AgentId? BoundAgent,
    ulong Generation,
    ulong Sequence);

public sealed record BodyBindingObservation(
    BodyBinding Binding,
    BodyObservation? Body,
    string? FailureReason = null);

public sealed record BodyCommandResult(
    Guid RequestId,
    HostActionState State,
    string? FailureReason,
    BodyObservation? Body = null,
    BodyBindingObservation? Binding = null)
{
    public bool Completed => State == HostActionState.Completed;
}

public sealed record BindBodyArguments(
    AgentId Agent,
    BodyId Body,
    BodyBindingKind Kind,
    ulong ExpectedBodyGeneration) : HostCommandArguments;

public sealed record ReleaseBodyArguments(
    AgentId Agent,
    BodyId Body,
    ulong ExpectedBodyGeneration) : HostCommandArguments;

public sealed record QueryBodyBindingArguments(
    AgentId Agent,
    BodyId Body) : HostCommandArguments;

public sealed record MoveBodyTowardArguments(
    AgentId Agent,
    BodyId Body,
    HostPosition3 TargetPosition,
    float StoppingDistance,
    float MaximumDistance,
    HostMovementSpeedPolicy SpeedPolicy,
    ulong ExpectedBodyGeneration,
    ulong ExpectedObservationSequence) : HostCommandArguments;

public sealed record BodyBindingRegistryResult(
    bool Accepted,
    BodyBinding? Binding,
    string? FailureReason)
{
    public static BodyBindingRegistryResult Reject(string reason) => new(false, null, reason);

    public static BodyBindingRegistryResult Accept(BodyBinding binding) => new(true, binding, null);
}

/// <summary>
/// Small process-local ownership table. It owns semantic exclusivity only;
/// backend liveness and generation discovery remain adapter responsibilities.
/// </summary>
public sealed class BodyBindingRegistry
{
    private readonly Dictionary<AgentId, BodyBinding> activeByAgent = new();
    private readonly Dictionary<BodyId, BodyBinding> activeByBody = new();
    private readonly Dictionary<AgentId, BodyBinding> lastByAgent = new();

    public bool HasActiveExclusiveBinding => activeByAgent.Count > 0;

    public BodyBindingRegistryResult BeginBinding(
        AgentId agent,
        BodyId body,
        BodyBindingKind kind,
        ulong generation)
    {
        if (generation == 0)
        {
            return BodyBindingRegistryResult.Reject("body_generation_missing");
        }

        if (kind != BodyBindingKind.ExclusiveControl)
        {
            return BodyBindingRegistryResult.Reject("binding_kind_unsupported");
        }

        if (activeByAgent.ContainsKey(agent))
        {
            return BodyBindingRegistryResult.Reject("agent_already_exclusively_bound");
        }

        if (activeByBody.ContainsKey(body))
        {
            return BodyBindingRegistryResult.Reject("body_already_exclusively_bound");
        }

        var binding = new BodyBinding(agent, body, kind, BodyBindingState.Binding, generation);
        activeByAgent.Add(agent, binding);
        activeByBody.Add(body, binding);
        lastByAgent[agent] = binding;
        return BodyBindingRegistryResult.Accept(binding);
    }

    public BodyBindingRegistryResult CompleteBinding(AgentId agent, BodyId body, ulong generation)
    {
        BodyBindingRegistryResult match = RequireActive(agent, body, expectedGeneration: null);
        if (!match.Accepted)
        {
            return match;
        }

        BodyBinding current = match.Binding!;
        if (current.State != BodyBindingState.Binding)
        {
            return BodyBindingRegistryResult.Reject("binding_not_in_progress");
        }

        if (generation == 0)
        {
            return BodyBindingRegistryResult.Reject("body_generation_missing");
        }

        return Replace(current with { State = BodyBindingState.Bound, Generation = generation });
    }

    public BodyBindingRegistryResult FailBinding(AgentId agent, BodyId body, bool restoreRequired)
    {
        BodyBindingRegistryResult match = RequireActive(agent, body, expectedGeneration: null);
        if (!match.Accepted)
        {
            return match;
        }

        BodyBinding failed = match.Binding! with
        {
            State = restoreRequired ? BodyBindingState.RestoreRequired : BodyBindingState.Failed,
        };
        RemoveActive(failed);
        lastByAgent[agent] = failed;
        return BodyBindingRegistryResult.Accept(failed);
    }

    public BodyBindingRegistryResult AuthorizeExclusiveCommand(
        AgentId agent,
        BodyId body,
        ulong expectedGeneration)
    {
        BodyBindingRegistryResult match = RequireActive(agent, body, expectedGeneration);
        if (!match.Accepted)
        {
            return match;
        }

        BodyBinding binding = match.Binding!;
        if (binding.State != BodyBindingState.Bound)
        {
            return BodyBindingRegistryResult.Reject("body_not_bound");
        }

        if (binding.Kind != BodyBindingKind.ExclusiveControl)
        {
            return BodyBindingRegistryResult.Reject("exclusive_control_required");
        }

        return match;
    }

    public BodyBindingRegistryResult BeginRelease(
        AgentId agent,
        BodyId body,
        ulong expectedGeneration)
    {
        BodyBindingRegistryResult match = RequireActive(agent, body, expectedGeneration);
        if (!match.Accepted)
        {
            if (lastByAgent.TryGetValue(agent, out BodyBinding? previous)
                && previous.Body == body
                && previous.Generation == expectedGeneration
                && previous.State == BodyBindingState.Released)
            {
                return BodyBindingRegistryResult.Accept(previous);
            }

            return match;
        }

        BodyBinding binding = match.Binding!;
        if (binding.State == BodyBindingState.Releasing)
        {
            return match;
        }

        if (binding.State is not BodyBindingState.Bound and not BodyBindingState.Lost)
        {
            return BodyBindingRegistryResult.Reject("body_not_bound");
        }

        return Replace(binding with { State = BodyBindingState.Releasing });
    }

    public BodyBindingRegistryResult CompleteRelease(AgentId agent, BodyId body, ulong generation)
    {
        BodyBindingRegistryResult match = RequireActive(agent, body, generation);
        if (!match.Accepted)
        {
            if (lastByAgent.TryGetValue(agent, out BodyBinding? previous)
                && previous.Body == body
                && previous.Generation == generation
                && previous.State == BodyBindingState.Released)
            {
                return BodyBindingRegistryResult.Accept(previous);
            }

            return match;
        }

        BodyBinding released = match.Binding! with { State = BodyBindingState.Released };
        RemoveActive(released);
        lastByAgent[agent] = released;
        return BodyBindingRegistryResult.Accept(released);
    }

    public BodyBindingRegistryResult MarkLost(AgentId agent, BodyId body, ulong generation)
    {
        BodyBindingRegistryResult match = RequireActive(agent, body, generation);
        if (!match.Accepted)
        {
            return match;
        }

        BodyBinding lost = match.Binding! with { State = BodyBindingState.Lost };
        return Replace(lost);
    }

    public BodyBinding? Query(AgentId agent)
    {
        return activeByAgent.TryGetValue(agent, out BodyBinding? active)
            ? active
            : lastByAgent.GetValueOrDefault(agent);
    }

    private BodyBindingRegistryResult RequireActive(
        AgentId agent,
        BodyId body,
        ulong? expectedGeneration)
    {
        if (!activeByAgent.TryGetValue(agent, out BodyBinding? byAgent))
        {
            return BodyBindingRegistryResult.Reject("agent_not_bound");
        }

        if (byAgent.Body != body)
        {
            return BodyBindingRegistryResult.Reject("wrong_body_for_agent");
        }

        if (!activeByBody.TryGetValue(body, out BodyBinding? byBody)
            || byBody.Agent != agent)
        {
            return BodyBindingRegistryResult.Reject("body_owned_by_other_agent");
        }

        if (expectedGeneration.HasValue && byAgent.Generation != expectedGeneration.Value)
        {
            return BodyBindingRegistryResult.Reject("stale_body_generation");
        }

        return BodyBindingRegistryResult.Accept(byAgent);
    }

    private BodyBindingRegistryResult Replace(BodyBinding binding)
    {
        activeByAgent[binding.Agent] = binding;
        activeByBody[binding.Body] = binding;
        lastByAgent[binding.Agent] = binding;
        return BodyBindingRegistryResult.Accept(binding);
    }

    private void RemoveActive(BodyBinding binding)
    {
        activeByAgent.Remove(binding.Agent);
        activeByBody.Remove(binding.Body);
    }
}
