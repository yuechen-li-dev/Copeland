using System.Security.Cryptography;
using Dominatus.Core.Persistence;

namespace Marionette.Skyrim.App;

public sealed record WorldCheckpointRequest(int ProtocolVersion, string MessageKind, string RequestId,
    ulong CheckpointEpoch = 0, string CheckpointHex = "");
public sealed record WorldCheckpointResult(int ProtocolVersion, string MessageKind, string RequestId,
    ulong ServerSequence, string Status, ulong Epoch, string LoadStatus, string CheckpointHex);

public sealed partial class SkyrimCheckpointStore
{
    // SaveFile remains the canonical DOM1 encoder/decoder. Its file-only API is
    // contained here; no parallel in-memory binary codec is introduced.
    public string ExportEmbedded(SkyrimWorldOwnerRuntime owner)
    {
        Directory.CreateDirectory(directory);
        string scratch = Path.Combine(directory, "embedded-export.tmp");
        try
        {
            var checkpoint = DominatusCheckpointBuilder.Capture(owner.DominatusWorld);
            SaveFile.Write(scratch, DominatusSave.CreateCheckpointChunks(checkpoint));
            byte[] bytes = File.ReadAllBytes(scratch);
            if (bytes.Length > 24000) throw new InvalidDataException("embedded_checkpoint_size_limit");
            return Convert.ToHexString(bytes);
        }
        finally
        {
            File.Delete(scratch);
        }
    }

    public SkyrimWorldOwnerRuntime RestoreEmbedded(string hex, string sessionScope)
    {
        if (hex.Length is < 24 or > 48000 || hex.Length % 2 != 0)
            throw new InvalidDataException("embedded_checkpoint_size_invalid");
        byte[] bytes;
        try { bytes = Convert.FromHexString(hex); }
        catch (FormatException exception) { throw new InvalidDataException("embedded_checkpoint_hex_invalid", exception); }
        Directory.CreateDirectory(directory);
        string scratch = Path.Combine(directory, "embedded-restore.tmp");
        try
        {
            File.WriteAllBytes(scratch, bytes);
            var (checkpoint, _) = DominatusSave.ReadCheckpointChunks(SaveFile.Read(scratch));
            var restored = RestoreWorld(checkpoint, sessionScope);
            // Retain the exact selected co-save artifact for diagnostics. It is
            // addressed by bytes, never selected by save name or game timestamp.
            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            File.Move(scratch, Path.Combine(directory, hash + ".dom"), overwrite: true);
            return restored;
        }
        finally
        {
            File.Delete(scratch);
        }
    }

    private static SkyrimWorldOwnerRuntime RestoreWorld(DominatusCheckpoint checkpoint, string sessionScope)
    {
        var restored = new SkyrimWorldOwnerRuntime(sessionScope);
        foreach (AgentCheckpoint agentCheckpoint in checkpoint.Agents.Skip(1))
        {
            Dictionary<string, object> values = BbJsonCodec.DeserializeSnapshot(agentCheckpoint.BlackboardBlob);
            if (values.ContainsKey(SkyrimWorldCheckpointKeys.SemanticAgentId.Name))
                restored.AddRestoredImportedRuntimeAgent(ReadImportedAgent(values));
        }
        DominatusCheckpointBuilder.Restore(restored.DominatusWorld, checkpoint);
        restored.RestoreSemanticStateFromBlackboards();
        _ = restored.ReadOwnedAgentState();
        _ = restored.ReadPatrolTrial();
        return restored;
    }
}

public sealed partial class MarionetteTransportClient
{
    private static async ValueTask<WorldCheckpointResult> ExchangeCheckpointAsync(Stream pipe,
        WorldCheckpointRequest request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        await MarionetteWireProtocol.WriteAsync(pipe, request, timeout.Token).ConfigureAwait(false);
        var result = await MarionetteWireProtocol.ReadAsync<WorldCheckpointResult>(pipe, timeout.Token).ConfigureAwait(false);
        if (result.RequestId != request.RequestId || result.MessageKind != "world_checkpoint_result")
            throw new InvalidDataException("embedded_checkpoint_response_mismatch");
        return result;
    }
}
