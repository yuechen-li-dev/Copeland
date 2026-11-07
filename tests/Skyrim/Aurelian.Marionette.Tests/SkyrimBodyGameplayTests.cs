using System.Text.Json;
using Marionette.Skyrim.App;
using Xunit;

namespace Marionette.Skyrim.App.Tests;

public sealed class SkyrimBodyGameplayTests
{
    [Fact]
    public async Task InventoryReceiptsCannotBeAppliedToAnotherBodyOrRotatedLease()
    {
        var fixture = new Fixture();
        await fixture.Controller.AcquireAsync(new(3, 100));
        BodyGameplaySnapshot original = await fixture.Controller.ObserveGameplayAsync();
        await fixture.Controller.SwitchAsync(new(3, 200));
        int calls = fixture.Requests.Count;
        await Assert.ThrowsAsync<ArgumentException>(async () => await fixture.Controller.DropAsync(original, 15));
        Assert.Equal(calls, fixture.Requests.Count);
        await fixture.Controller.SwitchAsync(new(3, 100));
        calls = fixture.Requests.Count;
        await Assert.ThrowsAsync<ArgumentException>(async () => await fixture.Controller.EquipAsync(original, 15));
        Assert.Equal(calls, fixture.Requests.Count);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public async Task QuestItemsAndAmbiguousInstancesNeverIssueAMutation(bool quest, uint stacks)
    {
        var fixture = new Fixture();
        fixture.Item = fixture.Item with { FormId = 1000, QuestObject = quest, ExtraStacks = stacks };
        await fixture.Controller.AcquireAsync(new(3, 100));
        BodyGameplaySnapshot snapshot = await fixture.Controller.ObserveGameplayAsync();
        int calls = fixture.Requests.Count;
        await Assert.ThrowsAsync<ArgumentException>(async () => await fixture.Controller.DropAsync(snapshot, 1000));
        Assert.Equal(calls, fixture.Requests.Count);
        Assert.NotNull(fixture.Controller.Lease);
    }

    [Fact]
    public async Task FungibleGoldUsesTheCountReceiptAcrossSplitEngineStacks()
    {
        var fixture = new Fixture();
        fixture.Item = fixture.Item with { ExtraStacks = 2 };
        await fixture.Controller.AcquireAsync(new(3, 100));
        BodyGameplaySnapshot snapshot = await fixture.Controller.ObserveGameplayAsync();
        await fixture.Controller.DropAsync(snapshot, 15, 1);
        Assert.Equal("drop", fixture.Requests[^1].BodyAction);
        Assert.Equal(11U, fixture.Requests[^1].BodyExpectedItemCount);
        Assert.Equal(1U, fixture.Requests[^1].BodyItemCount);
        Assert.Equal(snapshot.Lease, fixture.Controller.Lease);
    }

    [Fact]
    public async Task InventoryChangeIsRejectedWithoutRetryAndRetainsCurrentOwnership()
    {
        var fixture = new Fixture();
        await fixture.Controller.AcquireAsync(new(3, 100));
        BodyGameplaySnapshot snapshot = await fixture.Controller.ObserveGameplayAsync();
        fixture.Failure = "body_inventory_changed";
        int calls = fixture.Requests.Count;
        var rejected = await Assert.ThrowsAsync<SkyrimBodyControlException>(async () =>
            await fixture.Controller.DropAsync(snapshot, 15, 2));
        Assert.Equal("body_inventory_changed", rejected.Reason);
        Assert.Equal(calls + 1, fixture.Requests.Count);
        Assert.Equal(snapshot.Lease, fixture.Controller.Lease);
        Assert.Equal(11U, fixture.Requests[^1].BodyExpectedItemCount);
        Assert.Equal(2U, fixture.Requests[^1].BodyItemCount);
    }

    [Fact]
    public async Task GameplayReceiptMustDescribeTheOwnedBodyAndPlayerQuestAuthority()
    {
        var fixture = new Fixture();
        await fixture.Controller.AcquireAsync(new(3, 100));
        fixture.QuestOwner = 100;
        await Assert.ThrowsAsync<InvalidDataException>(async () => await fixture.Controller.ObserveGameplayAsync());
        fixture.QuestOwner = 20;
        fixture.GameplayActor = 200;
        await Assert.ThrowsAsync<InvalidDataException>(async () => await fixture.Controller.ObserveGameplayAsync());
    }

    [Fact]
    public async Task DamageIsBoundedToBodyVitalsAndRoundTripsThroughGeneratedJson()
    {
        var fixture = new Fixture();
        await fixture.Controller.AcquireAsync(new(3, 100));
        int calls = fixture.Requests.Count;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await fixture.Controller.DamageVitalAsync((SkyrimVital)6, 10));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await fixture.Controller.DamageVitalAsync(SkyrimVital.Health, float.NaN));
        Assert.Equal(calls, fixture.Requests.Count);
        BodyGameplaySnapshot snapshot = await fixture.Controller.DamageVitalAsync(SkyrimVital.Health, 10);
        Assert.Equal(24U, fixture.Requests[^1].BodyActorValue);
        Assert.Equal(10, fixture.Requests[^1].BodyDamage);
        string json = JsonSerializer.Serialize(snapshot.Observation, MarionetteWireJsonContext.Default.BodyGameplayObservation);
        var restored = JsonSerializer.Deserialize(json, MarionetteWireJsonContext.Default.BodyGameplayObservation)!;
        Assert.Equal(snapshot.Observation.Stats, restored.Stats);
        Assert.Equal(snapshot.Observation.Inventory, restored.Inventory);
        Assert.Equal(20U, restored.QuestOwner);
    }

    private sealed class Fixture
    {
        private ulong sequence;
        private ulong lease;
        private uint actor;
        public List<BodyControlRequest> Requests { get; } = [];
        public BodyInventoryItem Item { get; set; } = new(15, "Gold", 11, false, false, 1);
        public string Failure { get; set; } = "";
        public uint QuestOwner { get; set; } = 20;
        public uint GameplayActor { get; set; }
        public SkyrimBodyControl Controller { get; }

        public Fixture()
        {
            Controller = new SkyrimBodyControl((request, _) =>
            {
                Requests.Add(request);
                if (request.BodyAction is "acquire" or "switch")
                {
                    actor = request.TargetFormId;
                    lease++;
                }
                var observation = new BodyControlObservation(lease, 3, actor, Phase: "active");
                var gameplay = new BodyGameplayObservation(GameplayActor == 0 ? actor : GameplayActor,
                    QuestOwner, 0, 0, 1385, 100, true, [new(SkyrimActorValue.Health, 242, 252, 252)], [Item]);
                return ValueTask.FromResult(new BodyControlResult(1, "body_control_result", request.RequestId,
                    ++sequence, Failure.Length == 0 ? "completed" : "failed", Failure, 3, observation, Gameplay: gameplay));
            });
        }
    }
}
