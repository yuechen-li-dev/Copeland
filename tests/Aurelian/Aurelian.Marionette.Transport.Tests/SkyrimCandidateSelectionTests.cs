using Aurelian.Actuation.Host;
using Xunit;

namespace Aurelian.Marionette.Transport.Tests;

public sealed class SkyrimCandidateSelectionTests
{
    [Fact]
    public void Lowering_ProducesOpaqueStableBodiesAndImportedAgents()
    {
        var registry = new ImportedAgentRegistry("session-one");
        EligibleHostFixturesResult observation = Result(
            Candidate(0x1234, 20, "actor-a"),
            Candidate(0x5678, 40, "actor-b"));

        SkyrimCandidateSet first = SkyrimCandidateLowerer.Lower("session-one", observation, registry);
        SkyrimCandidateSet repeated = SkyrimCandidateLowerer.Lower("session-one", observation, registry);

        Assert.Equal(2, first.Candidates.Count);
        Assert.Equal(
            first.Candidates.Select(candidate => candidate.Agent.Id),
            repeated.Candidates.Select(candidate => candidate.Agent.Id));
        Assert.Equal(
            first.Candidates.Select(candidate => candidate.Body.Id),
            repeated.Candidates.Select(candidate => candidate.Body.Id));
        Assert.All(first.Candidates, candidate =>
        {
            Assert.Equal(AgentProvenanceKind.ImportedLegacy, candidate.Agent.Provenance.Kind);
            Assert.DoesNotContain("1234", candidate.Body.Id.Value, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("5678", candidate.Body.Id.Value, StringComparison.OrdinalIgnoreCase);
            Assert.Equal((ulong)1, candidate.Body.Generation);
            Assert.Equal((ulong)7, candidate.Body.Sequence);
            Assert.True(candidate.Eligibility.Eligible);
        });
        Assert.Contains(first.BackendMappings.Values, mapping => mapping.ActorFormId == 0x1234);

        AgentBodyCandidate selected = first.Candidates[0];
        AgentBodyCandidate refreshed = SkyrimCandidateLowerer.RefreshSelectedGeneration(
            selected,
            materializationGeneration: 3,
            registry);
        Assert.Equal(selected.Agent.Id, refreshed.Agent.Id);
        Assert.Equal((ulong)3, refreshed.Body.Generation);
    }

    [Fact]
    public void Selection_UsesDominatusDecisionAndNearestValidCandidateWins()
    {
        AgentBodyCandidate far = PortableCandidate("far", 300);
        AgentBodyCandidate near = PortableCandidate("near", 30);
        var runtime = new SkyrimCandidateSelectionRuntime();

        Assert.True(runtime.PublishCandidates([far, near]));
        SkyrimCandidateSelectionState terminal = runtime.RunUntilTerminal();

        Assert.Equal(SkyrimCandidateSelectionState.Completed, terminal);
        Assert.Equal(near.Agent.Id, runtime.SelectedCandidate!.Agent.Id);
        Assert.Equal(near.Agent.Id, runtime.DeliveredAcquireIntent!.Agent);
        Assert.Equal(1, runtime.AcquireIntentRecipientCount);
        Assert.Equal("Aurelian.Skyrim.CandidateAgent", SkyrimCandidateSelectionRuntime.CandidateSlot.Id);
        Assert.Contains(runtime.UtilityReports, report =>
            report.Factors.Any(factor => factor.Name == "distance_from_player"));
    }

    [Fact]
    public void Selection_RejectsUnbindableStaleOrProtectedCandidates()
    {
        AgentBodyCandidate valid = PortableCandidate("valid", 100);
        AgentBodyCandidate unbindable = PortableCandidate("unbindable", 1) with
        {
            Body = PortableCandidate("unbindable", 1).Body with
            {
                Capabilities = PortableCandidate("unbindable", 1).Body.Capabilities with
                {
                    CanBeExclusiveBound = false,
                },
            },
        };
        unbindable = unbindable with
        {
            Eligibility = CandidateEligibilityPolicy.Evaluate(unbindable.Body, unbindable.Traits),
        };
        AgentBodyCandidate protectedNpc = PortableCandidate("protected", 2, isProtected: true);
        var runtime = new SkyrimCandidateSelectionRuntime();

        runtime.PublishCandidates([unbindable, protectedNpc, valid]);
        runtime.RunUntilTerminal();

        Assert.Equal(valid.Agent.Id, runtime.SelectedCandidate!.Agent.Id);
        Assert.Equal(0, runtime.UtilityReports.Single(report => report.Agent == unbindable.Agent.Id).TotalScore);
        Assert.Equal(0, runtime.UtilityReports.Single(report => report.Agent == protectedNpc.Agent.Id).TotalScore);
    }

    [Fact]
    public void StrongArchetypePreference_CanOutweighDistance()
    {
        AgentBodyCandidate near = PortableCandidate("near", 10) with
        {
            Agent = PortableCandidate("near", 10).Agent with
            {
                Data = PortableCandidate("near", 10).Agent.Data with
                {
                    Identity = new IdentityProfile("Near", "legacy-actor"),
                    Selection = new SelectionProfile(0.5f, 0.1f, 0.1f, 0.8f),
                },
            },
        };
        AgentBodyCandidate preferred = PortableCandidate("preferred", 800) with
        {
            Agent = PortableCandidate("preferred", 800).Agent with
            {
                Data = PortableCandidate("preferred", 800).Agent.Data with
                {
                    Selection = new SelectionProfile(0.5f, 0.1f, 0.1f, 0.8f),
                },
            },
        };
        var runtime = new SkyrimCandidateSelectionRuntime();

        runtime.PublishCandidates([near, preferred]);
        runtime.RunUntilTerminal();

        Assert.Equal(preferred.Agent.Id, runtime.SelectedCandidate!.Agent.Id);
    }

    [Fact]
    public void Ties_AreStableAndIndependentFromNativeQueryOrder()
    {
        AgentBodyCandidate first = PortableCandidate("a", 100);
        AgentBodyCandidate second = PortableCandidate("b", 100);
        AgentId expected = new[] { first.Agent.Id, second.Agent.Id }
            .OrderBy(id => id.Value)
            .First();

        var forward = new SkyrimCandidateSelectionRuntime();
        forward.PublishCandidates([first, second]);
        forward.RunUntilTerminal();
        var reverse = new SkyrimCandidateSelectionRuntime();
        reverse.PublishCandidates([second, first]);
        reverse.RunUntilTerminal();

        Assert.Equal(expected, forward.SelectedCandidate!.Agent.Id);
        Assert.Equal(expected, reverse.SelectedCandidate!.Agent.Id);
    }

    [Fact]
    public void DuplicateCandidateUpdate_IsHandledDeterministically()
    {
        AgentBodyCandidate first = PortableCandidate("a", 100);
        AgentBodyCandidate second = PortableCandidate("b", 200);
        var runtime = new SkyrimCandidateSelectionRuntime();

        Assert.True(runtime.PublishCandidates([first, second]));
        Assert.True(runtime.PublishCandidates([first, second]));

        Assert.Equal(SkyrimCandidateSelectionState.Completed, runtime.RunUntilTerminal());
        Assert.Equal(first.Agent.Id, runtime.SelectedCandidate!.Agent.Id);
        Assert.Equal(1, runtime.AcquireIntentRecipientCount);
    }

    [Fact]
    public void NoValidCandidate_RoutesExplicitlyToNoCandidate()
    {
        var runtime = new SkyrimCandidateSelectionRuntime();

        runtime.PublishCandidates([PortableCandidate("protected", 10, isProtected: true)]);

        Assert.Equal(SkyrimCandidateSelectionState.NoCandidate, runtime.RunUntilTerminal());
        Assert.Null(runtime.SelectedCandidate);
        Assert.Equal("NoSafeCandidate", runtime.Decision!.BestId);
    }

    [Fact]
    public void GeneratedSelectionFlows_HaveAuthoredStatesAndNoHiddenStates()
    {
        var runtime = new SkyrimCandidateSelectionRuntime();
        string[] ids = runtime.FlowInspection.States.Select(state => state.Id.Value).ToArray();

        Assert.Equal(8, ids.Length);
        Assert.All(ids, id => Assert.StartsWith("aurelian.skyrim.candidate-selection.", id));
        Assert.Empty(runtime.FlowInspection.GeneratedArtifacts);
        Assert.Empty(SkyrimCandidateMailboxFlow.Define().Inspect().GeneratedArtifacts);
    }

    private static EligibleHostFixturesResult Result(params EligibleHostFixtureCandidate[] candidates) => new(
        MarionetteWireProtocol.Version,
        "eligible_host_fixtures_result",
        "request-1",
        ServerSequence: 4,
        Status: "completed",
        RuntimeSequence: 7,
        OriginPlayerFormId: 0x14,
        InspectedActorCount: 3,
        CandidateCount: checked((uint)candidates.Length),
        Candidates: candidates,
        FailureReason: null);

    private static EligibleHostFixtureCandidate Candidate(
        uint formId,
        float distance,
        string stableKey) => new(
        formId,
        BaseFormId: null,
        distance,
        Dead: true,
        Humanoid: true,
        Essential: false,
        Protected: false,
        Intact: true,
        EligibilityReason: "eligible",
        Loaded: true,
        stableKey,
        PositionX: distance,
        PositionY: 0,
        PositionZ: 0);

    private static AgentBodyCandidate PortableCandidate(
        string id,
        float distance,
        bool isProtected = false)
    {
        var body = new BodyObservation(
            new BodyId($"body-{id}"),
            IsLoaded: true,
            IsAlive: false,
            new HostPosition3(distance, 0, 0),
            new BodyCapabilities(true, false, false, false, true, true),
            BodyBindingState.Unbound,
            BoundAgent: null,
            Generation: 7,
            Sequence: 11);
        ImportedNpcAgent agent = new ImportedAgentRegistry("selection-session")
            .ResolveOrCreate(
                body,
                new ImportedNpcData(
                    new IdentityProfile(id, "humanoid-corpse"),
                    new BodyProfile(true, false, isProtected),
                    SelectionProfile.ImportedDefault))
            .Agent!;
        var traits = new CandidateTraits(
            IsHumanoid: true,
            IsDead: true,
            IsEssential: false,
            IsProtected: isProtected,
            distance,
            IsLoaded: true,
            CanBindExclusively: true,
            Archetype: "humanoid-corpse");
        return new AgentBodyCandidate(
            agent,
            body,
            traits,
            CandidateEligibilityPolicy.Evaluate(body, traits));
    }
}
