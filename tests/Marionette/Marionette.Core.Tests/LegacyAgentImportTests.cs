using Aurelian.Actuation.Host;
using Marionette.Skyrim;
using Xunit;

namespace Marionette.Core.Tests;

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

    [Fact]
    public void PlacedOrigin_NormalizesPluginAndSerializesDeterministically()
    {
        var origin = new SkyrimPlacedActorOrigin("SomeMod.ESP", 0x12345);

        Assert.Equal("somemod.esp", origin.PluginName);
        Assert.Equal("somemod.esp|012345", origin.StableKey);
        Assert.Equal(origin, new SkyrimPlacedActorOrigin("somemod.esp", 0x12345));
    }

    [Fact]
    public void PlacedOrigin_RejectsMalformedIdentity()
    {
        Assert.Throws<ArgumentException>(() => new SkyrimPlacedActorOrigin(" ", 1));
        Assert.Throws<ArgumentException>(() => new SkyrimPlacedActorOrigin("mod.txt", 1));
        Assert.Throws<ArgumentException>(() => new SkyrimPlacedActorOrigin("folder/mod.esp", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SkyrimPlacedActorOrigin("mod.esp", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SkyrimPlacedActorOrigin("mod.esp", 0x0100_0000));
    }

    [Fact]
    public void PlacedOrigin_SurvivesRuntimeBodyAndLoadOrderChanges()
    {
        var registry = new ImportedAgentRegistry("first-session");
        var origin = SkyrimActorOrigin.ForPlaced(
            new SkyrimPlacedActorOrigin("Example.esm", 0x1234));
        ImportedAgentResolution first = registry.ResolveOrCreate(Body("runtime-01", 1), Data(), origin);

        Assert.True(registry.MarkBodyLost(Body("runtime-01", 1).Id));
        ImportedAgentResolution rematerialized = registry.ResolveOrCreate(
            Body("runtime-09", 2),
            Data(),
            origin);

        Assert.Equal(first.Agent!.Id, rematerialized.Agent!.Id);
        Assert.False(rematerialized.Created);
        Assert.Equal(Body("runtime-09", 2).Id, registry.CurrentBody(origin.Placed!.Value));
    }

    [Fact]
    public void PluginNamespaceSeparatesEqualLocalIdsAndLightIdsAreSupported()
    {
        var registry = new ImportedAgentRegistry("session");
        AgentId full = registry.ResolveOrCreate(
            Body("full", 1),
            Data(),
            SkyrimActorOrigin.ForPlaced(new SkyrimPlacedActorOrigin("full.esp", 0xabc))).Agent!.Id;
        AgentId light = registry.ResolveOrCreate(
            Body("light", 1),
            Data(),
            SkyrimActorOrigin.ForPlaced(new SkyrimPlacedActorOrigin("light.esl", 0xabc))).Agent!.Id;

        Assert.NotEqual(full, light);
    }

    [Fact]
    public void DynamicOrigin_RemainsSessionScoped()
    {
        SkyrimActorOrigin origin = SkyrimActorOrigin.ForDynamic("runtime-ff001234");

        AgentId first = ImportedAgentRegistry.CreateDeterministicAgentId("session-a", origin);
        AgentId second = ImportedAgentRegistry.CreateDeterministicAgentId("session-b", origin);

        Assert.NotEqual(first, second);
        Assert.Equal(SkyrimActorOriginKind.DynamicSessionReference, origin.Kind);
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
