using Deliverance.Core.Storage;
using InputMan.Core;
using TinyFarm.InputMan;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmFullGameM9Tests
{
    [Fact]
    public void StartHasObjectiveToolsAndNoCompletedJobs()
    {
        TinyFarmSupperGame game = Create();
        Assert.Equal(SupperScreen.Title, game.Screen);
        Assert.True(game.CapturesGameplay);
        Assert.Equal(TinyFarmSceneIds.Farm, game.State.CurrentScene);
        Assert.Contains(WorldFact.SupperRequested, game.State.Facts);
        Assert.False(TinyFarmSupper.IsReady(game.State));
        Assert.False(TinyFarmSupper.IsComplete(game.State));
        Assert.All(game.State.FarmPlots, plot => Assert.Null(plot.Crop));
        Assert.Contains(TinyFarmIds.Sword, game.State.Actor(TinyFarmIds.Player).Inventory);
        Assert.Contains(TinyFarmIds.Axe, game.State.Actor(TinyFarmIds.Player).Inventory);
        Assert.Equal(5, game.Objectives().Length);
    }

    [Fact]
    public void PrematureCompletionRejectsWithoutAnySemanticMutation()
    {
        TinyFarmSupperGame game = Create();
        string before = TinyFarmSemanticHash.Compute(game.State);
        var resolver = new TinyFarmResolver(game.Definitions);
        var reduction = resolver.Resolve(game.State,
            [new IntentEnvelope(TinyFarmIds.Player, new CompleteSupperIntent(), game.State.Minute, 0, IntentSourceKind.Human)]);
        Assert.Equal(IntentReason.SupperNotReady, reduction.Results.Single().Reason);
        Assert.Equal(before, TinyFarmSemanticHash.Compute(reduction.State));
    }

    [Fact]
    public void RealSessionCompletesWithDialogueScenesSaveRestoreAndReplayParity()
    {
        FileSaveStore store = NewStore();
        var game = new TinyFarmSupperGame(store);
        var walkthrough = new TinyFarmSupperWalkthrough(game);
        bool midSave = false;
        walkthrough.Checkpoint = name =>
        {
            if (name == "03-dialogue")
            {
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
                var restoredDialogue = new TinyFarmSupperGame(store);
                Assert.True(restoredDialogue.Load());
                Assert.True(restoredDialogue.Dialogue.IsActive);
                Assert.Equal(game.Dialogue.Presentation!.OperationId, restoredDialogue.Dialogue.Presentation!.OperationId);
                restoredDialogue.ApplyDialogue(TinyFarmDialogueAction.Advance);
                Assert.Equal(before, TinyFarmSemanticHash.Compute(restoredDialogue.State));
            }
            if (name != "mid-objective-save")
            {
                return;
            }
            Assert.False(TinyFarmSupper.IsComplete(game.State));
            Assert.True(TinyFarmSupper.IsReady(game.State));
            Assert.True(game.Save());
            TinyFarmSupperGame restored = new(store);
            Assert.True(restored.Load());
            Assert.Equal(TinyFarmSemanticHash.Compute(game.State), TinyFarmSemanticHash.Compute(restored.State));
            Assert.Equal(game.Host.Session.NextSequence, restored.Host.Session.NextSequence);
            Assert.Equal(game.State.SelectedHotbarSlot, restored.State.SelectedHotbarSlot);
            Assert.Equal(game.State.ActorScenes, restored.State.ActorScenes);
            Assert.Equal(game.State.Minute, restored.State.Minute);
            Assert.Equal(game.State.InventoryStacks, restored.State.InventoryStacks);
            new TinyFarmSupperWalkthrough(restored).FinishFromKitchen();
            Assert.True(TinyFarmSupper.IsComplete(restored.State));
            midSave = true;
        };
        walkthrough.Run();
        Assert.True(midSave);
        Assert.Equal(TinyFarmSemanticHash.Compute(game.State), walkthrough.Replay().FinalHash);
        Assert.Contains("mara.supper-help", game.Dialogue.Trace);
        Assert.Contains("mara.supper-ready", game.Dialogue.Trace);
        Assert.Contains("mara.supper-thanks", game.Dialogue.Trace);
        Assert.Equal(TinyFarmIds.Mara, game.State.Item(TinyFarmIds.WildMint).Owner);
        Assert.Equal(SupperScreen.Complete, game.Screen);
        Assert.True(game.EffectEvents > 0);
        Assert.True(game.AudioEvents > 0);
        Assert.Equal(IntentReason.SupperAlreadyCompleted, game.Execute(new CompleteSupperIntent()).Results.First().Reason);
    }

    [Fact]
    public void MenuContextsSuppressMovementAttackAndHotbar()
    {
        TinyFarmSupperGame game = Create();
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
        TinyFarmSupperGame game = Create();
        string before = TinyFarmSemanticHash.Compute(game.State);
        Assert.False(game.Load());
        Assert.Equal(before, TinyFarmSemanticHash.Compute(game.State));
        Assert.StartsWith("Could not continue", game.Status);
    }

    private static TinyFarmSupperGame Create() => new(NewStore());

    private static FileSaveStore NewStore()
    {
        return new FileSaveStore(Path.Combine(Path.GetTempPath(), "tinyfarm-m9-tests", Guid.NewGuid().ToString("N")));
    }
}
