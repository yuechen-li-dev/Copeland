using Aurelian.Actuation.Host;
using Xunit;

namespace Aurelian.Actuation.Tests;

public sealed class LegacyAgentImportTests
{
    [Fact]
    public void RepeatedBody_ResolvesSameImportedAgent()
    {
        var registry = new ImportedAgentRegistry("fixture-session");

        ImportedAgentResolution first = registry.ResolveOrCreate(Body("one", 7), Data());
        ImportedAgentResolution repeated = registry.ResolveOrCreate(Body("one", 7), Data());

        Assert.True(first.Created);
        Assert.False(repeated.Created);
        Assert.Equal(first.Agent, repeated.Agent);
        Assert.Equal(AgentProvenanceKind.ImportedLegacy, first.Agent!.Provenance.Kind);
        Assert.Equal("fixture-one", first.Agent.Provenance.SourceIdentity);
    }

    [Fact]
    public void DifferentBodies_ResolveDifferentDeterministicAgents()
    {
        var firstRegistry = new ImportedAgentRegistry("fixture-session");
        var secondRegistry = new ImportedAgentRegistry("fixture-session");

        AgentId first = firstRegistry.ResolveOrCreate(Body("one", 7), Data()).Agent!.Id;
        AgentId second = firstRegistry.ResolveOrCreate(Body("two", 7), Data()).Agent!.Id;
        AgentId repeatedFixture = secondRegistry.ResolveOrCreate(Body("one", 7), Data()).Agent!.Id;

        Assert.NotEqual(first, second);
        Assert.Equal(first, repeatedFixture);
    }

    [Fact]
    public void StaleGeneration_CannotHijackImportedAgent()
    {
        var registry = new ImportedAgentRegistry("fixture-session");
        ImportedNpcAgent original = registry.ResolveOrCreate(Body("one", 9), Data()).Agent!;

        ImportedAgentResolution stale = registry.ResolveOrCreate(Body("one", 8), Data());

        Assert.False(stale.Accepted);
        Assert.Equal("stale_body_generation", stale.FailureReason);
        Assert.Equal(original, registry.Find(new BodyId("fixture-one")));
    }

    [Fact]
    public void BodyLoss_PreservesIdentityAndNewGenerationReusesAgentByPolicy()
    {
        var registry = new ImportedAgentRegistry("fixture-session");
        ImportedNpcAgent original = registry.ResolveOrCreate(Body("one", 7), Data()).Agent!;

        Assert.True(registry.MarkBodyLost(new BodyId("fixture-one")));
        Assert.True(registry.IsBodyLost(new BodyId("fixture-one")));
        ImportedAgentResolution refreshed = registry.ResolveOrCreate(Body("one", 8), Data());

        Assert.Equal(original.Id, refreshed.Agent!.Id);
        Assert.False(refreshed.Created);
        Assert.False(registry.IsBodyLost(new BodyId("fixture-one")));
    }

    [Fact]
    public void AuthoredProvenance_RemainsDistinctFromImportedProvenance()
    {
        var authored = new AgentProvenance(
            AgentProvenanceKind.AurelianAuthored,
            "Aurelian",
            "npc.example");
        ImportedNpcAgent imported = new ImportedAgentRegistry("fixture-session")
            .ResolveOrCreate(Body("one", 7), Data()).Agent!;

        Assert.NotEqual(authored.Kind, imported.Provenance.Kind);
        Assert.DoesNotContain(
            typeof(AgentBodyCandidate).GetProperties(),
            property => property.Name.Contains("Form", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(ImportedNpcAgent).GetProperties(),
            property => property.Name.Contains("Form", StringComparison.Ordinal));
    }

    [Fact]
    public void SemanticEligibility_IsInspectable()
    {
        BodyObservation body = Body("one", 7);
        var traits = new CandidateTraits(
            IsHumanoid: true,
            IsDead: true,
            IsEssential: true,
            IsProtected: false,
            DistanceFromPlayer: 32,
            IsLoaded: true,
            CanBindExclusively: true,
            Archetype: "humanoid-corpse");

        CandidateEligibility eligibility = CandidateEligibilityPolicy.Evaluate(body, traits);

        Assert.False(eligibility.Eligible);
        Assert.Contains("essential_actor_excluded", eligibility.Reasons);
    }

    private static ImportedNpcData Data() => new(
        new IdentityProfile("Fixture NPC", "humanoid-corpse"),
        new BodyProfile(Humanoid: true, Essential: false, Protected: false),
        SelectionProfile.ImportedDefault);

    private static BodyObservation Body(string id, ulong generation) => new(
        new BodyId($"fixture-{id}"),
        IsLoaded: true,
        IsAlive: false,
        new HostPosition3(1, 2, 3),
        new BodyCapabilities(
            CanMove: true,
            CanLook: false,
            CanAnimate: false,
            CanReceiveInput: false,
            CanBeExclusiveBound: true,
            CanRestore: true),
        BodyBindingState.Unbound,
        BoundAgent: null,
        generation,
        Sequence: generation);
}
