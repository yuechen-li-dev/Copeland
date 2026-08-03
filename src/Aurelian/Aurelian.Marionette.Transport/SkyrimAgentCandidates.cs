using System.Security.Cryptography;
using System.Text;
using Aurelian.Actuation.Host;
using Dominatus.Core;
using Dominatus.Core.Decision;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using Dominatus.Core.Trace;
using Dominatus.OptFlow;
using AurelianAgentId = Aurelian.Actuation.Host.AgentId;

namespace Aurelian.Marionette.Transport;

public sealed record SkyrimBodyCandidateMapping(
    BodyId Body,
    uint ActorFormId,
    ulong ObservationGeneration);

public sealed record SkyrimCandidateSet(
    IReadOnlyList<AgentBodyCandidate> Candidates,
    IReadOnlyDictionary<BodyId, SkyrimBodyCandidateMapping> BackendMappings,
    ulong ObservationGeneration);

/// <summary>Skyrim-specific lowering boundary. Raw actor identity stops here.</summary>
public static class SkyrimCandidateLowerer
{
    private const ulong InitialImportGeneration = 1;

    public static SkyrimCandidateSet Lower(
        string sessionId,
        EligibleHostFixturesResult result,
        ImportedAgentRegistry registry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(registry);
        if (result.Status != "completed" || result.RuntimeSequence == 0)
        {
            throw new InvalidDataException(result.FailureReason ?? "candidate_query_incomplete");
        }
        if (result.Candidates.Length != result.CandidateCount)
        {
            throw new InvalidDataException("candidate_count_mismatch");
        }
        if (!MarionetteTransportClient.IsDeterministicOrder(result.Candidates))
        {
            throw new InvalidDataException("candidate_order_not_deterministic");
        }

        var candidates = new List<AgentBodyCandidate>(result.Candidates.Length);
        var mappings = new Dictionary<BodyId, SkyrimBodyCandidateMapping>();
        for (int index = 0; index < result.Candidates.Length; index++)
        {
            EligibleHostFixtureCandidate source = result.Candidates[index];
            BodyId bodyId = CreateBodyId(sessionId, source.StableSortKey);
            var body = new BodyObservation(
                bodyId,
                source.Loaded,
                IsAlive: !source.Dead,
                new HostPosition3(source.PositionX, source.PositionY, source.PositionZ),
                new BodyCapabilities(
                    CanMove: source.Loaded && source.Humanoid && source.Intact,
                    CanLook: false,
                    CanAnimate: false,
                    CanReceiveInput: false,
                    CanBeExclusiveBound: source.Loaded && source.Intact,
                    CanRestore: true),
                BodyBindingState.Unbound,
                BoundAgent: null,
                InitialImportGeneration,
                result.RuntimeSequence);
            string archetype = source.Humanoid && source.Dead
                ? "humanoid-corpse"
                : "legacy-actor";
            var data = new ImportedNpcData(
                new IdentityProfile($"Imported Skyrim NPC {index + 1}", archetype),
                new BodyProfile(source.Humanoid, source.Essential, source.Protected),
                SelectionProfile.ImportedDefault);
            ImportedAgentResolution resolution = registry.ResolveOrCreate(body, data);
            if (!resolution.Accepted)
            {
                throw new InvalidDataException(resolution.FailureReason);
            }

            var traits = new CandidateTraits(
                source.Humanoid,
                source.Dead,
                source.Essential,
                source.Protected,
                source.Distance,
                source.Loaded,
                source.EligibilityReason == "eligible" && source.Intact,
                archetype);
            CandidateEligibility eligibility = CandidateEligibilityPolicy.Evaluate(body, traits);
            candidates.Add(new AgentBodyCandidate(resolution.Agent!, body, traits, eligibility));
            mappings.Add(
                bodyId,
                new SkyrimBodyCandidateMapping(bodyId, source.FormId, InitialImportGeneration));
        }

        return new SkyrimCandidateSet(candidates, mappings, result.RuntimeSequence);
    }

    public static AgentBodyCandidate RefreshSelectedGeneration(
        AgentBodyCandidate selected,
        ulong materializationGeneration,
        ImportedAgentRegistry registry)
    {
        if (materializationGeneration == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(materializationGeneration));
        }

        BodyObservation refreshedBody = selected.Body with
        {
            Generation = materializationGeneration,
        };
        ImportedAgentResolution resolution = registry.ResolveOrCreate(
            refreshedBody,
            selected.Agent.Data);
        if (!resolution.Accepted || resolution.Agent!.Id != selected.Agent.Id)
        {
            throw new InvalidDataException(resolution.FailureReason ?? "selected_agent_identity_changed");
        }

        return selected with { Agent = resolution.Agent, Body = refreshedBody };
    }

    private static BodyId CreateBodyId(string sessionId, string stableSortKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableSortKey);
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes($"skyrim-body\n{sessionId}\n{stableSortKey}"));
        return new BodyId($"skyrim-body-{Convert.ToHexString(hash[..12]).ToLowerInvariant()}");
    }
}

public sealed record CandidateUtilityFactor(
    string Name,
    float Value,
    float Weight,
    float Contribution);

public sealed record CandidateUtilityReport(
    AurelianAgentId Agent,
    BodyId Body,
    bool Eligible,
    IReadOnlyList<CandidateUtilityFactor> Factors,
    float TotalScore);

public static class CandidateUtilityScorer
{
    public static CandidateUtilityReport Score(AgentBodyCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        SelectionProfile policy = candidate.Agent.Data.Selection;
        float distance = 1.0f - Math.Clamp(candidate.Traits.DistanceFromPlayer / 2048.0f, 0.0f, 1.0f);
        float capabilities = candidate.Body.Capabilities.CanMove
            && candidate.Body.Capabilities.CanBeExclusiveBound
            ? 1.0f
            : 0.0f;
        float archetype = string.Equals(
            candidate.Agent.Data.Identity.Archetype,
            "humanoid-corpse",
            StringComparison.Ordinal)
            ? 1.0f
            : 0.25f;
        float provenance = candidate.Agent.Provenance.Kind == AgentProvenanceKind.ImportedLegacy
            ? 1.0f
            : 0.75f;
        float denominator = 0.20f
            + policy.DistanceWeight
            + policy.CapabilityWeight
            + policy.ArchetypeWeight
            + 0.05f;
        var factors = new List<CandidateUtilityFactor>
        {
            Factor("base_preference", policy.BasePreference, 0.20f, denominator),
            Factor("distance_from_player", distance, policy.DistanceWeight, denominator),
            Factor("required_capabilities", capabilities, policy.CapabilityWeight, denominator),
            Factor("archetype_preference", archetype, policy.ArchetypeWeight, denominator),
            Factor("imported_provenance", provenance, 0.05f, denominator),
        };
        float score = candidate.Eligibility.Eligible
            ? Math.Clamp(factors.Sum(factor => factor.Contribution), 0.0f, 1.0f)
            : 0.0f;
        return new CandidateUtilityReport(
            candidate.Agent.Id,
            candidate.Body.Id,
            candidate.Eligibility.Eligible,
            factors,
            score);
    }

    private static CandidateUtilityFactor Factor(
        string name,
        float value,
        float weight,
        float denominator)
    {
        float normalizedValue = Math.Clamp(value, 0.0f, 1.0f);
        float normalizedWeight = Math.Max(weight, 0.0f);
        return new CandidateUtilityFactor(
            name,
            normalizedValue,
            normalizedWeight,
            denominator <= 0.0f ? 0.0f : normalizedValue * normalizedWeight / denominator);
    }
}

public sealed record CandidateSetUpdated(IReadOnlyList<AgentBodyCandidate> Candidates);

public sealed record AcquireBodyIntent(
    AurelianAgentId Agent,
    BodyId Body,
    ulong ExpectedGeneration);

public enum SkyrimCandidateSelectionState
{
    AwaitCandidates,
    MaterializeAgents,
    EvaluateCandidates,
    RequestSelectedBinding,
    Completed,
    NoCandidate,
    Failed,
}

public sealed class SkyrimCandidateSelectionRuntime
{
    public static readonly DecisionSlot CandidateSlot = new("Aurelian.Skyrim.CandidateAgent");
    private readonly AiWorld world = new();
    private readonly AiAgent coordinator;
    private readonly Dominatus.Core.Hfsm.HfsmInstance brain;
    private readonly SelectionTrace trace = new();
    private readonly Dictionary<AurelianAgentId, AiAgent> candidateAgents = new();
    private IReadOnlyList<AgentBodyCandidate> candidates = [];
    private IReadOnlyList<CandidateUtilityReport> utilityReports = [];
    private SkyrimCandidateSelectionState state = SkyrimCandidateSelectionState.AwaitCandidates;

    public SkyrimCandidateSelectionRuntime()
    {
        FlowDefinition flow = SkyrimCandidateSelectionFlow.Define(this);
        brain = flow.CreateBrain();
        brain.Trace = trace;
        coordinator = new AiAgent(brain);
        world.Add(coordinator);
    }

    public SkyrimCandidateSelectionState State => state;

    public AgentBodyCandidate? SelectedCandidate { get; private set; }

    public AcquireBodyIntent? DeliveredAcquireIntent { get; private set; }

    public int AcquireIntentRecipientCount { get; private set; }

    public DecisionReport? Decision => trace.LastDecision;

    public IReadOnlyList<CandidateUtilityReport> UtilityReports => utilityReports;

    public FlowInspection FlowInspection => SkyrimCandidateSelectionFlow.Define(this).Inspect();

    public bool PublishCandidates(IReadOnlyList<AgentBodyCandidate> updated)
    {
        ArgumentNullException.ThrowIfNull(updated);
        return world.Mail.Send(coordinator.Id, new CandidateSetUpdated(updated));
    }

    public SkyrimCandidateSelectionState RunUntilTerminal(int maximumTicks = 64)
    {
        for (int index = 0; index < maximumTicks; index++)
        {
            world.Tick(0.01f);
            if (state is SkyrimCandidateSelectionState.Completed
                or SkyrimCandidateSelectionState.NoCandidate
                or SkyrimCandidateSelectionState.Failed)
            {
                return state;
            }
        }

        throw new TimeoutException("Candidate selection did not reach a terminal state.");
    }

    internal void AcceptCandidateSet(CandidateSetUpdated updated)
    {
        candidates = updated.Candidates
            .OrderBy(candidate => candidate.Agent.Id.Value)
            .ToArray();
    }

    internal void SetState(SkyrimCandidateSelectionState next)
    {
        state = next;
    }

    internal void MaterializeAgents()
    {
        foreach (AgentBodyCandidate candidate in candidates)
        {
            if (candidateAgents.ContainsKey(candidate.Agent.Id))
            {
                continue;
            }

            var agent = new AiAgent(SkyrimCandidateMailboxFlow.Define().CreateBrain());
            world.Add(agent);
            candidateAgents.Add(candidate.Agent.Id, agent);
        }
    }

    internal IReadOnlyList<UtilityOption> CreateOptions()
    {
        utilityReports = candidates.Select(CandidateUtilityScorer.Score).ToArray();
        var options = new List<UtilityOption>(utilityReports.Count + 1);
        foreach (CandidateUtilityReport report in utilityReports)
        {
            string optionId = OptionId(report.Agent);
            options.Add(Ai.Option(
                optionId,
                new Consideration((_, _) => report.TotalScore),
                SkyrimCandidateSelectionFlow.States.RequestSelectedBinding));
        }
        options.Add(Ai.Option(
            "NoSafeCandidate",
            Consideration.Constant(0.01f),
            SkyrimCandidateSelectionFlow.States.NoCandidate));
        return options;
    }

    internal bool SelectBestAndDeliverIntent()
    {
        string? bestId = trace.LastDecision?.BestId;
        AgentBodyCandidate? selected = candidates.FirstOrDefault(
            candidate => string.Equals(OptionId(candidate.Agent.Id), bestId, StringComparison.Ordinal));
        if (selected is null || !selected.Eligibility.Eligible)
        {
            return false;
        }
        if (!candidateAgents.TryGetValue(selected.Agent.Id, out AiAgent? recipient))
        {
            return false;
        }

        var intent = new AcquireBodyIntent(
            selected.Agent.Id,
            selected.Body.Id,
            selected.Body.Generation);
        if (!world.Mail.Send(recipient.Id, intent))
        {
            return false;
        }

        AcquireIntentRecipientCount = candidateAgents.Values.Count(
            candidateAgent => candidateAgent.Events.CountForType<AcquireBodyIntent>() > 0);

        EventCursor cursor = default;
        if (!recipient.Events.TryConsume(ref cursor, filter: null, out AcquireBodyIntent delivered))
        {
            return false;
        }
        if (delivered.Agent != selected.Agent.Id || delivered.Body != selected.Body.Id)
        {
            return false;
        }

        SelectedCandidate = selected;
        DeliveredAcquireIntent = delivered;
        return true;
    }

    private static string OptionId(AurelianAgentId agent) => $"CandidateAgent:{agent}";

    private sealed class SelectionTrace : IAiTraceSink
    {
        public DecisionReport? LastDecision { get; private set; }

        public void OnEnter(StateId state, float time, string reason) { }

        public void OnExit(StateId state, float time, string reason) { }

        public void OnTransition(StateId from, StateId to, float time, string reason) { }

        public void OnYield(StateId state, float time, object yielded)
        {
            if (yielded is DecisionReport report)
            {
                LastDecision = report;
            }
        }
    }
}

public static partial class SkyrimCandidateSelectionFlow
{
    [DominatusFlow("aurelian.skyrim.candidate-selection.m2")]
    public static partial FlowDefinition Define(SkyrimCandidateSelectionRuntime runtime);

    [DominatusState("aurelian.skyrim.candidate-selection.root", Root = true)]
    private static IEnumerator<AiStep> Root(
        AiCtx context,
        SkyrimCandidateSelectionRuntime runtime)
    {
        yield return Ai.Push(States.AwaitCandidates, "await portable candidate event");
        yield return Ai.MatchReturn(
            Ai.OnSuccess(States.MaterializeAgents),
            Ai.OnFailure(States.Failed),
            Ai.OnReturn(States.Failed));
    }

    [DominatusState("aurelian.skyrim.candidate-selection.await-candidates")]
    private static IEnumerator<AiStep> AwaitCandidates(
        AiCtx context,
        SkyrimCandidateSelectionRuntime runtime)
    {
        runtime.SetState(SkyrimCandidateSelectionState.AwaitCandidates);
        yield return Ai.Event<CandidateSetUpdated>(
            onConsumed: (_, updated) => runtime.AcceptCandidateSet(updated),
            cursorStart: EventCursorStart.IncludeExisting);
        yield return Ai.Succeed("candidate set delivered through coordinator mailbox");
    }

    [DominatusState("aurelian.skyrim.candidate-selection.materialize-agents")]
    private static IEnumerator<AiStep> MaterializeAgents(
        AiCtx context,
        SkyrimCandidateSelectionRuntime runtime)
    {
        runtime.SetState(SkyrimCandidateSelectionState.MaterializeAgents);
        runtime.MaterializeAgents();
        yield return Ai.Goto(States.EvaluateCandidates, "imported agents materialized");
    }

    [DominatusState("aurelian.skyrim.candidate-selection.evaluate-candidates")]
    private static IEnumerator<AiStep> EvaluateCandidates(
        AiCtx context,
        SkyrimCandidateSelectionRuntime runtime)
    {
        runtime.SetState(SkyrimCandidateSelectionState.EvaluateCandidates);
        yield return Ai.Decide(
            SkyrimCandidateSelectionRuntime.CandidateSlot,
            runtime.CreateOptions(),
            hysteresis: 0.0f,
            minCommitSeconds: 0.0f,
            tieEpsilon: 0.0001f);
    }

    [DominatusState("aurelian.skyrim.candidate-selection.request-selected-binding")]
    private static IEnumerator<AiStep> RequestSelectedBinding(
        AiCtx context,
        SkyrimCandidateSelectionRuntime runtime)
    {
        runtime.SetState(SkyrimCandidateSelectionState.RequestSelectedBinding);
        yield return runtime.SelectBestAndDeliverIntent()
            ? Ai.Goto(States.Completed, "selected agent received acquire-body intent")
            : Ai.Goto(States.Failed, "selected agent intent delivery failed");
    }

    [DominatusState("aurelian.skyrim.candidate-selection.completed")]
    private static IEnumerator<AiStep> Completed(
        AiCtx context,
        SkyrimCandidateSelectionRuntime runtime)
    {
        runtime.SetState(SkyrimCandidateSelectionState.Completed);
        while (true)
        {
            yield return Ai.Steady("candidate selection complete");
        }
    }

    [DominatusState("aurelian.skyrim.candidate-selection.no-candidate")]
    private static IEnumerator<AiStep> NoCandidate(
        AiCtx context,
        SkyrimCandidateSelectionRuntime runtime)
    {
        runtime.SetState(SkyrimCandidateSelectionState.NoCandidate);
        while (true)
        {
            yield return Ai.Steady("no safe candidate");
        }
    }

    [DominatusState("aurelian.skyrim.candidate-selection.failed")]
    private static IEnumerator<AiStep> Failed(
        AiCtx context,
        SkyrimCandidateSelectionRuntime runtime)
    {
        runtime.SetState(SkyrimCandidateSelectionState.Failed);
        while (true)
        {
            yield return Ai.Steady("candidate selection failed");
        }
    }
}

public static partial class SkyrimCandidateMailboxFlow
{
    [DominatusFlow("aurelian.skyrim.imported-agent-mailbox.m2")]
    public static partial FlowDefinition Define();

    [DominatusState("aurelian.skyrim.imported-agent-mailbox.idle", Root = true)]
    private static IEnumerator<AiStep> Idle(AiCtx context)
    {
        while (true)
        {
            yield return Ai.Steady("imported agent mailbox ready");
        }
    }
}
