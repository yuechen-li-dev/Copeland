using System.Text.Json;
using Deliverance.Core;
using Deliverance.Core.Codecs;
using Deliverance.Core.Encryption;
using Deliverance.Core.Modules;
using Deliverance.Core.Serialization;
using Deliverance.Core.Storage;
using Deliverance.Dominatus;

namespace TinyFarm.Core;

public sealed record TinyFarmSemanticSaveSnapshot(
    int ApplicationSaveVersion,
    string RuntimeVersion,
    string DefinitionHash,
    TinyFarmState World,
    long NextSequence,
    IReadOnlyList<GameEvent> RecentEvents,
    TinyFarmDialogueCheckpoint? Dialogue = null);

public sealed record TinyFarmSemanticSaveSnapshotV1(
    string RuntimeVersion,
    string DefinitionHash,
    TinyFarmState World,
    long NextSequence,
    IReadOnlyList<GameEvent> RecentEvents);

public sealed class TinyFarmDeliverancePersistence : IPersistenceApplicationBridge
{
    public const string ApplicationId = "tiny-farm";
    public const string ApplicationVersion = "m6";
    public const string ModuleId = "tinyfarm.semantic-state";
    public const int ModuleSchemaVersion = 2;

    private readonly TinyFarmSimulationHost host;
    private readonly TinyFarmDefinitions definitions;
    private readonly TinyFarmDialogueCoordinator? dialogue;

    public DeliveranceService Deliverance { get; }
    public DeliverancePersistenceActuator Actuator { get; }

    public TinyFarmDeliverancePersistence(
        TinyFarmSimulationHost host,
        TinyFarmDefinitions definitions,
        ISaveStore store,
        IEncryptionCodec? encryption = null,
        IEncryptionKeyProvider? keyProvider = null,
        TinyFarmDialogueCoordinator? dialogue = null)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        this.definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        this.dialogue = dialogue;
        var serializer = new MessagePackSaveSerializer();
        Deliverance = new DeliveranceService(new DeliveranceOptions
        {
            Store = store ?? throw new ArgumentNullException(nameof(store)),
            Serializer = serializer,
            DefaultCompression = new GzipCodec(),
            DefaultEncryption = encryption,
            EncryptionKeyProvider = keyProvider,
        });
        Actuator = new DeliverancePersistenceActuator(Deliverance, this);
    }

    public SaveRequest CaptureSave(string slotId)
    {
        TinyFarmSemanticSaveSnapshot snapshot = CaptureSnapshot();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, TinyFarmChunkedSaveCodec.ChunkOptions);
        var module = new SaveModulePayload(
            ModuleId,
            ModuleSchemaVersion,
            ModuleCriticality.Required,
            SerializerId: 0,
            CompressionId: 1,
            bytes);
        return new SaveRequest(
            new SaveApplicationMetadata(
                ApplicationId,
                ApplicationVersion,
                DefinitionHash: definitions.Identity,
                CadenceConfigHash: host.CadenceConfigurationIdentity,
                ApplicationSaveVersion: 1),
            [module]);
    }

    public IReadOnlyList<SaveModuleDefinition> GetLoadDefinitions(string slotId)
    {
        return
        [
            new SaveModuleDefinition(
                ModuleId,
                ModuleSchemaVersion,
                ModuleCriticality.Required,
                migrations:
                [
                    new ModuleMigration(1, MigrateV1ToV2),
                ],
                validateCurrentPayload: bytes => _ = DecodeAndValidate(bytes))
        ];
    }

    public LoadCompatibility GetLoadCompatibility(string slotId)
    {
        return new LoadCompatibility(
            ApplicationId,
            definitions.Identity,
            host.CadenceConfigurationIdentity,
            RequireCadenceMatch: true,
            ApplicationSaveVersion: 1);
    }

    public void CommitLoadedCandidate(string slotId, LoadedSaveCandidate candidate)
    {
        TinyFarmSemanticSaveSnapshot snapshot = DecodeAndValidate(candidate.GetModule(ModuleId).Payload);
        var session = new TinyFarmSession(
            snapshot.World,
            definitions,
            snapshot.NextSequence,
            snapshot.RecentEvents);
        host.CommitLoadedSession(session);
        if (dialogue is not null && snapshot.Dialogue is not null)
        {
            dialogue.Restore(snapshot.Dialogue);
        }
    }

    public TinyFarmSemanticSaveSnapshot CaptureSnapshot()
    {
        TinyFarmSession session = host.Session;
        TinyFarmChunkedSaveCodec.ValidateWorld(session.State, definitions);
        return new TinyFarmSemanticSaveSnapshot(
            ApplicationSaveVersion: 1,
            TinyFarmChunkedSaveCodec.RuntimeVersionFor(session.State.Version),
            definitions.Identity,
            session.State.DeepCopy(),
            session.NextSequence,
            session.RecentEvents.ToArray(),
            dialogue?.Capture());
    }

    private TinyFarmSemanticSaveSnapshot DecodeAndValidate(ReadOnlyMemory<byte> bytes)
    {
        TinyFarmSemanticSaveSnapshot snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<TinyFarmSemanticSaveSnapshot>(
                bytes.Span,
                TinyFarmChunkedSaveCodec.ChunkOptions)
                ?? throw new InvalidDataException("TinyFarm semantic save module was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("TinyFarm semantic save module contains malformed JSON.", exception);
        }

        if (snapshot.ApplicationSaveVersion != 1)
        {
            throw new InvalidDataException($"Unsupported TinyFarm application save version '{snapshot.ApplicationSaveVersion}'.");
        }
        string expectedRuntime = TinyFarmChunkedSaveCodec.RuntimeVersionFor(snapshot.World.Version);
        if (!string.Equals(snapshot.RuntimeVersion, expectedRuntime, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"TinyFarm runtime version mismatch: save '{snapshot.RuntimeVersion}', expected '{expectedRuntime}'.");
        }
        if (!string.Equals(snapshot.DefinitionHash, definitions.Identity, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"TinyFarm definition mismatch: save '{snapshot.DefinitionHash}', runtime '{definitions.Identity}'.");
        }
        TinyFarmChunkedSaveCodec.ValidateWorld(snapshot.World, definitions);
        return snapshot;
    }

    private static ReadOnlyMemory<byte> MigrateV1ToV2(ReadOnlyMemory<byte> bytes)
    {
        TinyFarmSemanticSaveSnapshotV1 legacy;
        try
        {
            legacy = JsonSerializer.Deserialize<TinyFarmSemanticSaveSnapshotV1>(
                bytes.Span,
                TinyFarmChunkedSaveCodec.ChunkOptions)
                ?? throw new InvalidDataException("TinyFarm schema v1 save module was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("TinyFarm schema v1 save module contains malformed JSON.", exception);
        }

        var current = new TinyFarmSemanticSaveSnapshot(
            ApplicationSaveVersion: 1,
            legacy.RuntimeVersion,
            legacy.DefinitionHash,
            legacy.World,
            legacy.NextSequence,
            legacy.RecentEvents,
            null);
        return JsonSerializer.SerializeToUtf8Bytes(current, TinyFarmChunkedSaveCodec.ChunkOptions);
    }
}

public sealed record TinyFarmReplayRecord(
    int Index,
    IntentEnvelope Intent,
    string ExpectedStateHash);

public sealed record TinyFarmReplayEnvelope(
    int ReplayFormatVersion,
    string ApplicationId,
    string DefinitionHash,
    string CadenceConfigHash,
    TinyFarmState InitialCheckpoint,
    string InitialCheckpointHash,
    IReadOnlyList<TinyFarmReplayRecord> Intents);

public sealed record TinyFarmReplayResult(TinyFarmState State, string FinalHash, int AppliedIntentCount);

public static class TinyFarmSemanticReplay
{
    public const int CurrentReplayFormatVersion = 1;

    public static byte[] Serialize(TinyFarmReplayEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return JsonSerializer.SerializeToUtf8Bytes(envelope, TinyFarmChunkedSaveCodec.ChunkOptions);
    }

    public static TinyFarmReplayEnvelope Deserialize(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<TinyFarmReplayEnvelope>(bytes, TinyFarmChunkedSaveCodec.ChunkOptions)
                ?? throw new InvalidDataException("TinyFarm replay envelope was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "TinyFarm replay intent schema mismatch or malformed replay envelope.",
                exception);
        }
    }

    public static TinyFarmReplayEnvelope Create(
        TinyFarmState initialCheckpoint,
        string definitionHash,
        string cadenceConfigHash,
        IReadOnlyList<TinyFarmReplayRecord> intents)
    {
        ArgumentNullException.ThrowIfNull(initialCheckpoint);
        return new TinyFarmReplayEnvelope(
            CurrentReplayFormatVersion,
            TinyFarmDeliverancePersistence.ApplicationId,
            definitionHash,
            cadenceConfigHash,
            initialCheckpoint.DeepCopy(),
            TinyFarmSemanticHash.Compute(initialCheckpoint),
            intents);
    }

    public static TinyFarmReplayResult Replay(
        TinyFarmReplayEnvelope envelope,
        TinyFarmDefinitions definitions,
        string cadenceConfigHash)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(definitions);
        if (envelope.ReplayFormatVersion != CurrentReplayFormatVersion)
        {
            throw new InvalidDataException($"Unsupported TinyFarm replay format '{envelope.ReplayFormatVersion}'.");
        }
        if (!string.Equals(envelope.ApplicationId, TinyFarmDeliverancePersistence.ApplicationId, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Replay application mismatch at sequence 0.");
        }
        if (!string.Equals(envelope.DefinitionHash, definitions.Identity, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Replay definition hash mismatch at sequence 0.");
        }
        if (!string.Equals(envelope.CadenceConfigHash, cadenceConfigHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Replay cadence hash mismatch at sequence 0.");
        }

        string initialHash = TinyFarmSemanticHash.Compute(envelope.InitialCheckpoint);
        if (!string.Equals(initialHash, envelope.InitialCheckpointHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Replay checkpoint hash mismatch at sequence 0.");
        }

        TinyFarmState state = envelope.InitialCheckpoint.DeepCopy();
        var resolver = new TinyFarmResolver(definitions);
        long previousSequence = long.MinValue;
        for (int index = 0; index < envelope.Intents.Count; index++)
        {
            TinyFarmReplayRecord record = envelope.Intents[index];
            if (record.Index != index || record.Intent.Sequence <= previousSequence)
            {
                throw new InvalidDataException($"Replay intent ordering mismatch at index {index}.");
            }
            previousSequence = record.Intent.Sequence;
            IntentEnvelope replayIntent = record.Intent with { Source = IntentSourceKind.Replay };
            state = resolver.Resolve(state, [replayIntent]).State;
            string actualHash = TinyFarmSemanticHash.Compute(state);
            if (!string.Equals(actualHash, record.ExpectedStateHash, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Replay state hash divergence at index {index}, sequence {record.Intent.Sequence}: expected '{record.ExpectedStateHash}', actual '{actualHash}'.");
            }
        }
        return new TinyFarmReplayResult(state, TinyFarmSemanticHash.Compute(state), envelope.Intents.Count);
    }
}
