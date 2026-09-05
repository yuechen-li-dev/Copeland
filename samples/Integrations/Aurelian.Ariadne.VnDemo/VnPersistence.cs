using Deliverance.Core;
using Deliverance.Core.Codecs;
using Deliverance.Core.Modules;
using Deliverance.Core.Serialization;
using Deliverance.Core.Storage;

namespace Aurelian.Ariadne.VnDemo;

public sealed class VnPersistence
{
    public const string ModuleId = "vn.dialogue.session";
    private const int SchemaVersion = 1;
    private readonly MessagePackSaveSerializer serializer = new();
    private readonly NoCompressionCodec compression = new();
    private readonly DeliveranceService deliverance;

    public VnPersistence(string directory)
    {
        var options = new DeliveranceOptions
        {
            Store = new FileSaveStore(directory),
            Serializer = serializer,
            DefaultCompression = compression,
            BuildId = "aurelian-ariadne-m7b",
            BackupCopiesToKeep = 1,
        };
        deliverance = new DeliveranceService(options);
    }

    public async Task SaveAsync(string slotId, VnSession session)
    {
        SaveModulePayload payload = SaveModulePayload.Create(
            ModuleId,
            SchemaVersion,
            ModuleCriticality.Required,
            serializer,
            compression,
            session.Capture());
        await deliverance.SaveAsync(
            slotId,
            new SaveRequest(
                new SaveApplicationMetadata(
                    ApplicationId: "aurelian.ariadne.vn-demo",
                    ApplicationVersion: "m7b",
                    BuildId: "aurelian-ariadne-m7b",
                    DefinitionHash: VnDialogueDefinition.DialogueId,
                    ApplicationSaveVersion: SchemaVersion),
                [payload]));
    }

    public async Task LoadAsync(string slotId, VnSession session)
    {
        var definition = new SaveModuleDefinition(ModuleId, SchemaVersion, ModuleCriticality.Required);
        LoadedSaveCandidate candidate = await deliverance.LoadAsync(
            slotId,
            [definition],
            new LoadCompatibility(
                ApplicationId: "aurelian.ariadne.vn-demo",
                DefinitionHash: VnDialogueDefinition.DialogueId,
                RequireCadenceMatch: false,
                ApplicationSaveVersion: SchemaVersion));
        VnSessionCheckpoint checkpoint = candidate.Deserialize<VnSessionCheckpoint>(ModuleId, deliverance.Options.Serializers);
        session.Restore(checkpoint);
    }
}
