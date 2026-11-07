using Marionette.Skyrim;
using Marionette.Skyrim.App;
using Xunit;

namespace Marionette.Skyrim.App.Tests;

public sealed class SkyrimCharacterSwitchTests
{
    private static BodyCharacterInfo Info(uint form, uint cell = 93151) =>
        new(form, "Skyrim.esm", form, "Marcurio", "The Bee and Barb", "Skyrim.esm", cell, 268, 315, 64, false, "");

    [Fact]
    public void RegisteringACharacterPreservesSelectionAndUpdatesItsStableLocation()
    {
        var owner = new SkyrimWorldOwnerRuntime("menu");
        owner.RecordEmbodiment(new(LeaseId: 1, WorldGeneration: 3, FormId: 856054,
            PluginName: "Skyrim.esm", LocalFormId: 856054));
        Guid selected = owner.ReadEmbodiment()!.SelectedCharacterId!.Value;
        EmbodiedCharacter first = owner.RegisterCharacter(Info(760198));
        EmbodiedCharacter changed = owner.RegisterCharacter(Info(760198) with { PositionX = 400 });
        Assert.Equal(first.CharacterId, changed.CharacterId);
        Assert.Equal(selected, owner.ReadEmbodiment()!.SelectedCharacterId);
        Assert.Equal(2, owner.ReadEmbodiment()!.Version);
        Assert.Equal(400, changed.LastLocation!.PositionX);
        Assert.Equal(2, owner.ReadEmbodiment()!.Characters.Length);
    }

    [Fact]
    public void InvalidLocationCannotBecomeACanonicalRosterEntry()
    {
        var owner = new SkyrimWorldOwnerRuntime("invalid");
        Assert.Throws<InvalidDataException>(() => owner.RegisterCharacter(Info(760198) with { PositionX = float.NaN }));
        Assert.Throws<ArgumentException>(() => owner.RegisterCharacter(Info(760198) with { UnavailableReason = "dead" }));
        Assert.Null(owner.ReadEmbodiment());
    }

    [Fact]
    public async Task TravelReleasesOnlyTheLiveLeaseAndDoesNotClearPersistentSelection()
    {
        var fixture = new Fixture();
        await fixture.Bodies.AcquireAsync(new(3, 856054));
        await fixture.Bodies.TravelToCharacterAsync(new("Skyrim.esm", 760198), Info(760198).Location, 3);
        Assert.Null(fixture.Bodies.Lease);
        Assert.NotNull(fixture.Owner.ReadEmbodiment()!.Selected);
        BodyControlRequest travel = fixture.Requests[^1];
        Assert.Equal("travel", travel.BodyAction);
        Assert.Equal(41U, travel.ExpectedBodyLeaseId);
        Assert.Equal(93151U, travel.BodyCellLocalFormId);
        Assert.Equal("Skyrim.esm", travel.BodyCellPlugin);
        Assert.Equal(268, travel.TargetPositionX);
    }

    [Fact]
    public async Task RejectedOrUncertainTravelNeverRetriesAndPreservesKnownOwnership()
    {
        var fixture = new Fixture();
        await fixture.Bodies.AcquireAsync(new(3, 856054));
        SkyrimBodyLease lease = fixture.Bodies.Lease!;
        fixture.Failure = "body_destination_unavailable";
        await Assert.ThrowsAsync<SkyrimBodyControlException>(async () =>
            await fixture.Bodies.TravelToCharacterAsync(new("Skyrim.esm", 760198), Info(760198).Location, 3));
        Assert.Equal(lease, fixture.Bodies.Lease);
        fixture.Failure = "dispatch_timeout";
        await Assert.ThrowsAsync<SkyrimBodyControlException>(async () =>
            await fixture.Bodies.TravelToCharacterAsync(new("Skyrim.esm", 760198), Info(760198).Location, 3));
        Assert.Equal(3, fixture.Requests.Count);
        Assert.Equal(lease, fixture.Bodies.Lease);
    }

    [Fact]
    public async Task AQueuedSwitchCannotCrossAWorldReplacementOrAClosedLifecycleGate()
    {
        var fixture = new Fixture();
        await fixture.Bodies.AcquireAsync(new(3, 856054));
        EmbodiedCharacter remote = fixture.Owner.RegisterCharacter(Info(760198));
        var controller = new SkyrimCharacterSwitchController();
        var state = new SkyrimStateResult(1, "skyrim_state_result", "state", 1, "completed", null,
            true, 1, true, 20, null, false, null, null, false, null, null, 20,
            WorldReady: true, WorldPaused: false, WorldGeneration: 3);
        var first = new SkyrimHostContext(Stream.Null, state, fixture.Owner, fixture.Bodies);
        controller.Select(first, remote.CharacterId);
        int calls = fixture.Requests.Count;
        Assert.True(await controller.TickAsync(first, false, CancellationToken.None));
        Assert.Equal(calls, fixture.Requests.Count);
        var replacement = new SkyrimHostContext(Stream.Null, state, new SkyrimWorldOwnerRuntime("new-save"), fixture.Bodies);
        Assert.False(await controller.TickAsync(replacement, true, CancellationToken.None));
        Assert.False(controller.IsSwitching);
        Assert.Equal(calls, fixture.Requests.Count);
    }

    [Fact]
    public void VersionOneRosterStillLoadsAndUnknownVersionsRemainInvalid()
    {
        var original = new EmbodimentState(1, null, [new(Guid.NewGuid(), "Skyrim.esm", 856054)]);
        original.Validate();
        Assert.Throws<InvalidDataException>(() => (original with { Version = 3 }).Validate());
    }

    [Fact]
    public void TemporaryOwnedActorMetadataDoesNotBecomeAPlacedCharacter()
    {
        var owner = new SkyrimWorldOwnerRuntime("owned");
        EmbodiedCharacter placed = owner.RegisterCharacter(Info(760198));
        owner.WriteEmbodiment(owner.ReadEmbodiment()! with { SelectedCharacterId = placed.CharacterId });
        BodyCharacterInfo transient = Info(0xFF000800) with { PluginName = "", LocalFormId = 0 };
        owner.RecordEmbodiment(new(LeaseId: 2, WorldGeneration: 3, FormId: 0xFF000800, Loaded: true, Character: transient));
        Assert.Null(owner.ReadEmbodiment()!.SelectedCharacterId);
        Assert.Equal(placed, Assert.Single(owner.ReadEmbodiment()!.Characters));
    }

    [Fact]
    public async Task NewlyUnavailableFurnitureTargetCannotReleaseTheCurrentBody()
    {
        var fixture = new Fixture();
        await fixture.Bodies.AcquireAsync(new(3, 856054));
        SkyrimBodyLease source = fixture.Bodies.Lease!;
        Guid sourceCharacter = fixture.Owner.ReadEmbodiment()!.SelectedCharacterId!.Value;
        EmbodiedCharacter remote = fixture.Owner.RegisterCharacter(Info(760198));
        fixture.Description = Info(760198) with { UnavailableReason = "body_actor_in_furniture" };
        var state = new SkyrimStateResult(1, "skyrim_state_result", "state", 1, "completed", null,
            true, 1, true, 20, null, false, null, null, false, null, null, 20,
            WorldReady: true, WorldPaused: false, WorldGeneration: 3);
        var context = new SkyrimHostContext(Stream.Null, state, fixture.Owner, fixture.Bodies);
        var controller = new SkyrimCharacterSwitchController();
        controller.Select(context, remote.CharacterId);
        await controller.TickAsync(context, true, CancellationToken.None);
        Assert.False(controller.IsSwitching);
        Assert.Equal(source, fixture.Bodies.Lease);
        Assert.Equal(sourceCharacter, fixture.Owner.ReadEmbodiment()!.SelectedCharacterId);
        Assert.Equal(new[] { "acquire", "describe" }, fixture.Requests.Select(request => request.BodyAction));
    }

    private sealed class Fixture
    {
        private ulong sequence;
        private BodyControlObservation current = new();
        public SkyrimWorldOwnerRuntime Owner { get; } = new("fixture");
        public List<BodyControlRequest> Requests { get; } = [];
        public string Failure { get; set; } = "";
        public BodyCharacterInfo? Description { get; set; }
        public SkyrimBodyControl Bodies { get; }

        public Fixture()
        {
            Bodies = new SkyrimBodyControl((request, _) =>
            {
                Requests.Add(request);
                if (Failure.Length == 0)
                {
                    if (request.BodyAction == "acquire") current = new(41, 3, request.TargetFormId,
                        PluginName: "Skyrim.esm", LocalFormId: request.TargetFormId);
                    if (request.BodyAction == "travel") current = new();
                }
                return ValueTask.FromResult(new BodyControlResult(1, "body_control_result", request.RequestId,
                    ++sequence, Failure.Length == 0 ? "completed" : "failed", Failure, 3, current,
                    Character: Description));
            }, Owner.RecordEmbodiment, Owner.ClearEmbodimentSelection);
        }
    }
}
