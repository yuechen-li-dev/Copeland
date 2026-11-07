using System.Text;
using Marionette.Skyrim.App;
using Xunit;

namespace Marionette.Skyrim.App.Tests;

public sealed class SkyrimBodyControlTests
{
    [Fact]
    public async Task AnUnavailableWorldSnapshotDoesNotRevokeOwnership()
    {
        ulong sequence = 0;
        BodyControlRequest? lastRequest = null;
        var controller = new SkyrimBodyControl((request, _) =>
        {
            lastRequest = request;
            return ValueTask.FromResult(new BodyControlResult(1, "body_control_result", request.RequestId,
                ++sequence, "completed", "", 3, new BodyControlObservation(10, 3, 100, Phase: "active")));
        });
        await controller.AcquireAsync(new(3, 100));
        controller.ObserveWorld(0);
        Assert.Equal(new SkyrimBodyLease(3, 10, 100), controller.Lease);
        await controller.HeartbeatAsync();
        Assert.Equal(3UL, lastRequest!.ExpectedWorldGeneration);
        Assert.Equal(10UL, lastRequest.ExpectedBodyLeaseId);
        controller.ObserveWorld(4);
        Assert.Null(controller.Lease);
    }

    [Theory]
    [InlineData("dispatch_timeout")]
    [InlineData("dispatch_unavailable")]
    [InlineData("runtime_unavailable")]
    [InlineData("queue_full")]
    public async Task DispatcherRefusalsPreserveOwnershipAndClearHeldInput(string reason)
    {
        ulong sequence = 0;
        var requests = new List<BodyControlRequest>();
        var controller = new SkyrimBodyControl((request, _) =>
        {
            requests.Add(request);
            bool deferred = sequence == 2;
            return ValueTask.FromResult(new BodyControlResult(1, "body_control_result", request.RequestId,
                ++sequence, deferred ? "failed" : "completed", deferred ? reason : "", deferred ? 0UL : 3UL,
                deferred ? new BodyControlObservation() : new BodyControlObservation(10, 3, 100, Phase: "active")));
        });
        await controller.AcquireAsync(new(3, 100));
        await controller.UpdateAsync(BodyMovement.Forward);
        SkyrimBodyLease lease = controller.Lease!;
        var failure = await Assert.ThrowsAsync<SkyrimBodyControlException>(async () => await controller.HeartbeatAsync());
        Assert.True(failure.IsDeferred);
        Assert.Equal(lease, controller.Lease);
        Assert.Equal("suspended", controller.Observation.Phase);
        await controller.HeartbeatAsync();
        Assert.Equal(lease, controller.Lease);
        Assert.Equal(10UL, requests[^1].ExpectedBodyLeaseId);
        Assert.Equal(0U, requests[^1].InputButtons);
        Assert.Equal("active", controller.Observation.Phase);
    }

    [Fact]
    public async Task RefusedSwitchPreservesLeaseAndSuccessfulSwitchInvalidatesPreviousToken()
    {
        ulong sequence = 0;
        ulong nativeLease = 10;
        uint actor = 100;
        var requests = new List<BodyControlRequest>();
        var controller = new SkyrimBodyControl((request, _) =>
        {
            requests.Add(request);
            bool rejected = request.TargetFormId == 999;
            if (request.BodyAction == "switch" && !rejected)
            {
                nativeLease++;
                actor = request.TargetFormId;
            }
            var observation = new BodyControlObservation(nativeLease, 3, actor, Phase: "active", Loaded: true);
            return ValueTask.FromResult(new BodyControlResult(1, "body_control_result", request.RequestId,
                ++sequence, rejected ? "failed" : "completed", rejected ? "body_actor_ineligible" : "", 3, observation));
        });
        await controller.AcquireAsync(new(3, 100));
        SkyrimBodyLease before = controller.Lease!;
        await Assert.ThrowsAsync<SkyrimBodyControlException>(async () => await controller.SwitchAsync(new(3, 999)));
        Assert.Equal(before, controller.Lease);
        await controller.SwitchAsync(new(3, 200));
        Assert.Equal(new SkyrimBodyLease(3, 11, 200), controller.Lease);
        await controller.HeartbeatAsync();
        Assert.Equal(10UL, requests[2].ExpectedBodyLeaseId);
        Assert.Equal(11UL, requests[3].ExpectedBodyLeaseId);
    }

    [Fact]
    public async Task ObservingAnotherOwnerDoesNotAcquireItsLease()
    {
        var controller = new SkyrimBodyControl((request, _) => ValueTask.FromResult(
            new BodyControlResult(1, "body_control_result", request.RequestId, 1, "completed", "", 3,
                new BodyControlObservation(42, 3, 100, Phase: "active"))));
        await controller.ObserveAsync(3);
        Assert.Null(controller.Lease);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await controller.ActAsync(SkyrimBodyAction.Melee));
    }

    [Fact]
    public async Task WorldTransitionAndNativeExpiryRevokeManagedOwnership()
    {
        ulong sequence = 0;
        var controller = new SkyrimBodyControl((request, _) =>
        {
            bool acquiring = request.BodyAction == "acquire";
            return ValueTask.FromResult(new BodyControlResult(1, "body_control_result", request.RequestId,
                ++sequence, acquiring ? "completed" : "failed", acquiring ? "" : "body_lease_mismatch", 3,
                acquiring ? new BodyControlObservation(10, 3, 100, Phase: "active") : new(Reason: "heartbeat_expired")));
        });
        await controller.AcquireAsync(new(3, 100));
        await Assert.ThrowsAsync<SkyrimBodyControlException>(async () => await controller.HeartbeatAsync());
        Assert.Null(controller.Lease);
        await controller.AcquireAsync(new(3, 100));
        controller.ObserveWorld(4);
        Assert.Null(controller.Lease);
        Assert.Equal("world_generation_changed", controller.Observation.Reason);
    }

    [Fact]
    public async Task SuspendedInputDoesNotRemainHeldAfterResume()
    {
        ulong sequence = 0;
        var requests = new List<BodyControlRequest>();
        var controller = new SkyrimBodyControl((request, _) =>
        {
            requests.Add(request);
            string phase = request.BodyAction == "renew" ? "suspended" : "active";
            return ValueTask.FromResult(new BodyControlResult(1, "body_control_result", request.RequestId,
                ++sequence, "completed", "", 3, new BodyControlObservation(10, 3, 100, Phase: phase)));
        });
        await controller.AcquireAsync(new(3, 100));
        await controller.UpdateAsync(BodyMovement.Forward, 12, -5);
        await controller.HeartbeatAsync();
        Assert.Equal(0U, requests[^1].InputButtons);
        Assert.Equal(0, requests[^1].InputMouseX);
        Assert.Equal(0, requests[^1].InputMouseY);
    }

    [Fact]
    public void BodyCommandsUseGeneratedWireContracts()
    {
        byte[] frame = MarionetteWireProtocol.Encode(new BodyControlRequest(1, "body_control", "body-1",
            2000, 3, 10, 100, "switch"));
        string json = Encoding.UTF8.GetString(frame.AsSpan(4));
        Assert.Contains("\"expectedBodyLeaseId\":10", json);
        Assert.Contains("\"bodyAction\":\"switch\"", json);
        Assert.Contains("\"targetFormId\":100", json);
    }
}
