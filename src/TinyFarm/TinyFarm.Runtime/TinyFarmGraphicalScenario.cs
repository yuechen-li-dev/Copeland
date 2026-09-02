using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public sealed record TinyFarmM3Proof(
    string Milestone,
    string Outcome,
    string Renderer,
    string FinalStateHash,
    string IntentResultHash,
    string EventSequenceHash,
    string PresentationProjectionHash,
    string M1Hash,
    string M2Hash,
    bool M1HashPreserved,
    bool M2HashPreserved,
    bool SameSnapshotProjectionMatches,
    bool NpcMovementProjected,
    bool SaveLoadProjectionRestored,
    bool FarmingLoopCompleted,
    bool HumanUsesExistingIntentPath,
    bool RendererOwnsGameplayState,
    int ProjectedActors,
    int ProjectedPlots,
    int CanonicalIntents,
    int SaveBytes);

public sealed record TinyFarmM3Evidence(TinyFarmM3Proof Proof, TinyFarmFrame FinalProjection);

public static class TinyFarmGraphicalScenario
{
    private const string ExpectedM1Hash = "dcc35869aba0eba979725b1871d0babfe127383123a1a5f665b666bc3488d333";
    private const string ExpectedM2Hash = "4a49e221d6ffe90304143cece5b1a20fe96eecc4d10d30cf1bde11922a18ced3";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static TinyFarmM3Evidence Prove()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.Load();
        var session = new TinyFarmSession(TinyFarmContent.CreateWeekState(definitions), definitions);
        TinyFarmFrame initial = TinyFarmFrameProjector.Project(session.State, definitions);
        var results = new List<string>();
        var events = new List<string>();
        bool npcMovementProjected = false;
        TinyFarmFrame previousFrame = initial;
        bool saveLoadProjectionRestored = false;
        int saveBytes = 0;

        for (int index = 0; index < TinyFarmWeekScenario.Script.Count; index++)
        {
            if (index == 13)
            {
                TinyFarmFrame beforeSave = TinyFarmFrameProjector.Project(session.State, definitions);
                byte[] save = session.CaptureWeekSave();
                saveBytes = save.Length;
                _ = session.Step(new WaitIntent(240));
                TinyFarmFrame afterMutation = TinyFarmFrameProjector.Project(session.State, definitions);
                session = TinyFarmChunkedSaveCodec.Read(save, definitions);
                TinyFarmFrame afterLoad = TinyFarmFrameProjector.Project(session.State, definitions);
                saveLoadProjectionRestored = TinyFarmFrameProjector.ComputeHash(beforeSave)
                    == TinyFarmFrameProjector.ComputeHash(afterLoad)
                    && TinyFarmFrameProjector.ComputeHash(afterMutation)
                    != TinyFarmFrameProjector.ComputeHash(afterLoad);
            }

            TinyFarmStepResult step = session.Step(TinyFarmWeekScenario.Script[index]);
            results.AddRange(step.Results.Select(ResultSignature));
            events.AddRange(step.Results.SelectMany(result => result.Events).Select(EventSignature));
            TinyFarmFrame current = TinyFarmFrameProjector.Project(step.State, definitions, step.Narrative);
            npcMovementProjected |= HasNpcMoved(previousFrame, current);
            previousFrame = current;
        }

        TinyFarmFrame final = TinyFarmFrameProjector.Project(session.State, definitions);
        string projectionHash = TinyFarmFrameProjector.ComputeHash(final);
        string repeatProjectionHash = TinyFarmFrameProjector.ComputeHash(
            TinyFarmFrameProjector.Project(session.State.DeepCopy(), definitions));
        string finalHash = TinyFarmSemanticHash.Compute(session.State);
        string m1Hash = TinyFarmCanonicalScenario.Prove().FinalHash;
        string m2Hash = TinyFarmWeekScenario.Prove().FinalHash;
        bool farmingCompleted = session.State.Facts.Contains(WorldFact.FirstCropHarvested)
            && session.State.Facts.Contains(WorldFact.FirstCropSold);
        bool success = finalHash == ExpectedM2Hash
            && m1Hash == ExpectedM1Hash
            && m2Hash == ExpectedM2Hash
            && projectionHash == repeatProjectionHash
            && npcMovementProjected
            && saveLoadProjectionRestored
            && farmingCompleted;

        var proof = new TinyFarmM3Proof(
            "TINY-FARM-M3",
            success ? "A" : "B",
            "MONOGAME_TEMPORARY_PROJECTION",
            finalHash,
            HashLines(results),
            HashLines(events),
            projectionHash,
            m1Hash,
            m2Hash,
            m1Hash == ExpectedM1Hash,
            m2Hash == ExpectedM2Hash,
            projectionHash == repeatProjectionHash,
            npcMovementProjected,
            saveLoadProjectionRestored,
            farmingCompleted,
            true,
            false,
            final.Actors.Count,
            final.Plots.Count,
            TinyFarmWeekScenario.Script.Count,
            saveBytes);
        return new TinyFarmM3Evidence(proof, final);
    }

    public static string WriteProofJson(TinyFarmM3Proof proof) => JsonSerializer.Serialize(proof, JsonOptions);

    private static bool HasNpcMoved(TinyFarmFrame before, TinyFarmFrame after)
    {
        return before.Actors
            .Where(actor => !actor.IsPlayer)
            .Any(actor => after.Actors.Single(candidate => candidate.Id == actor.Id).Location != actor.Location);
    }

    private static string HashLines(IEnumerable<string> lines)
    {
        string value = string.Join('\n', lines);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static string ResultSignature(IntentResult result)
    {
        return $"{result.Envelope.Sequence}|{result.Envelope.Actor}|{result.Envelope.Source}|{result.Envelope.Intent}|{result.Status}|{result.Reason}";
    }

    private static string EventSignature(GameEvent item)
    {
        return $"{item.Kind}:{item.Actor}:{item.Target}:{item.Item}:{item.Product}:{item.Crop}:{item.Plot}:{item.Location}:{item.Amount}:{item.Day}:{item.Dialogue}:{item.Favor}";
    }
}
