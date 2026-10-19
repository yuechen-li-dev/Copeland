using Deliverance.Core.Storage;
using System.Diagnostics;
using InputMan.Core;
using TinyFarm.InputMan;
using Xunit;

namespace TinyFarm.Core.Tests;

/// <summary>The supper prototype, reconciled as the Sleeping Spring's fourth chapter.</summary>
public sealed class TinyFarmSupperChapterTests
{
    [Fact]
    public void TheChapterStartsQuietAndTheRiverRoadIsClosedUntilMaraAsks()
    {
        TinyFarmGame game = Create();
        Assert.Equal(TinyFarmChapter.Supper, game.Chapter);
        Assert.Equal(TinyFarmScreen.Title, game.Screen);
        Assert.True(game.CapturesGameplay);
        Assert.Equal(TinyFarmSceneIds.Farm, game.State.CurrentScene);
        Assert.DoesNotContain(WorldFact.SupperRequested, game.State.Facts);
        Assert.False(TinyFarmSupper.IsReady(game.State));
        Assert.True(TinyFarmSupper.IsAuthored(game.Definitions));
        Assert.False(TinyFarmOpeningRoutes.IsOpen(game.State, TinyFarmSceneIds.Riverside));
        Assert.Equal("THE SLEEPING SPRING", game.JournalTitle);
        Assert.Equal(5, game.Objectives().Length);
        // The earlier chapters do not offer supper at all.
        Assert.False(TinyFarmSupper.IsAuthored(TinyFarmShippingContent.Load()));
    }

    [Fact]
    public void PrematureCompletionRejectsWithoutAnySemanticMutation()
    {
        TinyFarmGame game = Create();
        string before = TinyFarmSemanticHash.Compute(game.State);
        var resolver = new TinyFarmResolver(game.Definitions);
        var reduction = resolver.Resolve(game.State,
            [new IntentEnvelope(TinyFarmIds.Player, new CompleteSupperIntent(), game.State.Minute, 0, IntentSourceKind.Human)]);
        Assert.Equal(IntentReason.SupperNotReady, reduction.Results.Single().Reason);
        Assert.Equal(before, TinyFarmSemanticHash.Compute(reduction.State));
    }

    [Fact]
    public void RealSessionCompletesSupperWithDialogueScenesSaveRestoreAndReplayParity()
    {
        FileSaveStore store = NewStore();
        var game = new TinyFarmGame(store);
        var walkthrough = new TinyFarmWalkthrough(game);
        bool requestChecked = false;
        bool midSave = false;
        walkthrough.Checkpoint = name =>
        {
            if (name == "03-opening-dialogue")
            {
                // Before Old Burrow, Mara gives the opening advice and does not ask yet.
                Assert.Equal("mara.opening", game.Dialogue.Presentation!.OperationId);
                Assert.False(TinyFarmSupper.IsRequested(game.State));
            }
            if (name == "03-supper-request")
            {
                Assert.Equal("mara.supper-invite", game.Dialogue.Presentation!.OperationId);
                Assert.True(TinyFarmSupper.IsRequested(game.State));
                Assert.True(TinyFarmOpeningRoutes.IsOpen(game.State, TinyFarmSceneIds.Riverside));
                Assert.True(game.State.Actor(TinyFarmIds.Player).Rpg!.Crafting!.Knows(TinyFarmIds.SauteedHenOfTheWoodsRecipe));
                Assert.Equal("SUPPER WITH MARA", game.JournalTitle);

                // Saved mid-conversation, the request and the dialogue come back together.
                var engine = new InputManEngine(GameControls.CreateProfile());
                engine.SetMaps(game.Contexts);
                engine.Tick(new InputSnapshot(new Dictionary<ControlKey, bool>
                {
                    [Controls.Key(KeyboardKey.W)] = true,
                    [Controls.Key(KeyboardKey.Number4)] = true
                }, new Dictionary<ControlKey, float>()), .016f, 0);
                string before = TinyFarmSemanticHash.Compute(game.State);
                game.Handle(engine.CurrentFrame);
                game.Advance(TimeSpan.FromSeconds(1), engine.CurrentFrame, true);
                Assert.Equal(before, TinyFarmSemanticHash.Compute(game.State));
                Assert.True(game.Save());
                var restoredDialogue = new TinyFarmGame(store);
                Assert.True(restoredDialogue.Load());
                Assert.True(restoredDialogue.Dialogue.IsActive);
                Assert.Equal(game.Dialogue.Presentation!.OperationId, restoredDialogue.Dialogue.Presentation!.OperationId);
                restoredDialogue.ApplyDialogue(TinyFarmDialogueAction.Advance);
                Assert.Equal(before, TinyFarmSemanticHash.Compute(restoredDialogue.State));
                requestChecked = true;
            }
            if (name != "mid-objective-save")
            {
                return;
            }
            Assert.False(TinyFarmSupper.IsComplete(game.State));
            Assert.True(TinyFarmSupper.IsReady(game.State));
            Assert.All(game.Objectives().Take(4), objective => Assert.StartsWith("[done]", objective));
            Assert.True(game.Save());
            TinyFarmGame restored = new(store);
            Assert.True(restored.Load());
            Assert.Equal(TinyFarmSemanticHash.Compute(game.State), TinyFarmSemanticHash.Compute(restored.State));
            Assert.Equal(game.Host.Session.NextSequence, restored.Host.Session.NextSequence);
            Assert.Equal(game.State.ActorScenes, restored.State.ActorScenes);
            Assert.Equal(game.State.InventoryStacks, restored.State.InventoryStacks);
            new TinyFarmWalkthrough(restored).FinishWithMara();
            Assert.True(TinyFarmSupper.IsComplete(restored.State));
            midSave = true;
        };
        walkthrough.Run();
        Assert.True(requestChecked);
        Assert.True(midSave);
        Assert.Equal(TinyFarmSemanticHash.Compute(game.State), walkthrough.Replay().FinalHash);
        Assert.Contains("mara.supper-ready", game.Dialogue.Trace);
        Assert.Contains("mara.supper-thanks", game.Dialogue.Trace);
        Assert.Equal(TinyFarmIds.Mara, game.State.Item(TinyFarmIds.WildMint).Owner);
        // The dish went on Mara's table, supper restored you, and play simply continues.
        Assert.Equal(0, game.State.ProductCount(TinyFarmIds.Player, TinyFarmIds.SauteedHenOfTheWoods));
        Assert.Equal(12, game.State.Slice!.Health);
        Assert.Equal(TinyFarmScreen.Playing, game.Screen);
        Assert.StartsWith("Supper with Mara", game.Status);
        Assert.True(game.EffectEvents > 0);
        Assert.True(game.AudioEvents > 0);
        Assert.Equal(IntentReason.SupperAlreadyCompleted, game.Execute(new CompleteSupperIntent()).Results.First().Reason);

        // Afterwards Mara remembers it.
        Assert.Equal(IntentResultStatus.Accepted, game.Execute(new InteractIntent()).Results.First().Status);
        Assert.Equal("mara.supper-after", game.Dialogue.Presentation!.OperationId);
    }

    [Fact]
    public void MenuContextsSuppressMovementAttackAndHotbar()
    {
        TinyFarmGame game = Create();
        var engine = new InputManEngine(GameControls.CreateProfile());
        engine.SetMaps(game.Contexts);
        engine.Tick(new InputSnapshot(new Dictionary<ControlKey, bool>
        {
            [Controls.Key(KeyboardKey.W)] = true,
            [Controls.Key(KeyboardKey.Space)] = true,
            [Controls.Key(KeyboardKey.Number4)] = true
        }, new Dictionary<ControlKey, float>()), .016f, 0);
        string before = TinyFarmSemanticHash.Compute(game.State);
        game.Handle(engine.CurrentFrame);
        game.Advance(TimeSpan.FromSeconds(1), engine.CurrentFrame, true);
        Assert.Equal(before, TinyFarmSemanticHash.Compute(game.State));
    }

    [Fact]
    public void MissingSaveLeavesLiveSessionIntactAndReportsFailure()
    {
        TinyFarmGame game = Create();
        string before = TinyFarmSemanticHash.Compute(game.State);
        Assert.False(game.Load());
        Assert.Equal(before, TinyFarmSemanticHash.Compute(game.State));
        Assert.StartsWith("Could not continue", game.Status);
    }

    [Fact]
    public async Task GameplaySaveBeginsWithoutWaitingForStorage()
    {
        var store = new GatedSaveStore();
        var game = new TinyFarmGame(store);
        game.Start();

        long started = Stopwatch.GetTimestamp();
        Assert.True(game.BeginSave());
        TimeSpan beginElapsed = Stopwatch.GetElapsedTime(started);

        Assert.True(game.SaveInProgress);
        Assert.True(beginElapsed < TimeSpan.FromMilliseconds(100));

        store.Release();
        var engine = new InputManEngine(GameControls.CreateProfile());
        engine.SetMaps(game.Contexts);
        engine.Tick(InputSnapshot.Empty, 0, 0);
        for (int attempt = 0; attempt < 100 && game.SaveInProgress; attempt++)
        {
            await Task.Delay(5);
            game.Advance(TimeSpan.Zero, engine.CurrentFrame, focused: true);
        }

        Assert.False(game.SaveInProgress);
        Assert.StartsWith("Saved.", game.Status);
    }

    private static TinyFarmGame Create() => new(NewStore());

    private static FileSaveStore NewStore()
    {
        return new FileSaveStore(Path.Combine(Path.GetTempPath(), "tinyfarm-supper-tests", Guid.NewGuid().ToString("N")));
    }

    private sealed class GatedSaveStore : ISaveStore
    {
        private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => gate.SetResult();

        public Task<bool> ExistsAsync(string slotId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<IReadOnlyList<string>> ListSlotsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<SlotInfo?> GetSlotInfoAsync(string slotId, CancellationToken ct = default) => Task.FromResult<SlotInfo?>(null);
        public Task<IReadOnlyList<SlotInfo>> ListSlotInfosAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SlotInfo>>([]);
        public Task<ReadOnlyMemory<byte>> ReadSlotAsync(string slotId, CancellationToken ct = default) => throw new FileNotFoundException();
        public Task WriteSlotAsync(string slotId, ReadOnlyMemory<byte> bytes, int keepBackups, CancellationToken ct = default) => gate.Task;
        public Task DeleteAsync(string slotId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
