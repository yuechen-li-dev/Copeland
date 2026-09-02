using System.Text;
using Dominatus.Core.Persistence;
using TinyFarm.Core;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmM2Tests
{
    private readonly TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.Load();

    [Fact]
    public void TsonDefinitions_LoadStableTypedContent()
    {
        Assert.StartsWith("tiny-farm-content-m2-sha256:", definitions.Identity);
        Assert.Equal(2, definitions.Items.Count);
        Assert.Equal(3, definitions.Crop(TinyFarmIds.TurnipCrop).GrowthDays);
        Assert.Equal(TinyFarmIds.TurnipSeed, definitions.Crop(TinyFarmIds.TurnipCrop).SeedItemId);
    }

    [Fact]
    public void PlantWaterHarvest_UseActorGenericResolverAndTypedRejections()
    {
        TinyFarmState state = TinyFarmContent.CreateWeekState(definitions);
        state = Resolve(state, TinyFarmIds.Player, new MoveIntent(TinyFarmIds.Farmhouse)).State;
        IntentResult missingSeed = Resolve(
            state,
            TinyFarmIds.Player,
            new PlantIntent(TinyFarmIds.PlotOne, TinyFarmIds.TurnipCrop)).Results.Single();
        Assert.Equal(IntentReason.ItemNotOwned, missingSeed.Reason);
        Assert.Equal(IntentReason.PlotEmpty, Resolve(state, TinyFarmIds.Player, new WaterIntent(TinyFarmIds.PlotOne)).Results.Single().Reason);

        state.MutableInventoryStacks.Add(new InventoryStack(TinyFarmIds.Mara, TinyFarmIds.TurnipSeed, 1));
        ActorState mara = state.Actor(TinyFarmIds.Mara);
        state.MutableActors[state.MutableActors.IndexOf(mara)] = mara with { Location = TinyFarmIds.Farmhouse };
        ResolutionBatchResult planted = Resolve(
            state,
            TinyFarmIds.Mara,
            new PlantIntent(TinyFarmIds.PlotOne, TinyFarmIds.TurnipCrop),
            IntentSourceKind.Dominatus);
        Assert.Equal(IntentResultStatus.Accepted, planted.Results.Single().Status);
        IntentResult occupied = Resolve(
            planted.State,
            TinyFarmIds.Player,
            new PlantIntent(TinyFarmIds.PlotOne, TinyFarmIds.TurnipCrop)).Results.Single();
        Assert.Equal(IntentReason.PlotOccupied, occupied.Reason);
        Assert.Equal(IntentResultStatus.Accepted, Resolve(planted.State, TinyFarmIds.Player, new WaterIntent(TinyFarmIds.PlotOne)).Results.Single().Status);
        Assert.Equal(IntentReason.CropImmature, Resolve(planted.State, TinyFarmIds.Player, new HarvestIntent(TinyFarmIds.PlotOne)).Results.Single().Reason);
    }

    [Fact]
    public void WateredCrop_AdvancesOnlyAtExplicitDayBoundary()
    {
        TinyFarmState state = PreparedPlantedState();
        state = Resolve(state, TinyFarmIds.Player, new WaterIntent(TinyFarmIds.PlotOne)).State;
        state = Resolve(state, TinyFarmIds.Player, new WaitIntent(240)).State;
        Assert.Equal(0, state.FarmPlots.Single(plot => plot.Id == TinyFarmIds.PlotOne).GrowthStage);
        for (int index = 0; index < 5; index++)
        {
            state = Resolve(state, TinyFarmIds.Player, new WaitIntent(240)).State;
        }
        FarmPlotState plot = state.FarmPlots.Single(item => item.Id == TinyFarmIds.PlotOne);
        Assert.Equal(1, plot.GrowthStage);
        Assert.False(plot.WateredToday);
    }

    [Fact]
    public void CanonicalWeek_CompletesEconomyLoopAndAllReloadPoints()
    {
        TinyFarmM2Proof proof = TinyFarmWeekScenario.Prove();
        Assert.Equal("A", proof.Outcome);
        Assert.Equal(7, proof.FinalDay);
        Assert.Equal(28, proof.PlayerMoney);
        Assert.Equal(0, proof.PlayerTurnips);
        Assert.All(proof.SaveReloadPoints, item => Assert.True(item.Value));
        Assert.True(proof.NpcPurchases >= 1);
    }

    [Fact]
    public void WeekSchedule_HasMarketDayAndWeekendVariation()
    {
        int saturdayTen = 5 * 1440 + 10 * 60;
        int sundayTen = 6 * 1440 + 10 * 60;
        Assert.Equal(TinyFarmIds.GeneralStore, TinyFarmNpcController.ScheduledDestination(TinyFarmIds.Mara, saturdayTen));
        Assert.Equal(TinyFarmIds.Riverside, TinyFarmNpcController.ScheduledDestination(TinyFarmIds.Mara, sundayTen));
    }

    [Fact]
    public void FinalSeedStockConflict_HasStableActorOrderedWinner()
    {
        TinyFarmState state = TinyFarmContent.CreateWeekState(definitions);
        state.Minute = 9 * 60;
        ShopStock stock = state.ShopStock.Single();
        state.MutableShopStock[0] = stock with { Count = 1 };
        ActorState player = state.Actor(TinyFarmIds.Player);
        ActorState mara = state.Actor(TinyFarmIds.Mara);
        state.MutableActors[state.MutableActors.IndexOf(player)] = player with { Location = TinyFarmIds.GeneralStore };
        state.MutableActors[state.MutableActors.IndexOf(mara)] = mara with { Location = TinyFarmIds.GeneralStore };
        var intents = new[]
        {
            new IntentEnvelope(TinyFarmIds.Player, new BuyProductIntent(TinyFarmIds.TurnipSeed), state.Minute, 0, IntentSourceKind.Human),
            new IntentEnvelope(TinyFarmIds.Mara, new BuyProductIntent(TinyFarmIds.TurnipSeed), state.Minute, 0, IntentSourceKind.Dominatus)
        };
        ResolutionBatchResult result = new TinyFarmResolver(definitions).Resolve(state, intents.Reverse());
        Assert.Equal(TinyFarmIds.Mara, result.Results.Single(item => item.Status == IntentResultStatus.Accepted).Envelope.Actor);
        Assert.Equal(IntentReason.StockUnavailable, result.Results.Single(item => item.Status == IntentResultStatus.Rejected).Reason);
    }

    [Fact]
    public void ChunkedSave_RoundTripsAndIgnoresUnknownOptionalChunk()
    {
        var session = new TinyFarmSession(TinyFarmContent.CreateWeekState(definitions), definitions);
        byte[] save = session.CaptureWeekSave();
        List<SaveChunk> chunks = ReadChunks(save);
        Assert.Equal(["tinyfarm.world", "tinyfarm.runtime", "tinyfarm.agents", "tinyfarm.narrative"], chunks.Select(chunk => chunk.Id.Value));
        chunks.Add(new SaveChunk(new ChunkId("future.optional"), "{}"u8.ToArray()));
        TinyFarmSession loaded = TinyFarmChunkedSaveCodec.Read(WriteChunks(chunks), definitions);
        Assert.Equal(TinyFarmSemanticHash.Compute(session.State), TinyFarmSemanticHash.Compute(loaded.State));
    }

    [Fact]
    public void ChunkedSave_MissingMalformedAndVersionMismatchFailBeforeSessionCreation()
    {
        var session = new TinyFarmSession(TinyFarmContent.CreateWeekState(definitions), definitions);
        List<SaveChunk> chunks = ReadChunks(session.CaptureWeekSave());
        List<SaveChunk> missingRuntime = chunks
            .Where(chunk => chunk.Id != TinyFarmChunkedSaveCodec.RuntimeChunk)
            .ToList();
        Assert.Throws<InvalidDataException>(() =>
            TinyFarmChunkedSaveCodec.Read(WriteChunks(missingRuntime), definitions));

        List<SaveChunk> malformed = chunks.Select(chunk =>
                chunk.Id == TinyFarmChunkedSaveCodec.WorldChunk
                    ? new SaveChunk(chunk.Id, "{"u8.ToArray())
                    : chunk)
            .ToList();
        Assert.Throws<InvalidDataException>(() => TinyFarmChunkedSaveCodec.Read(WriteChunks(malformed), definitions));

        List<SaveChunk> wrongVersion = ReplaceWorldText(chunks, "tiny-farm-m2@2", "tiny-farm-m2@9");
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => TinyFarmChunkedSaveCodec.Read(WriteChunks(wrongVersion), definitions));
        Assert.Contains("Unsupported TinyFarm runtime version", exception.Message);
    }

    [Fact]
    public void ChunkedSave_RejectsDefinitionAndUnknownCropMismatch()
    {
        var session = new TinyFarmSession(PreparedPlantedState(), definitions);
        List<SaveChunk> chunks = ReadChunks(session.CaptureWeekSave());
        TinyFarmDefinitions otherDefinitions = new("different", definitions.Items, definitions.Crops);
        Assert.Throws<InvalidDataException>(() => TinyFarmChunkedSaveCodec.Read(WriteChunks(chunks), otherDefinitions));

        List<SaveChunk> unknownCrop = ReplaceWorldText(chunks, "\"crop\":{\"value\":\"turnip\"}", "\"crop\":{\"value\":\"unknown\"}");
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => TinyFarmChunkedSaveCodec.Read(WriteChunks(unknownCrop), definitions));
        Assert.Contains("unknown crop", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void M1CanonicalHash_RemainsExact()
    {
        Assert.Equal("dcc35869aba0eba979725b1871d0babfe127383123a1a5f665b666bc3488d333", TinyFarmCanonicalScenario.Prove().FinalHash);
    }

    private TinyFarmState PreparedPlantedState()
    {
        TinyFarmState state = TinyFarmContent.CreateWeekState(definitions);
        state.MutableInventoryStacks.Add(new InventoryStack(TinyFarmIds.Player, TinyFarmIds.TurnipSeed, 1));
        state = Resolve(state, TinyFarmIds.Player, new MoveIntent(TinyFarmIds.Farmhouse)).State;
        return Resolve(state, TinyFarmIds.Player, new PlantIntent(TinyFarmIds.PlotOne, TinyFarmIds.TurnipCrop)).State;
    }

    private ResolutionBatchResult Resolve(TinyFarmState state, ActorId actor, GameIntent intent, IntentSourceKind source = IntentSourceKind.Human)
    {
        return new TinyFarmResolver(definitions).Resolve(state, [new IntentEnvelope(actor, intent, state.Minute, 0, source)]);
    }

    private static List<SaveChunk> ReplaceWorldText(List<SaveChunk> chunks, string oldValue, string newValue)
    {
        return chunks.Select(chunk =>
        {
            if (chunk.Id != TinyFarmChunkedSaveCodec.WorldChunk)
            {
                return chunk;
            }
            string original = Encoding.UTF8.GetString(chunk.Payload);
            string replaced = original.Replace(oldValue, newValue, StringComparison.Ordinal);
            Assert.NotEqual(original, replaced);
            return new SaveChunk(chunk.Id, Encoding.UTF8.GetBytes(replaced));
        }).ToList();
    }

    private static List<SaveChunk> ReadChunks(byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), $"tinyfarm-test-{Guid.NewGuid():N}.save");
        try
        {
            File.WriteAllBytes(path, bytes);
            return SaveFile.Read(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] WriteChunks(IReadOnlyList<SaveChunk> chunks)
    {
        string path = Path.Combine(Path.GetTempPath(), $"tinyfarm-test-{Guid.NewGuid():N}.save");
        try
        {
            SaveFile.Write(path, chunks);
            return File.ReadAllBytes(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
