using Deliverance.Core;
using Deliverance.Core.Codecs;
using Deliverance.Core.Modules;
using Deliverance.Core.Serialization;
using Deliverance.Core.Storage;

namespace Aurelian.Ariadne.VnDemo;

public sealed record RenGameSave(
    int ApplicationSaveVersion,
    string ActiveScene,
    string PendingOperation,
    DateTimeOffset SavedAtUtc,
    VnSessionCheckpoint Session);

public sealed record RenSaveSlotMetadata(
    int SlotNumber,
    string SlotId,
    bool Available,
    DateTimeOffset? SavedAtUtc,
    string LineLabel,
    bool Corrupt = false);

public sealed class VnPersistence
{
    public const string ModuleId = "renc.sunkill.session";
    public const int SchemaVersion = 1;
    public const int SlotCount = 3;
    private const string ApplicationId = "renc.sunkill";
    private const string BuildId = "renc-vn-m13";
    private const string DefinitionHash = "sunkill-dawn-engine-v1";
    private readonly MessagePackSaveSerializer serializer = new();
    private readonly NoCompressionCodec compression = new();
    private readonly DeliveranceService deliverance;

    public VnPersistence(string directory)
    {
        Directory = directory;
        var options = new DeliveranceOptions
        {
            Store = new FileSaveStore(directory),
            Serializer = serializer,
            DefaultCompression = compression,
            BuildId = BuildId,
            BackupCopiesToKeep = 1,
        };
        deliverance = new DeliveranceService(options);
    }

    public string Directory { get; }

    public static string SlotId(int slotNumber)
    {
        if (slotNumber is < 1 or > SlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slotNumber));
        }

        return $"slot-{slotNumber}";
    }

    public async Task<RenGameSave> SaveAsync(
        int slotNumber,
        VnSession session,
        DateTimeOffset? savedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        var envelope = new RenGameSave(
            SchemaVersion,
            SunkillDialogue.DialogueId,
            session.Presentation.OperationId ?? "terminal",
            savedAtUtc ?? DateTimeOffset.UtcNow,
            session.Capture());
        SaveModulePayload payload = SaveModulePayload.Create(
            ModuleId,
            SchemaVersion,
            ModuleCriticality.Required,
            serializer,
            compression,
            envelope);
        await deliverance.SaveAsync(
            SlotId(slotNumber),
            new SaveRequest(
                new SaveApplicationMetadata(
                    ApplicationId: ApplicationId,
                    ApplicationVersion: "m13",
                    BuildId: BuildId,
                    DefinitionHash: DefinitionHash,
                    ApplicationSaveVersion: SchemaVersion),
                [payload]));
        return envelope;
    }

    public async Task<RenGameSave> LoadAsync(int slotNumber, VnSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        RenGameSave envelope = await ReadAsync(slotNumber);
        Validate(envelope);
        session.Restore(envelope.Session);
        return envelope;
    }

    public async Task<IReadOnlyList<RenSaveSlotMetadata>> ReadSlotMetadataAsync()
    {
        var result = new List<RenSaveSlotMetadata>(SlotCount);
        for (int slotNumber = 1; slotNumber <= SlotCount; slotNumber++)
        {
            try
            {
                RenGameSave envelope = await ReadAsync(slotNumber);
                Validate(envelope);
                result.Add(new RenSaveSlotMetadata(
                    slotNumber,
                    SlotId(slotNumber),
                    true,
                    envelope.SavedAtUtc,
                    ShortLabel(envelope.PendingOperation)));
            }
            catch (FileNotFoundException)
            {
                result.Add(Empty(slotNumber));
            }
            catch (IOException)
            {
                result.Add(new RenSaveSlotMetadata(
                    slotNumber,
                    SlotId(slotNumber),
                    false,
                    null,
                    "CORRUPT SAVE",
                    Corrupt: true));
            }
        }

        return result;
    }

    private async Task<RenGameSave> ReadAsync(int slotNumber)
    {
        var definition = new SaveModuleDefinition(
            ModuleId,
            SchemaVersion,
            ModuleCriticality.Required);
        LoadedSaveCandidate candidate = await deliverance.LoadAsync(
            SlotId(slotNumber),
            [definition],
            new LoadCompatibility(
                ApplicationId,
                DefinitionHash,
                RequireCadenceMatch: false,
                ApplicationSaveVersion: SchemaVersion));
        return candidate.Deserialize<RenGameSave>(
            ModuleId,
            deliverance.Options.Serializers);
    }

    private static void Validate(RenGameSave envelope)
    {
        if (envelope.ApplicationSaveVersion != SchemaVersion
            || envelope.ActiveScene != SunkillDialogue.DialogueId
            || string.IsNullOrWhiteSpace(envelope.PendingOperation)
            || envelope.Session is null)
        {
            throw new InvalidDataException("The SUNKILL save envelope is incompatible or incomplete.");
        }
    }

    private static RenSaveSlotMetadata Empty(int slotNumber)
    {
        return new RenSaveSlotMetadata(
            slotNumber,
            SlotId(slotNumber),
            false,
            null,
            "EMPTY");
    }

    private static string ShortLabel(string operationId)
    {
        int separator = operationId.LastIndexOf('.');
        return separator >= 0 ? operationId[(separator + 1)..].Replace('-', ' ').ToUpperInvariant() : operationId;
    }
}
