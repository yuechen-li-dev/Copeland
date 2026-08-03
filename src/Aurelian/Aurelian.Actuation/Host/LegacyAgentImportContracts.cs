using System.Security.Cryptography;
using System.Text;

namespace Aurelian.Actuation.Host;

public enum AgentProvenanceKind
{
    ImportedLegacy,
    AurelianAuthored,
}

public sealed record AgentProvenance(
    AgentProvenanceKind Kind,
    string Source,
    string? SourceIdentity);

public sealed record IdentityProfile(
    string DisplayName,
    string Archetype);

public sealed record BodyProfile(
    bool Humanoid,
    bool Essential,
    bool Protected);

public sealed record SelectionProfile(
    float BasePreference,
    float DistanceWeight,
    float CapabilityWeight,
    float ArchetypeWeight)
{
    public static SelectionProfile ImportedDefault { get; } = new(
        BasePreference: 0.5f,
        DistanceWeight: 0.45f,
        CapabilityWeight: 0.30f,
        ArchetypeWeight: 0.25f);
}

public sealed record ImportedNpcData(
    IdentityProfile Identity,
    BodyProfile Body,
    SelectionProfile Selection);

public sealed record ImportedNpcAgent(
    AgentId Id,
    AgentProvenance Provenance,
    ImportedNpcData Data);

public sealed record CandidateTraits(
    bool IsHumanoid,
    bool IsDead,
    bool IsEssential,
    bool IsProtected,
    float DistanceFromPlayer,
    bool IsLoaded,
    bool CanBindExclusively,
    string? Archetype);

public sealed record CandidateEligibility(
    bool Eligible,
    IReadOnlyList<string> Reasons);

public sealed record AgentBodyCandidate(
    ImportedNpcAgent Agent,
    BodyObservation Body,
    CandidateTraits Traits,
    CandidateEligibility Eligibility);

public sealed record ImportedAgentResolution(
    bool Accepted,
    ImportedNpcAgent? Agent,
    bool Created,
    string? FailureReason)
{
    public static ImportedAgentResolution Reject(string reason) => new(false, null, false, reason);

    public static ImportedAgentResolution Accept(ImportedNpcAgent agent, bool created) =>
        new(true, agent, created, null);
}

/// <summary>
/// Session-scoped legacy import catalog. Body identity is already opaque at
/// this boundary; the catalog never receives or parses backend FormIDs.
/// Newer observations explicitly refresh the same imported agent, while
/// stale observations are rejected.
/// </summary>
public sealed class ImportedAgentRegistry
{
    private readonly string sessionScope;
    private readonly Dictionary<BodyId, ImportedNpcAgent> agentsByBody = new();
    private readonly Dictionary<BodyId, ulong> latestGenerationByBody = new();
    private readonly HashSet<BodyId> lostBodies = [];

    public ImportedAgentRegistry(string sessionScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionScope);
        this.sessionScope = sessionScope;
    }

    public ImportedAgentResolution ResolveOrCreate(
        BodyObservation body,
        ImportedNpcData data)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(data);
        if (body.Generation == 0)
        {
            return ImportedAgentResolution.Reject("body_generation_missing");
        }

        if (latestGenerationByBody.TryGetValue(body.Id, out ulong latest)
            && body.Generation < latest)
        {
            return ImportedAgentResolution.Reject("stale_body_generation");
        }

        if (agentsByBody.TryGetValue(body.Id, out ImportedNpcAgent? existing))
        {
            latestGenerationByBody[body.Id] = body.Generation;
            lostBodies.Remove(body.Id);
            return ImportedAgentResolution.Accept(existing, created: false);
        }

        var provenance = new AgentProvenance(
            AgentProvenanceKind.ImportedLegacy,
            "Skyrim/Marionette",
            body.Id.Value);
        var created = new ImportedNpcAgent(
            CreateDeterministicAgentId(sessionScope, body.Id),
            provenance,
            data);
        agentsByBody.Add(body.Id, created);
        latestGenerationByBody.Add(body.Id, body.Generation);
        return ImportedAgentResolution.Accept(created, created: true);
    }

    public bool MarkBodyLost(BodyId body)
    {
        if (!agentsByBody.ContainsKey(body))
        {
            return false;
        }

        lostBodies.Add(body);
        return true;
    }

    public bool IsBodyLost(BodyId body) => lostBodies.Contains(body);

    public ImportedNpcAgent? Find(BodyId body) => agentsByBody.GetValueOrDefault(body);

    public static AgentId CreateDeterministicAgentId(string sessionScope, BodyId body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionScope);
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes($"aurelian-imported-agent\n{sessionScope}\n{body.Value}"));
        byte[] guidBytes = hash[..16];
        guidBytes[7] = (byte)((guidBytes[7] & 0x0f) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3f) | 0x80);
        return new AgentId(new Guid(guidBytes));
    }
}

public static class CandidateEligibilityPolicy
{
    public static CandidateEligibility Evaluate(BodyObservation body, CandidateTraits traits)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(traits);
        var reasons = new List<string>();
        if (!body.IsLoaded || !traits.IsLoaded)
        {
            reasons.Add("body_not_loaded");
        }
        if (!body.Capabilities.CanMove)
        {
            reasons.Add("movement_capability_missing");
        }
        if (!body.Capabilities.CanBeExclusiveBound || !traits.CanBindExclusively)
        {
            reasons.Add("exclusive_binding_unavailable");
        }
        if (!traits.IsHumanoid)
        {
            reasons.Add("humanoid_required");
        }
        if (!traits.IsDead)
        {
            reasons.Add("corpse_policy_required");
        }
        if (traits.IsEssential)
        {
            reasons.Add("essential_actor_excluded");
        }
        if (traits.IsProtected)
        {
            reasons.Add("protected_actor_excluded");
        }

        return new CandidateEligibility(reasons.Count == 0, reasons);
    }
}
