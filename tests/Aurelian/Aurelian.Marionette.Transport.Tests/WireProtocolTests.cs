using System.Buffers.Binary;
using Aurelian.Actuation.Host;
using Xunit;

namespace Aurelian.Marionette.Transport.Tests;

public sealed class WireProtocolTests
{
    [Fact]
    public async Task Frame_RoundTrips()
    {
        var request = new TransportRequest(MarionetteWireProtocol.Version, "ping", "request-1", 1);
        using var stream = new MemoryStream(MarionetteWireProtocol.Encode(request));
        TransportRequest result = await MarionetteWireProtocol.ReadAsync<TransportRequest>(stream, CancellationToken.None);
        Assert.Equal(request, result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65537)]
    public async Task Frame_RejectsInvalidLengths(uint length)
    {
        byte[] frame = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, length);
        using var stream = new MemoryStream(frame);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await MarionetteWireProtocol.ReadAsync<TransportRequest>(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Frame_RejectsTruncatedPayload()
    {
        byte[] frame = new byte[6];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, 8);
        using var stream = new MemoryStream(frame);
        await Assert.ThrowsAsync<EndOfStreamException>(async () => await MarionetteWireProtocol.ReadAsync<TransportRequest>(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Frame_RejectsMalformedJson()
    {
        byte[] payload = "{not-json}"u8.ToArray();
        byte[] frame = new byte[payload.Length + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)payload.Length);
        payload.CopyTo(frame, 4);
        using var stream = new MemoryStream(frame);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await MarionetteWireProtocol.ReadAsync<TransportRequest>(stream, CancellationToken.None));
    }

    [Fact]
    public async Task SkyrimStateRequest_RoundTrips()
    {
        var request = new SkyrimStateRequest(MarionetteWireProtocol.Version, "query_skyrim_state", "state-1", 250);
        using var stream = new MemoryStream(MarionetteWireProtocol.Encode(request));
        SkyrimStateRequest result = await MarionetteWireProtocol.ReadAsync<SkyrimStateRequest>(stream, CancellationToken.None);
        Assert.Equal(request, result);
    }

    [Fact]
    public async Task EligibleHostFixtureMessages_RoundTripWithValueOnlyCandidate()
    {
        var request = new EligibleHostFixturesRequest(MarionetteWireProtocol.Version, "query_eligible_host_fixtures", "hosts-1", 1024, 8, 250);
        using var requestStream = new MemoryStream(MarionetteWireProtocol.Encode(request));
        EligibleHostFixturesRequest decodedRequest = await MarionetteWireProtocol.ReadAsync<EligibleHostFixturesRequest>(requestStream, CancellationToken.None);
        Assert.Equal(request, decodedRequest);

        var candidate = new EligibleHostFixtureCandidate(0x1234, 0x5678, 42.0F, true, true, false, false, true, "eligible", true, "4660", 1, 2, 3);
        var response = new EligibleHostFixturesResult(1, "eligible_host_fixtures_result", "hosts-1", 4, "completed", 12, 0x14, 3, 1, [candidate], null);
        using var responseStream = new MemoryStream(MarionetteWireProtocol.Encode(response));
        EligibleHostFixturesResult decodedResponse = await MarionetteWireProtocol.ReadAsync<EligibleHostFixturesResult>(responseStream, CancellationToken.None);
        Assert.Equal((uint)0x1234, decodedResponse.Candidates[0].FormId);
        Assert.DoesNotContain("name", System.Text.Encoding.UTF8.GetString(MarionetteWireProtocol.Encode(response)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EligibleHostCandidates_RequireDistanceThenFormIdOrdering()
    {
        var ordered = new[] {
            new EligibleHostFixtureCandidate(10, null, 10, true, true, false, false, true, "eligible", true, "10", 1, 2, 3),
            new EligibleHostFixtureCandidate(20, null, 10, true, true, false, false, true, "eligible", true, "20", 4, 5, 6)
        };
        Assert.True(MarionetteTransportClient.IsDeterministicOrder(ordered));
        Assert.False(MarionetteTransportClient.IsDeterministicOrder(ordered.Reverse().ToArray()));
    }

    [Fact]
    public async Task SkyrimStateResult_PreservesPartialSnapshot()
    {
        var response = new SkyrimStateResult(1, "skyrim_state_result", "state-1", 3, "completed", null, true, 7, true, 0x14, 0x1234, false, null, null, false, null, null, null);
        using var stream = new MemoryStream(MarionetteWireProtocol.Encode(response));
        SkyrimStateResult result = await MarionetteWireProtocol.ReadAsync<SkyrimStateResult>(stream, CancellationToken.None);
        Assert.Equal((uint)0x14, result.PlayerFormId);
        Assert.Equal((ulong)7, result.RuntimeSequence);
        Assert.Equal((uint)0x1234, result.CrosshairTargetFormId);
        Assert.Null(result.PendingRequestGeneration);
        Assert.Null(result.CameraTargetFormId);
    }

    [Fact]
    public async Task SkyrimStateResult_PreservesFailureDiagnostic()
    {
        var response = new SkyrimStateResult(1, "skyrim_state_result", "state-1", 3, "failed", "dispatch_timeout", false, 0, false, null, null, false, null, null, false, null, null, null);
        using var stream = new MemoryStream(MarionetteWireProtocol.Encode(response));
        SkyrimStateResult result = await MarionetteWireProtocol.ReadAsync<SkyrimStateResult>(stream, CancellationToken.None);
        Assert.Equal("failed", result.Status);
        Assert.Equal("dispatch_timeout", result.Diagnostic);
    }

    [Fact]
    public async Task KnownActuatorMessages_RoundTripWithBoundedShape()
    {
        var request = new MoveHostKnownSpikeRequest(MarionetteWireProtocol.Version, "move_host_known_spike", "move-1", 7, 64, "positive_y", 250);
        using var stream = new MemoryStream(MarionetteWireProtocol.Encode(request));
        MoveHostKnownSpikeRequest result = await MarionetteWireProtocol.ReadAsync<MoveHostKnownSpikeRequest>(stream, CancellationToken.None);
        Assert.Equal(request, result);
    }

    [Fact]
    public async Task MoveTowardMessage_RoundTripsAsSemanticValues()
    {
        var request = new MoveTowardRequest(1, "move_toward", "move-2", 7, 0x1234, 11, 10, 20, 30, 16, 64, "walk", 2000);
        using var stream = new MemoryStream(MarionetteWireProtocol.Encode(request));
        MoveTowardRequest result = await MarionetteWireProtocol.ReadAsync<MoveTowardRequest>(stream, CancellationToken.None);
        Assert.Equal(request, result);
        Assert.DoesNotContain("pointer", System.Text.Encoding.UTF8.GetString(MarionetteWireProtocol.Encode(request)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActorSnapshot_MapsUnsupportedFieldsHonestlyAndGatesExperimentalMove()
    {
        var state = new SkyrimStateResult(
            1, "skyrim_state_result", "state-2", 12, "completed", null, true, 11,
            true, 0x14, null, false, null, null, true, 7, 0x1234, 0x1234,
            true, 7, 0x1234, true, false, false, 10, 20, 30, 1.5f, 0x18,
            null, null, null, "supported", "unsupported", "experimental", "supported",
            "unsupported", "unsupported", "unsupported", "unsupported");

        HostActorObservation actor = MarionetteTransportClient.ToActorObservation(state);

        Assert.Equal(new HostActorId(0x1234, 7), actor.ActorId);
        Assert.Null(actor.Velocity);
        Assert.True(actor.Capabilities.CanMoveToward);
        Assert.Equal(HostCapabilitySupport.Unsupported, actor.Capabilities.AnimatedLocomotion);
        Assert.Equal((ulong)11, actor.Sequence);
    }

    [Fact]
    public async Task HostMutationResult_PreservesRestorationEvidence()
    {
        var response = new HostMutationResult(1, "restore_host_session_result", "restore-1", 8, "completed", null, false, 3, 7, 20, 0x14, 0x14, true, true, true, true, true, [0, 0, 0], [0, 0, 0], [0, 0, 0], [0, 0, 0]);
        using var stream = new MemoryStream(MarionetteWireProtocol.Encode(response));
        HostMutationResult result = await MarionetteWireProtocol.ReadAsync<HostMutationResult>(stream, CancellationToken.None);
        Assert.True(result.SessionCleared);
        Assert.Equal((uint)0x14, result.CameraTargetFormId);
    }

    [Fact]
    public async Task EvaluateHostRequestMessages_RoundTripWithEligibilityEvidence()
    {
        var request = new EvaluateHostRequestRequest(MarionetteWireProtocol.Version, "evaluate_host_request", "evaluate-1", 0x1234, 250);
        using var requestStream = new MemoryStream(MarionetteWireProtocol.Encode(request));
        EvaluateHostRequestRequest decodedRequest = await MarionetteWireProtocol.ReadAsync<EvaluateHostRequestRequest>(requestStream, CancellationToken.None);
        Assert.Equal(request, decodedRequest);

        var response = new EvaluateHostRequestResult(1, "evaluate_host_request_result", "evaluate-1", 9, "completed", 0x1234, true, "eligible", true, 4, 0x1234, "created", 8, null);
        using var responseStream = new MemoryStream(MarionetteWireProtocol.Encode(response));
        EvaluateHostRequestResult decodedResponse = await MarionetteWireProtocol.ReadAsync<EvaluateHostRequestResult>(responseStream, CancellationToken.None);
        Assert.True(decodedResponse.Eligible);
        Assert.Equal((uint)4, decodedResponse.PendingRequestGeneration);
    }

    [Fact]
    public async Task SessionBootstrapMessages_RoundTripWithoutSaveFilename()
    {
        var request = new LoadDevelopmentSessionRequest(MarionetteWireProtocol.Version, "load_development_session", "load-1", "ed-m2b2d", 5000);
        using var requestStream = new MemoryStream(MarionetteWireProtocol.Encode(request));
        LoadDevelopmentSessionRequest roundTrippedRequest = await MarionetteWireProtocol.ReadAsync<LoadDevelopmentSessionRequest>(requestStream, CancellationToken.None);
        Assert.Equal("ed-m2b2d", roundTrippedRequest.SaveId);

        var response = new SessionLoadResult(1, "session_load_state_result", "state-1", 9, "completed", "ed-m2b2d", 2, "ready", true, 0x14, true, 7, null);
        using var responseStream = new MemoryStream(MarionetteWireProtocol.Encode(response));
        SessionLoadResult roundTrippedResponse = await MarionetteWireProtocol.ReadAsync<SessionLoadResult>(responseStream, CancellationToken.None);
        Assert.Equal((uint)0x14, roundTrippedResponse.PlayerFormId);
        Assert.True(roundTrippedResponse.WorldReady);
        Assert.DoesNotContain(".ess", System.Text.Encoding.UTF8.GetString(MarionetteWireProtocol.Encode(response)));
    }
}
