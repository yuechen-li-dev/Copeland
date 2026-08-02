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

public sealed record LocalTransportConfig(string Profile, string Token, string ClientName)
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
public sealed record TransportStateResult(int ProtocolVersion, string MessageKind, string RequestId, ulong ServerSequence, bool BridgeReady, bool PresenterTransportEnabled, bool SemanticActuationEnabled, bool ControllerConnected, string Profile, string SessionId, int MaxMessageBytes, string[] SupportedMessageKinds);
public sealed record LoopbackReport(int ProtocolVersion, string Profile, bool Authenticated, string SessionId, bool PipeConnected, string PingRequestId, bool PingCompleted, string TransportStateRequestId, bool TransportStateCompleted, bool BridgeReady, bool PresenterTransportEnabled, bool SemanticActuationEnabled, ulong ServerSequenceStart, ulong ServerSequenceEnd, bool GracefulDisconnect);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LocalTransportConfig))]
[JsonSerializable(typeof(ClientHello))]
[JsonSerializable(typeof(ServerHello))]
[JsonSerializable(typeof(TransportRequest))]
[JsonSerializable(typeof(TransportResult))]
[JsonSerializable(typeof(TransportStateResult))]
[JsonSerializable(typeof(LoopbackReport))]
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

        string disconnectId = Guid.NewGuid().ToString("N");
        await MarionetteWireProtocol.WriteAsync(pipe, new TransportRequest(MarionetteWireProtocol.Version, "disconnect", disconnectId, 3), cancellationToken).ConfigureAwait(false);
        TransportResult disconnect = await MarionetteWireProtocol.ReadAsync<TransportResult>(pipe, cancellationToken).ConfigureAwait(false);
        ValidateResult(disconnect, "disconnect_result", disconnectId);

        return new LoopbackReport(MarionetteWireProtocol.Version, _config.Profile, true, hello.SessionId, true, pingId, true, stateId, true, state.BridgeReady, state.PresenterTransportEnabled, state.SemanticActuationEnabled, ping.ServerSequence, disconnect.ServerSequence, true);
    }

    private void ValidateResult(TransportResult result, string messageKind, string requestId)
    {
        if (result.MessageKind != messageKind || result.RequestId != requestId || result.Status != "completed" || result.ServerSequence <= _lastServerSequence)
        {
            throw new InvalidDataException("request_correlation_or_sequence_invalid");
        }

        _lastServerSequence = result.ServerSequence;
    }
}
