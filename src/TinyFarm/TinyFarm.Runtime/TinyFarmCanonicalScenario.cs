using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public sealed record CanonicalRun(
    string FinalHash,
    IReadOnlyList<string> ResultSequence,
    TinyFarmState FinalState,
    int NarrativeLineCount,
    int SaveBytes,
    long ElapsedMicroseconds);

public sealed record TinyFarmM1Proof(
    string Milestone,
    string Outcome,
    string FinalHash,
    bool RepeatedRunMatches,
    bool SaveReloadMatches,
    bool ResultSequenceMatches,
    string ConflictWinner,
    int ConflictRejected,
    int AutonomousNpcMoves,
    string InvalidIntentReason,
    int AriadneLines,
    int CanonicalIntents,
    int SaveBytes,
    int ReplayBytes,
    long CanonicalDayMicroseconds);

public static class TinyFarmCanonicalScenario
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static IReadOnlyList<GameIntent> Script { get; } =
    [
        new TalkIntent(TinyFarmIds.Mara),
        new MoveIntent(TinyFarmIds.GeneralStore),
        new TalkIntent(TinyFarmIds.Sela),
        new BuyIntent(TinyFarmIds.Apple),
        new MoveIntent(TinyFarmIds.TownSquare),
        new WaitIntent(240),
        new MoveIntent(TinyFarmIds.Riverside),
        new TakeIntent(TinyFarmIds.WildMint),
        new GiveIntent(TinyFarmIds.Letter, TinyFarmIds.Elias),
        new TalkIntent(TinyFarmIds.Mara),
        new MoveIntent(TinyFarmIds.TownSquare),
        new MoveIntent(TinyFarmIds.GeneralStore),
        new SellIntent(TinyFarmIds.WildMint),
        new MoveIntent(TinyFarmIds.TownSquare),
        new WaitIntent(240),
        new WaitIntent(120)
    ];

    public static TinyFarmM1Proof Prove()
    {
        CanonicalRun first = Run(reloadAt: null);
        CanonicalRun second = Run(reloadAt: null);
        CanonicalRun reloaded = Run(reloadAt: 8);
        (string winner, int rejected) = ProveConflict();
        int autonomousMoves = ProveAutonomy();
        string invalidReason = ProveInvalidIntent();
        int replayBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(first.ResultSequence));

        bool repeated = first.FinalHash == second.FinalHash;
        bool saveReload = first.FinalHash == reloaded.FinalHash;
        bool sequenceMatches = first.ResultSequence.SequenceEqual(second.ResultSequence, StringComparer.Ordinal) &&
                               first.ResultSequence.SequenceEqual(reloaded.ResultSequence, StringComparer.Ordinal);
        string outcome = repeated && saveReload && sequenceMatches && rejected == 1 && autonomousMoves >= 2
            ? "A"
            : "B";

        return new TinyFarmM1Proof(
            "TINY-FARM-M1",
            outcome,
            first.FinalHash,
            repeated,
            saveReload,
            sequenceMatches,
            winner,
            rejected,
            autonomousMoves,
            invalidReason,
            first.NarrativeLineCount,
            Script.Count,
            reloaded.SaveBytes,
            replayBytes,
            first.ElapsedMicroseconds);
    }

    public static string WriteProofJson(TinyFarmM1Proof proof)
    {
        return JsonSerializer.Serialize(proof, JsonOptions);
    }

    public static CanonicalRun Run(int? reloadAt)
    {
        var stopwatch = Stopwatch.StartNew();
        var session = new TinyFarmSession(TinyFarmContent.CreateInitialState());
        var sequence = new List<string>();
        int narrativeLines = 0;
        int saveBytes = 0;

        for (int index = 0; index < Script.Count; index++)
        {
            if (reloadAt == index)
            {
                string save = TinyFarmSaveCodec.Write(session.CaptureSave());
                saveBytes = Encoding.UTF8.GetByteCount(save);
                session = TinyFarmSaveCodec.Read(save);
            }

            TinyFarmStepResult step = session.Step(Script[index]);
            narrativeLines += step.Narrative.Count;
            sequence.AddRange(step.Results.Select(Signature));
        }

        stopwatch.Stop();
        return new CanonicalRun(
            TinyFarmSemanticHash.Compute(session.State),
            sequence,
            session.State.DeepCopy(),
            narrativeLines,
            saveBytes,
            stopwatch.ElapsedTicks * 1_000_000 / Stopwatch.Frequency);
    }

    private static (string Winner, int Rejected) ProveConflict()
    {
        TinyFarmState state = TinyFarmContent.CreateInitialState();
        var setup = new[]
        {
            new IntentEnvelope(TinyFarmIds.Player, new MoveIntent(TinyFarmIds.Riverside), state.Minute, 0, IntentSourceKind.Human),
            new IntentEnvelope(TinyFarmIds.Mara, new MoveIntent(TinyFarmIds.Riverside), state.Minute, 1, IntentSourceKind.Dominatus)
        };
        state = new TinyFarmResolver().Resolve(state, setup).State;

        var envelopes = new[]
        {
            new IntentEnvelope(TinyFarmIds.Player, new TakeIntent(TinyFarmIds.WildMint), state.Minute, 0, IntentSourceKind.Human),
            new IntentEnvelope(TinyFarmIds.Mara, new TakeIntent(TinyFarmIds.WildMint), state.Minute, 0, IntentSourceKind.Dominatus)
        };
        ResolutionBatchResult result = new TinyFarmResolver().Resolve(state, envelopes.Reverse());
        IntentResult accepted = result.Results.Single(item => item.Status == IntentResultStatus.Accepted);
        int rejected = result.Results.Count(item => item.Status == IntentResultStatus.Rejected);
        return (accepted.Envelope.Actor.Value, rejected);
    }

    private static int ProveAutonomy()
    {
        var session = new TinyFarmSession(TinyFarmContent.CreateInitialState());
        TinyFarmStepResult result = session.Step(new WaitIntent(240));
        return result.Results
            .Where(item => item.Envelope.Source == IntentSourceKind.Dominatus)
            .SelectMany(item => item.Events)
            .Count(gameEvent => gameEvent.Kind == GameEventKind.ActorMoved);
    }

    private static string ProveInvalidIntent()
    {
        TinyFarmState state = TinyFarmContent.CreateInitialState();
        var envelope = new IntentEnvelope(
            TinyFarmIds.Player,
            new GiveIntent(TinyFarmIds.Apple, TinyFarmIds.Mara),
            state.Minute,
            0,
            IntentSourceKind.Human);
        return new TinyFarmResolver().Resolve(state, [envelope]).Results.Single().Reason.ToString();
    }

    private static string Signature(IntentResult result)
    {
        string events = string.Join(
            ',',
            result.Events.Select(gameEvent =>
                $"{gameEvent.Kind}:{gameEvent.Actor}:{gameEvent.Target}:{gameEvent.Item}:{gameEvent.Location}:{gameEvent.Amount}:{gameEvent.Dialogue}:{gameEvent.Favor}"));
        return $"{result.Envelope.Sequence}|{result.Envelope.Actor}|{result.Envelope.Source}|{result.Envelope.Intent}|{result.Status}|{result.Reason}|{events}";
    }
}
