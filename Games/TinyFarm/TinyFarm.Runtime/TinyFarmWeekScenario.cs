using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public sealed record TinyFarmWeekRun(
    string FinalHash,
    TinyFarmState FinalState,
    IReadOnlyList<string> ResultSequence,
    IReadOnlyList<string> EventSequence,
    int NarrativeLines,
    int SaveBytes,
    long ElapsedMicroseconds,
    long SaveMicroseconds,
    long LoadMicroseconds,
    long HashMicroseconds);

public sealed record TinyFarmM2Proof(
    string Milestone,
    string Outcome,
    string FinalHash,
    bool RepeatedRunMatches,
    bool ResultSequenceMatches,
    bool EventSequenceMatches,
    IReadOnlyDictionary<int, bool> SaveReloadPoints,
    string DefinitionSetId,
    string PersistenceDecision,
    IReadOnlyList<string> SaveChunks,
    int FinalDay,
    int FinalMinuteOfDay,
    int PlayerMoney,
    int PlayerTurnips,
    string PlotOneState,
    string PlotTwoState,
    int ShopSeedStock,
    int NpcPurchases,
    int AutonomousNpcMoves,
    int NarrativeLines,
    int CanonicalIntents,
    int SaveBytes,
    int ReplayBytes,
    long WeekMicroseconds,
    long AverageDayMicroseconds,
    long SaveMicroseconds,
    long LoadMicroseconds,
    long HashMicroseconds,
    int PersistenceMechanisms,
    string RecommendedM3);

public static class TinyFarmWeekScenario
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static IReadOnlyList<GameIntent> Script { get; } = BuildScript();
    public static IReadOnlyList<int> SavePoints { get; } = [3, 6, 13, 20, 27, 44];

    public static TinyFarmM2Proof Prove()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.Load();
        TinyFarmWeekRun first = Run(definitions, null);
        TinyFarmWeekRun second = Run(definitions, null);
        var reloads = SavePoints.ToDictionary(point => point, point => Run(definitions, point).FinalHash == first.FinalHash);
        int replayBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new { first.ResultSequence, first.EventSequence }));
        int npcPurchases = first.EventSequence.Count(item => item.Contains("ItemBought:mara", StringComparison.Ordinal));
        int npcMoves = first.EventSequence.Count(item =>
            item.Contains("ActorMoved:", StringComparison.Ordinal)
            && !item.Contains("ActorMoved:player", StringComparison.Ordinal));
        bool repeated = first.FinalHash == second.FinalHash;
        bool results = first.ResultSequence.SequenceEqual(second.ResultSequence, StringComparer.Ordinal);
        bool events = first.EventSequence.SequenceEqual(second.EventSequence, StringComparer.Ordinal);
        bool success = repeated
            && results
            && events
            && reloads.Values.All(value => value)
            && first.FinalState.Day == 7
            && first.FinalState.Facts.Contains(WorldFact.FirstCropSold);
        FarmPlotState plotOne = first.FinalState.FarmPlots.Single(plot => plot.Id == TinyFarmIds.PlotOne);
        FarmPlotState plotTwo = first.FinalState.FarmPlots.Single(plot => plot.Id == TinyFarmIds.PlotTwo);

        return new TinyFarmM2Proof(
            "TINY-FARM-M2",
            success ? "A" : "B",
            first.FinalHash,
            repeated,
            results,
            events,
            reloads,
            definitions.Identity,
            "REUSE_AS_IS",
            ["tinyfarm.world", "tinyfarm.runtime", "tinyfarm.agents", "tinyfarm.narrative"],
            first.FinalState.Day,
            first.FinalState.Minute % 1440,
            first.FinalState.Actor(TinyFarmIds.Player).Money,
            first.FinalState.ProductCount(TinyFarmIds.Player, TinyFarmIds.Turnip),
            DescribePlot(plotOne),
            DescribePlot(plotTwo),
            first.FinalState.ShopStock.Single(stock => stock.Product == TinyFarmIds.TurnipSeed).Count,
            npcPurchases,
            npcMoves,
            first.NarrativeLines,
            Script.Count,
            first.SaveBytes,
            replayBytes,
            first.ElapsedMicroseconds,
            first.ElapsedMicroseconds / 7,
            first.SaveMicroseconds,
            first.LoadMicroseconds,
            first.HashMicroseconds,
            1,
            "M3: first graphical projection over immutable TinyFarm inspection state");
    }

    public static TinyFarmWeekRun Run(TinyFarmDefinitions definitions, int? reloadAt)
    {
        var stopwatch = Stopwatch.StartNew();
        var session = new TinyFarmSession(TinyFarmContent.CreateWeekState(definitions), definitions);
        var results = new List<string>();
        var events = new List<string>();
        int narrativeLines = 0;
        int saveBytes = 0;
        long saveMicroseconds = 0;
        long loadMicroseconds = 0;

        for (int index = 0; index < Script.Count; index++)
        {
            if (reloadAt == index)
            {
                long beforeSave = Stopwatch.GetTimestamp();
                byte[] save = session.CaptureWeekSave();
                saveMicroseconds = ElapsedMicroseconds(beforeSave);
                saveBytes = save.Length;
                long beforeLoad = Stopwatch.GetTimestamp();
                session = TinyFarmChunkedSaveCodec.Read(save, definitions);
                loadMicroseconds = ElapsedMicroseconds(beforeLoad);
            }

            TinyFarmStepResult step = session.Step(Script[index]);
            narrativeLines += step.Narrative.Count;
            results.AddRange(step.Results.Select(ResultSignature));
            events.AddRange(step.Results.SelectMany(result => result.Events).Select(EventSignature));
        }

        if (saveBytes == 0)
        {
            long beforeSave = Stopwatch.GetTimestamp();
            byte[] save = session.CaptureWeekSave();
            saveMicroseconds = ElapsedMicroseconds(beforeSave);
            saveBytes = save.Length;
            long beforeLoad = Stopwatch.GetTimestamp();
            _ = TinyFarmChunkedSaveCodec.Read(save, definitions);
            loadMicroseconds = ElapsedMicroseconds(beforeLoad);
        }

        long beforeHash = Stopwatch.GetTimestamp();
        string hash = TinyFarmSemanticHash.Compute(session.State);
        long hashMicroseconds = ElapsedMicroseconds(beforeHash);
        stopwatch.Stop();
        return new TinyFarmWeekRun(hash, session.State.DeepCopy(), results, events, narrativeLines, saveBytes,
            stopwatch.ElapsedTicks * 1_000_000 / Stopwatch.Frequency, saveMicroseconds, loadMicroseconds, hashMicroseconds);
    }

    public static string WriteProofJson(TinyFarmM2Proof proof) => JsonSerializer.Serialize(proof, JsonOptions);

    public static byte[] CaptureFinalSave(TinyFarmDefinitions definitions)
    {
        var session = new TinyFarmSession(TinyFarmContent.CreateWeekState(definitions), definitions);
        foreach (GameIntent intent in Script)
        {
            session.Step(intent);
        }

        return session.CaptureWeekSave();
    }

    private static IReadOnlyList<GameIntent> BuildScript()
    {
        var script = new List<GameIntent>
        {
            new MoveIntent(TinyFarmIds.GeneralStore),
            new WaitIntent(60),
            new BuyProductIntent(TinyFarmIds.TurnipSeed),
            new MoveIntent(TinyFarmIds.TownSquare),
            new MoveIntent(TinyFarmIds.Farmhouse),
            new PlantIntent(TinyFarmIds.PlotOne, TinyFarmIds.TurnipCrop),
            new WaterIntent(TinyFarmIds.PlotOne)
        };
        AddDay(script);
        script.Add(new WaterIntent(TinyFarmIds.PlotOne));
        AddDay(script);
        script.Add(new WaterIntent(TinyFarmIds.PlotOne));
        AddDay(script);
        script.Add(new HarvestIntent(TinyFarmIds.PlotOne));
        script.Add(new MoveIntent(TinyFarmIds.TownSquare));
        script.Add(new MoveIntent(TinyFarmIds.GeneralStore));
        script.Add(new SellProductIntent(TinyFarmIds.Turnip));
        script.Add(new SellProductIntent(TinyFarmIds.Turnip));
        script.Add(new BuyProductIntent(TinyFarmIds.TurnipSeed));
        script.Add(new MoveIntent(TinyFarmIds.TownSquare));
        script.Add(new MoveIntent(TinyFarmIds.Farmhouse));
        script.Add(new PlantIntent(TinyFarmIds.PlotTwo, TinyFarmIds.TurnipCrop));
        script.Add(new WaterIntent(TinyFarmIds.PlotTwo));
        AddDay(script);
        script.Add(new WaterIntent(TinyFarmIds.PlotTwo));
        AddDay(script);
        script.Add(new WaterIntent(TinyFarmIds.PlotTwo));
        AddDay(script);
        script.Add(new HarvestIntent(TinyFarmIds.PlotTwo));
        script.Add(new MoveIntent(TinyFarmIds.TownSquare));
        script.Add(new MoveIntent(TinyFarmIds.GeneralStore));
        script.Add(new SellProductIntent(TinyFarmIds.Turnip));
        script.Add(new SellProductIntent(TinyFarmIds.Turnip));
        return script;
    }

    private static void AddDay(List<GameIntent> script)
    {
        for (int index = 0; index < 6; index++)
        {
            script.Add(new WaitIntent(240));
        }
    }

    private static string ResultSignature(IntentResult result)
    {
        return $"{result.Envelope.Sequence}|{result.Envelope.Actor}|{result.Envelope.Source}|{result.Envelope.Intent}|{result.Status}|{result.Reason}";
    }

    private static string EventSignature(GameEvent item)
    {
        return $"{item.Kind}:{item.Actor}:{item.Target}:{item.Item}:{item.Product}:{item.Crop}:{item.Plot}:{item.Location}:{item.Amount}:{item.Day}:{item.Dialogue}:{item.Favor}";
    }

    private static string DescribePlot(FarmPlotState plot)
    {
        return plot.Crop is null
            ? "empty"
            : $"{plot.Crop.Value.Value}:stage-{plot.GrowthStage}:watered-{plot.WateredToday}";
    }
    private static long ElapsedMicroseconds(long started) => (Stopwatch.GetTimestamp() - started) * 1_000_000 / Stopwatch.Frequency;
}
