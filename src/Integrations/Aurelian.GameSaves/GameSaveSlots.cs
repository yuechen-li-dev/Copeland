using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Deliverance.Core;
using Deliverance.Core.Modules;
using Deliverance.Core.Serialization;
using Deliverance.Core.Storage;

namespace Aurelian.GameSaves;

/// <summary>Deliverance owns containers, integrity, atomic writes and slot management.
/// The game explicitly captures, validates and commits its own typed snapshot.</summary>
public sealed class GameSaveSlots<TSnapshot> where TSnapshot : class
{
    private const string ModuleId = "game.state";
    private readonly DeliveranceService service;
    private readonly JsonTypeInfo<TSnapshot> metadata;
    private readonly Action<TSnapshot> validate;
    private readonly SaveApplicationMetadata identity;
    private readonly int version;
    private readonly IReadOnlyList<ModuleMigration> migrations;

    /// <param name="version">Schema version written into new saves.</param>
    /// <param name="migrations">Byte-level steps that bring an older save forward, one version at a time. Loading runs
    /// them before validation; a save that is newer than <paramref name="version"/>, or that has no path forward, is
    /// rejected without touching live state.</param>
    public GameSaveSlots(string applicationId, string definitionIdentity, ISaveStore store,
        JsonTypeInfo<TSnapshot> metadata, Action<TSnapshot> validate, int version = 1,
        IReadOnlyList<ModuleMigration>? migrations = null)
    {
        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Save schema versions start at 1.");
        }
        this.version = version;
        this.migrations = migrations ?? [];
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionIdentity);
        this.metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        this.validate = validate ?? throw new ArgumentNullException(nameof(validate));
        identity = new(applicationId, "1", DefinitionHash: definitionIdentity, ApplicationSaveVersion: 1);
        service = new DeliveranceService(new DeliveranceOptions
        {
            Store = store,
            Serializer = new MessagePackSaveSerializer(),
        });
    }

    public Task SaveAsync(string slot, TSnapshot snapshot, CancellationToken cancellation = default)
    {
        validate(snapshot);
        var payload = new SaveModulePayload(ModuleId, version, ModuleCriticality.Required, 0, 0,
            JsonSerializer.SerializeToUtf8Bytes(snapshot, metadata));
        return service.SaveAsync(slot, new SaveRequest(identity, [payload]), cancellation);
    }

    public async Task<TSnapshot> LoadAsync(string slot, CancellationToken cancellation = default)
    {
        var definition = new SaveModuleDefinition(ModuleId, version, ModuleCriticality.Required, migrations: migrations,
            validateCurrentPayload: bytes => _ = Decode(bytes));
        var compatibility = new LoadCompatibility(identity.ApplicationId, identity.DefinitionHash,
            RequireCadenceMatch: false, ApplicationSaveVersion: 1);
        LoadedSaveCandidate candidate = await service.LoadAsync(slot, [definition], compatibility, cancellation);
        return Decode(candidate.GetModule(ModuleId).Payload);
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellation = default) => service.ListSlotsAsync(cancellation);
    public Task<bool> ExistsAsync(string slot, CancellationToken cancellation = default) => service.SlotExistsAsync(slot, cancellation);
    public Task DeleteAsync(string slot, CancellationToken cancellation = default) => service.DeleteSlotAsync(slot, cancellation);

    private TSnapshot Decode(ReadOnlyMemory<byte> bytes)
    {
        TSnapshot snapshot = JsonSerializer.Deserialize(bytes.Span, metadata)
            ?? throw new InvalidDataException("The game save contains an empty state.");
        validate(snapshot);
        return snapshot;
    }
}
