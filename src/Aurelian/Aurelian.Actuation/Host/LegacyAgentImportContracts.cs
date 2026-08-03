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
    string? SourceIdentity)
{
    public SkyrimActorOrigin? SkyrimOrigin { get; init; }
}

public enum SkyrimActorOriginKind
{
    PlacedPluginReference,
    DynamicSessionReference,
}

/// <summary>
/// Load-order-independent identity for a placed Skyrim reference. Plugin names
/// are normalized to lower case and local IDs are formatted as six hexadecimal
/// digits. The 24-bit bound covers full plugins and the 12-bit subset used by
/// light plugins without encoding a runtime load-order prefix.
/// </summary>
public readonly record struct SkyrimPlacedActorOrigin
{
    public SkyrimPlacedActorOrigin(string pluginName, uint localFormId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);
        string fileName = Path.GetFileName(pluginName.Trim());
        string extension = Path.GetExtension(fileName);
        if (!string.Equals(extension, ".esm", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".esp", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".esl", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Skyrim plugin name must end in .esm, .esp, or .esl.", nameof(pluginName));
        }
        if (!string.Equals(fileName, pluginName.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Skyrim plugin identity must be a filename, not a path.", nameof(pluginName));
        }
        if (localFormId == 0 || localFormId > 0x00ff_ffffU)
        {
            throw new ArgumentOutOfRangeException(nameof(localFormId));
        }

        PluginName = fileName.ToLowerInvariant();
        LocalFormId = localFormId;
    }

    public string PluginName { get; }

    public uint LocalFormId { get; }

    public string StableKey => $"{PluginName}|{LocalFormId:X6}";

    public override string ToString() => StableKey;
}

public sealed record SkyrimActorOrigin
{
    private SkyrimActorOrigin(
        SkyrimActorOriginKind kind,
        SkyrimPlacedActorOrigin? placed,
        string? sessionReference)
    {
        Kind = kind;
        Placed = placed;
        SessionReference = sessionReference;
    }

    public SkyrimActorOriginKind Kind { get; }

    public SkyrimPlacedActorOrigin? Placed { get; }

    public string? SessionReference { get; }

    public static SkyrimActorOrigin ForPlaced(SkyrimPlacedActorOrigin origin) =>
        new(SkyrimActorOriginKind.PlacedPluginReference, origin, null);

    public static SkyrimActorOrigin ForDynamic(string sessionReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionReference);
        return new SkyrimActorOrigin(
            SkyrimActorOriginKind.DynamicSessionReference,
            null,
            sessionReference.Trim());
    }

    public string StableKey => Kind == SkyrimActorOriginKind.PlacedPluginReference
        ? $"placed:{Placed!.Value.StableKey}"
        : $"dynamic:{SessionReference}";
}

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
/// Legacy import catalog. Placed origins map to semantic identities independently
/// of current BodyIds. Dynamic origins remain explicitly session scoped.
/// </summary>
public sealed class ImportedAgentRegistry
{
    private readonly string sessionScope;
    private readonly Dictionary<BodyId, ImportedNpcAgent> agentsByBody = new();
    private readonly Dictionary<string, ImportedNpcAgent> agentsByOrigin = new(StringComparer.Ordinal);
    private readonly Dictionary<BodyId, string> originsByBody = new();
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
        return ResolveOrCreate(
            body,
            data,
            SkyrimActorOrigin.ForDynamic(body.Id.Value));
    }

    public ImportedAgentResolution ResolveOrCreate(
        BodyObservation body,
        ImportedNpcData data,
        SkyrimActorOrigin origin)
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

        string originKey = OriginKey(origin);
        if (agentsByOrigin.TryGetValue(originKey, out ImportedNpcAgent? existing))
        {
            agentsByBody[body.Id] = existing;
            originsByBody[body.Id] = originKey;
            latestGenerationByBody[body.Id] = body.Generation;
            lostBodies.Remove(body.Id);
            return ImportedAgentResolution.Accept(existing, created: false);
        }

        var provenance = new AgentProvenance(
            AgentProvenanceKind.ImportedLegacy,
            "Skyrim/Marionette",
            origin.Kind == SkyrimActorOriginKind.PlacedPluginReference
                ? origin.StableKey
                : body.Id.Value)
        {
            SkyrimOrigin = origin,
        };
        var created = new ImportedNpcAgent(
            CreateDeterministicAgentId(sessionScope, origin),
            provenance,
            data);
        agentsByOrigin.Add(originKey, created);
        agentsByBody[body.Id] = created;
        originsByBody[body.Id] = originKey;
        latestGenerationByBody[body.Id] = body.Generation;
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

    public ImportedNpcAgent? Find(SkyrimPlacedActorOrigin origin) =>
        agentsByOrigin.GetValueOrDefault($"placed:{origin.StableKey}");

    public IReadOnlyList<ImportedNpcAgent> PlacedAgents => agentsByOrigin
        .Where(entry => entry.Key.StartsWith("placed:", StringComparison.Ordinal))
        .Select(entry => entry.Value)
        .OrderBy(agent => agent.Id.Value)
        .ToArray();

    public void RegisterRestored(ImportedNpcAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        SkyrimActorOrigin origin = agent.Provenance.SkyrimOrigin
            ?? throw new ArgumentException("Restored imported agent must have Skyrim origin.", nameof(agent));
        if (origin.Kind != SkyrimActorOriginKind.PlacedPluginReference)
        {
            throw new ArgumentException("Dynamic Skyrim agents are not durable across sessions.", nameof(agent));
        }

        string originKey = OriginKey(origin);
        AgentId expected = CreateDeterministicAgentId(sessionScope, origin);
        if (expected != agent.Id)
        {
            throw new ArgumentException("Restored imported agent identity does not match its placed origin.", nameof(agent));
        }

        agentsByOrigin[originKey] = agent;
    }

    public BodyId? CurrentBody(SkyrimPlacedActorOrigin origin)
    {
        string key = $"placed:{origin.StableKey}";
        foreach ((BodyId body, string mappedOrigin) in originsByBody)
        {
            if (string.Equals(mappedOrigin, key, StringComparison.Ordinal)
                && !lostBodies.Contains(body))
            {
                return body;
            }
        }

        return null;
    }

    public static AgentId CreateDeterministicAgentId(string sessionScope, BodyId body)
    {
        return CreateDeterministicAgentId(
            sessionScope,
            SkyrimActorOrigin.ForDynamic(body.Value));
    }

    public static AgentId CreateDeterministicAgentId(
        string sessionScope,
        SkyrimActorOrigin origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionScope);
        string identityScope = origin.Kind == SkyrimActorOriginKind.PlacedPluginReference
            ? "placed"
            : $"dynamic:{sessionScope}";
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes($"aurelian-imported-agent-v2\n{identityScope}\n{origin.StableKey}"));
        byte[] guidBytes = hash[..16];
        guidBytes[7] = (byte)((guidBytes[7] & 0x0f) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3f) | 0x80);
        return new AgentId(new Guid(guidBytes));
    }

    private string OriginKey(SkyrimActorOrigin origin)
    {
        return origin.Kind == SkyrimActorOriginKind.PlacedPluginReference
            ? origin.StableKey
            : $"session:{sessionScope}:{origin.StableKey}";
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
