using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public sealed record TinyFarmAgentInspection(
    ActorId Id,
    string Name,
    TinyFarmAgentKind Kind,
    TinyFarmAgentControl Control,
    SceneId Scene,
    ScenePosition Position,
    TinyFarmAgentHealth? Health,
    int Level,
    IReadOnlyList<string> Conditions,
    IReadOnlyList<TinyFarmInventoryRow> Inventory,
    TinyFarmEquipment Equipment,
    TinyFarmObjectPose? ObjectPose,
    TinyFarmAgentAppearance Appearance,
    bool UsesLegacyStateAdapter);

public sealed record TinyFarmAgentAuthoringProof(
    string Outcome,
    string DefinitionIdentity,
    int SaveVersion,
    int PassiveDominatusAgents,
    bool SaveRoundTripQualified,
    bool ReflectionSerializationDisabled,
    string StateHash,
    IReadOnlyList<TinyFarmAgentInspection> Agents);

public static class TinyFarmAgentInspector
{
    public static TinyFarmAgentInspection Inspect(TinyFarmState state, TinyFarmDefinitions definitions, ActorId id)
    {
        ActorState actor = state.Actor(id);
        ActorSceneState placement = state.ActorScene(id);
        TinyFarmAgentState? data = actor.Agent;
        TinyFarmAgentHealth? health = data?.Health;
        if (data is null && actor.IsPlayer && state.Slice is not null)
        {
            health = new TinyFarmAgentHealth(state.Slice.Health, 12);
        }
        TinyFarmEquipment equipment = data?.Equipment ?? new TinyFarmEquipment(null, null);
        if (actor.IsPlayer)
        {
            equipment = TinyFarmEquipmentRules.Current(state);
        }
        TinyFarmAgentAppearance appearance = data?.Appearance ?? new TinyFarmAgentAppearance(
            id == TinyFarmIds.Mara ? TinyFarmAgentSprite.Mara : TinyFarmAgentSprite.Gardener);
        return new TinyFarmAgentInspection(actor.Id, actor.Name, data?.Kind ?? TinyFarmAgentKind.Character,
            data?.Control ?? (actor.IsPlayer ? TinyFarmAgentControl.Human : TinyFarmAgentControl.Schedule),
            placement.Scene, placement.WorldPosition, health, data?.Level ?? 1, data?.Conditions ?? [],
            TinyFarmInventory.Project(state, definitions, id), equipment, data?.ObjectPose, appearance, data is null);
    }

    public static TinyFarmAgentAuthoringProof Prove()
    {
        TinyFarmAuthoredWorld world = TinyFarmAgentExamples.Create();
        var session = new TinyFarmSession(world.State, world.Definitions);
        session.Step(new WaitIntent(1));
        string hash = TinyFarmSemanticHash.Compute(session.State);
        TinyFarmSession restored = TinyFarmChunkedSaveCodec.Read(
            TinyFarmChunkedSaveCodec.Write(session, world.Definitions), world.Definitions);
        if (TinyFarmSemanticHash.Compute(restored.State) != hash || session.PassiveDominatusAgentCount != 2)
        {
            throw new InvalidOperationException("Agent authoring did not preserve save state or create both passive Dominatus brains.");
        }
        return new TinyFarmAgentAuthoringProof("Success: typed agent authoring reaches the real world/session/save path",
            world.Definitions.Identity, restored.State.Version, session.PassiveDominatusAgentCount, true,
            !JsonSerializer.IsReflectionEnabledByDefault, hash,
            restored.State.Actors.OrderBy(actor => actor.Id.Value, StringComparer.Ordinal)
                .Select(actor => Inspect(restored.State, world.Definitions, actor.Id)).ToArray());
    }

    public static void WriteArtifacts(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "authoring-proof.json"), JsonSerializer.Serialize(Prove(),
            TinyFarmAgentInspectionJsonContext.Default.TinyFarmAgentAuthoringProof) + Environment.NewLine);
    }
}

[JsonSerializable(typeof(TinyFarmAgentAuthoringProof))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
internal partial class TinyFarmAgentInspectionJsonContext : JsonSerializerContext;
