using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aurelian.Marionette.Transport;

public static class MarionetteWireProtocol
{
    public const int Version = 1;
    public const int MaximumMessageBytes = 64 * 1024;

    public static byte[] Encode<T>(T message)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message, MarionetteWireJsonContext.Default.Options);
        if (payload.Length is 0 or > MaximumMessageBytes)
        {
            throw new InvalidDataException("frame_length_out_of_range");
        }

        byte[] frame = new byte[payload.Length + sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)payload.Length);
        payload.CopyTo(frame, sizeof(uint));
        return frame;
    }

    public static async ValueTask<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        byte[] lengthBytes = new byte[sizeof(uint)];
        await ReadExactlyAsync(stream, lengthBytes, cancellationToken).ConfigureAwait(false);
        uint payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(lengthBytes);
        if (payloadLength is 0 or > MaximumMessageBytes)
        {
            throw new InvalidDataException("frame_length_out_of_range");
        }

        byte[] payload = new byte[payloadLength];
        await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        try
        {
            T? result = JsonSerializer.Deserialize<T>(payload, MarionetteWireJsonContext.Default.Options);
            return result ?? throw new InvalidDataException("json_null");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("invalid_json", exception);
        }
    }

    public static async ValueTask WriteAsync<T>(Stream stream, T message, CancellationToken cancellationToken)
    {
        byte[] frame = Encode(message);
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("truncated_frame");
            }

            offset += read;
        }
    }
}

public sealed record LocalTransportConfig(string Profile, string Token, string ClientName, uint? FixtureTargetFormId = null)
{
    public string PipeName => $"MarionetteSSE.{Profile}.{GetCurrentUserSid()}.ed-m2b2";

    public static LocalTransportConfig Load(string path)
    {
        LocalTransportConfig? config = JsonSerializer.Deserialize(File.ReadAllBytes(path), MarionetteWireJsonContext.Default.LocalTransportConfig);
        if (config is null || string.IsNullOrWhiteSpace(config.Profile) || string.IsNullOrWhiteSpace(config.Token))
        {
            throw new InvalidDataException("transport_config_invalid");
        }

        return config;
    }

    private static string GetCurrentUserSid()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows named pipes require Windows.");
        }

        string? sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
        return string.IsNullOrWhiteSpace(sid) ? throw new InvalidOperationException("current_user_sid_unavailable") : sid;
    }
}

public sealed record ClientHello(int ProtocolVersion, string MessageKind, string Profile, string Nonce, string TokenProof, string ClientName);
public sealed record ServerHello(int ProtocolVersion, string MessageKind, bool Accepted, string? SessionId, string ServerName, string[] Capabilities, string? RejectionReason);
public sealed record TransportRequest(int ProtocolVersion, string MessageKind, string RequestId, long ClientSequence);
public sealed record TransportResult(int ProtocolVersion, string MessageKind, string RequestId, ulong ServerSequence, string Status);
public sealed record TransportStateResult(int ProtocolVersion, string MessageKind, string RequestId, ulong ServerSequence, bool BridgeReady, bool PresenterTransportEnabled, bool SemanticActuationEnabled, bool HostRequestEvaluationEnabled, bool ControllerConnected, string Profile, string SessionId, int MaxMessageBytes, string[] SupportedMessageKinds);
public sealed record SkyrimStateRequest(int ProtocolVersion, string MessageKind, string RequestId, int TimeoutMilliseconds);
public sealed record SkyrimStateResult(int ProtocolVersion, string MessageKind, string RequestId, ulong ServerSequence, string Status, string? Diagnostic, bool BridgeReady, ulong RuntimeSequence, bool PlayerAvailable, uint? PlayerFormId, uint? CrosshairTargetFormId, bool PendingRequestPresent, uint? PendingRequestGeneration, uint? PendingTargetFormId, bool ActiveHostSession, uint? ActiveHostGeneration, uint? ActiveHostFormId, uint? CameraTargetFormId);
public sealed record BeginHostSessionRequest(int ProtocolVersion, string MessageKind, string RequestId, uint ExpectedPendingRequestGeneration, uint ExpectedTargetFormId, int TimeoutMilliseconds);
public sealed record MoveHostKnownSpikeRequest(int ProtocolVersion, string MessageKind, string RequestId, uint ExpectedHostGeneration, uint Distance, string Direction, int TimeoutMilliseconds);
public sealed record RestoreHostSessionRequest(int ProtocolVersion, string MessageKind, string RequestId, uint ExpectedHostGeneration, int TimeoutMilliseconds);
public sealed record EmergencyRestoreRequest(int ProtocolVersion, string MessageKind, string RequestId, int TimeoutMilliseconds);
public sealed record EvaluateHostRequestRequest(int ProtocolVersion, string MessageKind, string RequestId, uint TargetFormId, int TimeoutMilliseconds);
public sealed record EvaluateHostRequestResult(int ProtocolVersion, string MessageKind, string RequestId, ulong ServerSequence, string Status, uint TargetFormId, bool Eligible, string EligibilityReason, bool PendingRequestPresent, uint? PendingRequestGeneration, uint? PendingTargetFormId, string RequestTransition, ulong RuntimeSequence, string? FailureReason);
public sealed record HostMutationResult(int ProtocolVersion, string MessageKind, string RequestId, ulong ServerSequence, string Status, string? FailureReason, bool OutcomeUncertain, ulong RuntimeSequence, uint HostGeneration, uint HostFormId, uint PlayerFormId, uint CameraTargetFormId, bool PlayerControlRestored, bool TargetPositionRestored, bool TargetAiRestored, bool TargetDeadRestored, bool SessionCleared, float[] HostPositionBefore, float[] HostPositionAfter, float[] PlayerPositionBefore, float[] PlayerPositionAfter);
public sealed record LoopbackReport(int ProtocolVersion, string Profile, bool Authenticated, string SessionId, bool PipeConnected, string PingRequestId, bool PingCompleted, string TransportStateRequestId, bool TransportStateCompleted, string SkyrimStateRequestId, bool SkyrimStateCompleted, bool BridgeReady, ulong RuntimeSequence, bool PlayerAvailable, uint? PlayerFormId, bool PendingRequestPresent, uint? PendingRequestGeneration, uint? PendingTargetFormId, bool ActiveHostSession, uint? ActiveHostGeneration, uint? ActiveHostFormId, uint? CameraTargetFormId, bool PresenterTransportEnabled, bool SemanticActuationEnabled, ulong ServerSequenceStart, ulong ServerSequenceEnd, bool GracefulDisconnect);
public sealed record KnownActuatorReport(int ProtocolVersion, string SessionId, bool Authenticated, uint TargetFormId, bool EvaluateAccepted, string EligibilityReason, bool InvalidTargetTested, string InvalidTargetReason, bool PendingCorrelationVerified, uint PendingRequestGeneration, uint PendingTargetFormId, bool SkyrimForegroundAtEvaluate, bool SkyrimForegroundAtBegin, bool SkyrimForegroundAtMove, bool SkyrimForegroundAtRestore, bool BeginAccepted, uint HostGeneration, bool MoveCompleted, float ObservedHostDistance, float ObservedPlayerDistance, bool RestoreCompleted, bool SessionCleared, ulong ServerSequenceStart, ulong ServerSequenceEnd);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LocalTransportConfig))]
[JsonSerializable(typeof(ClientHello))]
[JsonSerializable(typeof(ServerHello))]
[JsonSerializable(typeof(TransportRequest))]
[JsonSerializable(typeof(TransportResult))]
[JsonSerializable(typeof(TransportStateResult))]
[JsonSerializable(typeof(SkyrimStateRequest))]
[JsonSerializable(typeof(SkyrimStateResult))]
[JsonSerializable(typeof(BeginHostSessionRequest))]
[JsonSerializable(typeof(MoveHostKnownSpikeRequest))]
[JsonSerializable(typeof(RestoreHostSessionRequest))]
[JsonSerializable(typeof(EmergencyRestoreRequest))]
[JsonSerializable(typeof(EvaluateHostRequestRequest))]
[JsonSerializable(typeof(EvaluateHostRequestResult))]
[JsonSerializable(typeof(HostMutationResult))]
[JsonSerializable(typeof(LoopbackReport))]
[JsonSerializable(typeof(KnownActuatorReport))]
internal sealed partial class MarionetteWireJsonContext : JsonSerializerContext;

public sealed class MarionetteTransportClient
{
    private readonly LocalTransportConfig _config;
    private ulong _lastServerSequence;

    public MarionetteTransportClient(LocalTransportConfig config) => _config = config;

    public async ValueTask<LoopbackReport> RunLoopbackAsync(CancellationToken cancellationToken)
    {
        using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", _config.PipeName, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
        await pipe.ConnectAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        await MarionetteWireProtocol.WriteAsync(pipe, new ClientHello(MarionetteWireProtocol.Version, "client_hello", _config.Profile, Guid.NewGuid().ToString("N"), _config.Token, _config.ClientName), cancellationToken).ConfigureAwait(false);
        ServerHello hello = await MarionetteWireProtocol.ReadAsync<ServerHello>(pipe, cancellationToken).ConfigureAwait(false);
        if (!hello.Accepted || hello.ProtocolVersion != MarionetteWireProtocol.Version || string.IsNullOrWhiteSpace(hello.SessionId))
        {
            throw new InvalidDataException($"handshake_rejected:{hello.RejectionReason ?? "unknown"}");
        }

        string pingId = Guid.NewGuid().ToString("N");
        await MarionetteWireProtocol.WriteAsync(pipe, new TransportRequest(MarionetteWireProtocol.Version, "ping", pingId, 1), cancellationToken).ConfigureAwait(false);
        TransportResult ping = await MarionetteWireProtocol.ReadAsync<TransportResult>(pipe, cancellationToken).ConfigureAwait(false);
        ValidateResult(ping, "ping_result", pingId);

        string stateId = Guid.NewGuid().ToString("N");
        await MarionetteWireProtocol.WriteAsync(pipe, new TransportRequest(MarionetteWireProtocol.Version, "query_transport_state", stateId, 2), cancellationToken).ConfigureAwait(false);
        TransportStateResult state = await MarionetteWireProtocol.ReadAsync<TransportStateResult>(pipe, cancellationToken).ConfigureAwait(false);
        if (state.MessageKind != "transport_state_result" || state.RequestId != stateId || state.ServerSequence <= _lastServerSequence)
        {
            throw new InvalidDataException("transport_state_correlation_or_sequence_invalid");
        }
        _lastServerSequence = state.ServerSequence;

        string skyrimStateId = Guid.NewGuid().ToString("N");
        await MarionetteWireProtocol.WriteAsync(pipe, new SkyrimStateRequest(MarionetteWireProtocol.Version, "query_skyrim_state", skyrimStateId, 1000), cancellationToken).ConfigureAwait(false);
        SkyrimStateResult skyrimState = await MarionetteWireProtocol.ReadAsync<SkyrimStateResult>(pipe, cancellationToken).ConfigureAwait(false);
        if (skyrimState.MessageKind != "skyrim_state_result" || skyrimState.RequestId != skyrimStateId || skyrimState.Status != "completed" || skyrimState.ServerSequence <= _lastServerSequence || skyrimState.RuntimeSequence == 0)
        {
            throw new InvalidDataException($"skyrim_state_query_failed:{skyrimState.Diagnostic ?? "correlation_or_sequence_invalid"}");
        }
        _lastServerSequence = skyrimState.ServerSequence;

        string disconnectId = Guid.NewGuid().ToString("N");
        await MarionetteWireProtocol.WriteAsync(pipe, new TransportRequest(MarionetteWireProtocol.Version, "disconnect", disconnectId, 4), cancellationToken).ConfigureAwait(false);
        TransportResult disconnect = await MarionetteWireProtocol.ReadAsync<TransportResult>(pipe, cancellationToken).ConfigureAwait(false);
        ValidateResult(disconnect, "disconnect_result", disconnectId);

        return new LoopbackReport(MarionetteWireProtocol.Version, _config.Profile, true, hello.SessionId, true, pingId, true, stateId, true, skyrimStateId, true, skyrimState.BridgeReady, skyrimState.RuntimeSequence, skyrimState.PlayerAvailable, skyrimState.PlayerFormId, skyrimState.PendingRequestPresent, skyrimState.PendingRequestGeneration, skyrimState.PendingTargetFormId, skyrimState.ActiveHostSession, skyrimState.ActiveHostGeneration, skyrimState.ActiveHostFormId, skyrimState.CameraTargetFormId, state.PresenterTransportEnabled, state.SemanticActuationEnabled, ping.ServerSequence, disconnect.ServerSequence, true);
    }

    public async ValueTask<KnownActuatorReport> RunKnownActuatorScenarioAsync(CancellationToken cancellationToken)
    {
        using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", _config.PipeName, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
        await pipe.ConnectAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        await MarionetteWireProtocol.WriteAsync(pipe, new ClientHello(MarionetteWireProtocol.Version, "client_hello", _config.Profile, Guid.NewGuid().ToString("N"), _config.Token, _config.ClientName), cancellationToken).ConfigureAwait(false);
        ServerHello hello = await MarionetteWireProtocol.ReadAsync<ServerHello>(pipe, cancellationToken).ConfigureAwait(false);
        if (!hello.Accepted || string.IsNullOrWhiteSpace(hello.SessionId)) throw new InvalidDataException("handshake_rejected");
        string pingId = Guid.NewGuid().ToString("N");
        await MarionetteWireProtocol.WriteAsync(pipe, new TransportRequest(MarionetteWireProtocol.Version, "ping", pingId, 1), cancellationToken).ConfigureAwait(false);
        ValidateResult(await MarionetteWireProtocol.ReadAsync<TransportResult>(pipe, cancellationToken).ConfigureAwait(false), "ping_result", pingId);
        string transportId = Guid.NewGuid().ToString("N");
        await MarionetteWireProtocol.WriteAsync(pipe, new TransportRequest(MarionetteWireProtocol.Version, "query_transport_state", transportId, 2), cancellationToken).ConfigureAwait(false);
        TransportStateResult transport = await MarionetteWireProtocol.ReadAsync<TransportStateResult>(pipe, cancellationToken).ConfigureAwait(false);
        if (transport.MessageKind != "transport_state_result" || transport.RequestId != transportId || !transport.SemanticActuationEnabled) throw new InvalidDataException("semantic_actuation_not_enabled");
        if (!transport.HostRequestEvaluationEnabled || !transport.SupportedMessageKinds.Contains("evaluate_host_request", StringComparer.Ordinal)) throw new InvalidDataException("host_request_evaluation_not_enabled_or_supported");
        SkyrimStateResult initial = await QueryStateAsync(pipe, cancellationToken).ConfigureAwait(false);
        if (initial.PendingRequestPresent) throw new InvalidDataException("pending_request_already_present");
        uint targetFormId = _config.FixtureTargetFormId ?? initial.CrosshairTargetFormId ?? throw new InvalidDataException("fixture_target_form_id_missing");
        bool foregroundAtEvaluate = IsSkyrimForeground();
        EvaluateHostRequestResult evaluate = await SendEvaluateAsync(pipe, new EvaluateHostRequestRequest(MarionetteWireProtocol.Version, "evaluate_host_request", Guid.NewGuid().ToString("N"), targetFormId, 2000), cancellationToken).ConfigureAwait(false);
        if (evaluate.Status != "completed" || !evaluate.Eligible || !evaluate.PendingRequestPresent || !evaluate.PendingRequestGeneration.HasValue || evaluate.PendingRequestGeneration.Value == 0 || evaluate.PendingTargetFormId != targetFormId || evaluate.RequestTransition != "created") throw new InvalidDataException($"evaluate_host_request_failed:{evaluate.FailureReason ?? evaluate.EligibilityReason}");
        EvaluateHostRequestResult invalid = await SendEvaluateAsync(pipe, new EvaluateHostRequestRequest(MarionetteWireProtocol.Version, "evaluate_host_request", Guid.NewGuid().ToString("N"), 0x14, 2000), cancellationToken).ConfigureAwait(false);
        if (invalid.Status != "completed" || invalid.Eligible || invalid.EligibilityReason != "target_alive" || !invalid.PendingRequestPresent || invalid.PendingRequestGeneration != evaluate.PendingRequestGeneration || invalid.PendingTargetFormId != targetFormId || invalid.RequestTransition != "rejected") throw new InvalidDataException($"invalid_target_rejection_failed:{invalid.FailureReason ?? invalid.EligibilityReason}");
        SkyrimStateResult pending = await QueryStateAsync(pipe, cancellationToken).ConfigureAwait(false);
        if (!pending.PendingRequestPresent || pending.PendingRequestGeneration != evaluate.PendingRequestGeneration || pending.PendingTargetFormId != targetFormId) throw new InvalidDataException("pending_request_correlation_failed");
        bool foregroundAtBegin = IsSkyrimForeground();
        HostMutationResult begin = await SendMutationAsync(pipe, new BeginHostSessionRequest(MarionetteWireProtocol.Version, "begin_host_session", Guid.NewGuid().ToString("N"), pending.PendingRequestGeneration.Value, pending.PendingTargetFormId.Value, 2000), cancellationToken).ConfigureAwait(false);
        if (begin.Status != "completed" || begin.PlayerFormId != 0x14 || begin.HostFormId != pending.PendingTargetFormId.Value || begin.CameraTargetFormId != begin.HostFormId) throw new InvalidDataException($"begin_host_session_failed:{begin.FailureReason}");
        SkyrimStateResult active = await QueryStateAsync(pipe, cancellationToken).ConfigureAwait(false);
        if (!active.ActiveHostSession || active.ActiveHostGeneration != begin.HostGeneration || active.ActiveHostFormId != begin.HostFormId) throw new InvalidDataException("active_host_state_mismatch");
        bool foregroundAtMove = IsSkyrimForeground();
        HostMutationResult move = await SendMutationAsync(pipe, new MoveHostKnownSpikeRequest(MarionetteWireProtocol.Version, "move_host_known_spike", Guid.NewGuid().ToString("N"), begin.HostGeneration, 64, "positive_y", 2000), cancellationToken).ConfigureAwait(false);
        float hostDistance = Distance(move.HostPositionBefore, move.HostPositionAfter);
        float playerDistance = Distance(move.PlayerPositionBefore, move.PlayerPositionAfter);
        if (move.Status != "completed" || Math.Abs(hostDistance - 64.0F) > 2.0F || playerDistance > 2.0F || move.CameraTargetFormId != begin.HostFormId) throw new InvalidDataException($"known_spike_validation_failed:{move.FailureReason}");
        bool foregroundAtRestore = IsSkyrimForeground();
        HostMutationResult restore = await SendMutationAsync(pipe, new RestoreHostSessionRequest(MarionetteWireProtocol.Version, "restore_host_session", Guid.NewGuid().ToString("N"), begin.HostGeneration, 2000), cancellationToken).ConfigureAwait(false);
        if (restore.Status != "completed" || !restore.SessionCleared || restore.PlayerFormId != 0x14 || restore.CameraTargetFormId != 0x14) throw new InvalidDataException($"restore_failed:{restore.FailureReason}");
        return new KnownActuatorReport(MarionetteWireProtocol.Version, hello.SessionId, true, targetFormId, true, evaluate.EligibilityReason, true, invalid.EligibilityReason, true, pending.PendingRequestGeneration.Value, pending.PendingTargetFormId.Value, foregroundAtEvaluate, foregroundAtBegin, foregroundAtMove, foregroundAtRestore, true, begin.HostGeneration, true, hostDistance, playerDistance, true, true, evaluate.ServerSequence, restore.ServerSequence);
    }

    private async ValueTask<SkyrimStateResult> QueryStateAsync(Stream pipe, CancellationToken cancellationToken)
    {
        string id = Guid.NewGuid().ToString("N");
        await MarionetteWireProtocol.WriteAsync(pipe, new SkyrimStateRequest(MarionetteWireProtocol.Version, "query_skyrim_state", id, 2000), cancellationToken).ConfigureAwait(false);
        SkyrimStateResult result = await MarionetteWireProtocol.ReadAsync<SkyrimStateResult>(pipe, cancellationToken).ConfigureAwait(false);
        if (result.Status != "completed" || result.RequestId != id) throw new InvalidDataException("skyrim_state_query_failed");
        return result;
    }

    private static async ValueTask<HostMutationResult> SendMutationAsync<T>(Stream pipe, T request, CancellationToken cancellationToken)
    {
        await MarionetteWireProtocol.WriteAsync(pipe, request, cancellationToken).ConfigureAwait(false);
        return await MarionetteWireProtocol.ReadAsync<HostMutationResult>(pipe, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<EvaluateHostRequestResult> SendEvaluateAsync(Stream pipe, EvaluateHostRequestRequest request, CancellationToken cancellationToken)
    {
        await MarionetteWireProtocol.WriteAsync(pipe, request, cancellationToken).ConfigureAwait(false);
        EvaluateHostRequestResult result = await MarionetteWireProtocol.ReadAsync<EvaluateHostRequestResult>(pipe, cancellationToken).ConfigureAwait(false);
        if (result.MessageKind != "evaluate_host_request_result" || result.RequestId != request.RequestId || result.TargetFormId != request.TargetFormId)
        {
            throw new InvalidDataException("evaluate_host_request_correlation_invalid");
        }

        return result;
    }

    private static float Distance(float[] before, float[] after)
    {
        if (before.Length != 3 || after.Length != 3) throw new InvalidDataException("position_shape_invalid");
        float x = after[0] - before[0]; float y = after[1] - before[1]; float z = after[2] - before[2];
        return MathF.Sqrt(x * x + y * y + z * z);
    }

    private static bool IsSkyrimForeground()
    {
        nint window = GetForegroundWindow();
        _ = GetWindowThreadProcessId(window, out uint processId);
        try { return processId != 0 && System.Diagnostics.Process.GetProcessById((int)processId).ProcessName.StartsWith("SkyrimSE", StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    private void ValidateResult(TransportResult result, string messageKind, string requestId)
    {
        if (result.MessageKind != messageKind || result.RequestId != requestId || result.Status != "completed" || result.ServerSequence <= _lastServerSequence)
        {
            throw new InvalidDataException("request_correlation_or_sequence_invalid");
        }

        _lastServerSequence = result.ServerSequence;
    }
}
