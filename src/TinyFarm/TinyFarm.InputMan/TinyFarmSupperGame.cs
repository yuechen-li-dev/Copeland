using Aurelian.Audio;
using Aurelian.Effects2D;
using Deliverance.Core;
using Deliverance.Core.Storage;
using InputMan.Core;
using TinyFarm.Core;
using TinyFarm.Runtime;

namespace TinyFarm.InputMan;

public enum SupperScreen
{
    Title,
    Playing,
    Paused,
    Inventory,
    Complete
}

/// <summary>Small-game application policy; all world changes go through the simulation host.</summary>
public sealed class TinyFarmSupperGame
{
    private readonly TinyFarmInputController controls = new();
    private readonly TinyFarmAudioProjector audioProjector = new();
    private readonly TinyFarmVisualEffectProjector effectProjector = new();
    private readonly ISaveStore store;
    private SceneId? effectsScene;
    private bool completionShown;
    private Task? pendingSave;
    private Task<LoadedSaveCandidate>? pendingLoad;

    public TinyFarmSupperGame(ISaveStore store)
    {
        this.store = store;
        Definitions = TinyFarmDefinitionLoader.LoadM21();
        Host = new TinyFarmSimulationHost(new TinyFarmSession(TinyFarmSupperStart.Create(Definitions), Definitions), Definitions);
        Dialogue = new TinyFarmDialogueCoordinator(Host);
        Persistence = new TinyFarmDeliverancePersistence(Host, Definitions, store, dialogue: Dialogue);
    }

    public TinyFarmDefinitions Definitions { get; }
    public TinyFarmSimulationHost Host { get; }
    public TinyFarmDialogueCoordinator Dialogue { get; }
    public TinyFarmDeliverancePersistence Persistence { get; }
    public TinyFarmState State => Host.Session.State;
    public SupperScreen Screen { get; private set; } = SupperScreen.Title;
    public string Status { get; private set; } = "A note from Mara: let us make this place feel like home.";
    public bool ShouldQuit { get; private set; }
    public bool CapturesGameplay => Screen != SupperScreen.Playing || Dialogue.IsActive;
    public EffectRuntime Effects { get; private set; } = NewEffects();
    public Queue<AudioCue> PendingAudio { get; } = new();
    public int AcceptedActions { get; private set; }
    public int RejectedActions { get; private set; }
    public int EffectEvents { get; private set; }
    public int AudioEvents { get; private set; }
    public int FeedbackEpoch { get; private set; }
    public bool HasSave => store.ExistsAsync("supper").GetAwaiter().GetResult();
    public bool SaveInProgress => pendingSave is not null;
    public bool LoadInProgress => pendingLoad is not null;

    public ActionMapId[] Contexts => Dialogue.IsActive
        ? [GameControls.System, GameControls.Dialogue]
        : CapturesGameplay ? [GameControls.System, GameControls.Ui] : [GameControls.System, GameControls.Gameplay];

    public void Start()
    {
        if (Screen == SupperScreen.Title)
        {
            Status = "Plant a seed by the house. Mara is in town until noon, then by the river.";
        }
        Screen = SupperScreen.Playing;
    }

    public void Handle(InputFrame input)
    {
        if (input.WasPressed(GameControls.Save) && Screen != SupperScreen.Title)
        {
            BeginSave();
            return;
        }
        if (input.WasPressed(GameControls.Load))
        {
            BeginLoad();
            return;
        }
        if (Screen != SupperScreen.Playing && input.WasPressed(GameControls.Quit))
        {
            ShouldQuit = true;
            return;
        }
        if (Dialogue.IsActive)
        {
            if (controls.MapDialogue(input) is TinyFarmDialogueAction action)
            {
                ApplyDialogue(action);
            }
            return;
        }
        if (CapturesGameplay)
        {
            if (input.WasPressed(GameControls.UiConfirm) || input.WasPressed(GameControls.UiCancel))
            {
                Start();
            }
            return;
        }
        foreach (TinyFarmInputCommand command in controls.Map(input))
        {
            switch (command)
            {
                // Movement is sampled below and reduced at the host's fixed cadence.
                case SubmitGameIntent { Intent: SpatialMoveIntent }:
                    break;
                case SubmitGameIntent submit:
                    Execute(submit.Intent);
                    break;
                case TogglePauseCommand:
                    Screen = SupperScreen.Paused;
                    break;
                case ToggleInventoryCommand:
                    Screen = SupperScreen.Inventory;
                    break;
            }
            if (CapturesGameplay)
            {
                break;
            }
        }
    }

    public void Advance(TimeSpan elapsed, InputFrame input, bool focused)
    {
        CompletePendingPersistence();
        bool playing = !CapturesGameplay && focused;
        Host.Execute(new SetSimulationModeCommand(playing ? TinyFarmSimulationMode.Playing : TinyFarmSimulationMode.Paused));
        var move = input.GetAxis2(GameControls.Move);
        int x = playing ? Math.Sign(move.X) : 0;
        int y = playing && x == 0 ? -Math.Sign(move.Y) : 0;
        Host.SetPlayerMovement(x, y);
        TinyFarmHostAdvanceResult advanced = Host.AdvanceHostTime(elapsed);
        SynchronizeScene();
        if (playing)
        {
            IntentResult[] footsteps = advanced.Results.Where(result =>
                result.Envelope.Actor == TinyFarmIds.Player
                && result.Envelope.Intent is SpatialMoveIntent
                && result.Envelope.Sequence % 12 == 0).ToArray();
            ProjectFeedback(footsteps);
            Effects.Update(elapsed);
        }
        CheckCompletion();
    }

    public TinyFarmStepResult Execute(GameIntent intent)
    {
        TinyFarmStepResult step = Host.ExecuteIntent(intent);
        SynchronizeScene();
        ProjectFeedback(step.Results);
        IntentResult result = step.Results.First();
        if (result.Status == IntentResultStatus.Rejected)
        {
            RejectedActions++;
            Status = result.Reason switch
            {
                IntentReason.NoInteractionTarget => "Face something nearby, then press E. The prompt tells you what will happen.",
                IntentReason.WrongWeapon => "Select the sword with 4, then press SPACE beside the slime.",
                IntentReason.MissingIngredient => "The stove needs mushrooms. Gather them beside the river first.",
                IntentReason.SupperNotReady => "A few supper jobs remain. Your journal shows what is missing.",
                _ => "That did not work: " + result.Reason + ". Try moving closer or changing tools."
            };
        }
        else
        {
            AcceptedActions++;
            Status = result.Events.LastOrDefault()?.Kind switch
            {
                GameEventKind.CropPlanted => "A seed for tomorrow. No waiting needed: planting counts!",
                GameEventKind.ItemTaken => "Wild mint tucked safely away for Mara.",
                GameEventKind.ForageGathered => "Mushrooms gathered. The stove in Hearth House is ready.",
                GameEventKind.RecipeCooked => "Supper smells excellent. Try not to eat the evidence.",
                GameEventKind.EnemyDefeated => "Old Burrow is quiet again. One fewer uninvited dinner guest.",
                GameEventKind.TreeChopped => "A little firewood. A very satisfying thump.",
                GameEventKind.SceneEntered => "A new corner of home. Follow the doorway signs to return.",
                _ => Status
            };
        }
        Dialogue.TryBeginFrom(step);
        CheckCompletion();
        return step;
    }

    public void ApplyDialogue(TinyFarmDialogueAction action)
    {
        Dialogue.Apply(action);
        CheckCompletion();
    }

    public bool Save()
    {
        try
        {
            Persistence.Deliverance.SaveAsync("supper", Persistence.CaptureSave("supper")).GetAwaiter().GetResult();
            Status = "Saved. Your supper, world, and conversation are safe. N continues from here.";
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Status = "Could not save: " + error.Message;
            return false;
        }
    }

    public bool BeginSave()
    {
        if (pendingSave is not null || pendingLoad is not null)
        {
            Status = "A persistence operation is already in progress.";
            return false;
        }
        try
        {
            TinyFarmSemanticSaveSnapshot snapshot = Persistence.CaptureSnapshot();
            pendingSave = Task.Run(async () =>
            {
                SaveRequest request = Persistence.CreateSaveRequest("supper", snapshot);
                await Persistence.Deliverance.SaveAsync("supper", request).ConfigureAwait(false);
            });
            Status = "Saving in the background...";
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Status = "Could not save: " + error.Message;
            return false;
        }
    }

    public bool Load()
    {
        try
        {
            LoadedSaveCandidate candidate = Persistence.Deliverance.LoadAsync("supper",
                Persistence.GetLoadDefinitions("supper"), Persistence.GetLoadCompatibility("supper")).GetAwaiter().GetResult();
            Persistence.CommitLoadedCandidate("supper", candidate);
            Screen = SupperScreen.Playing;
            completionShown = TinyFarmSupper.IsComplete(State);
            effectsScene = null;
            FeedbackEpoch++;
            Effects = NewEffects();
            PendingAudio.Clear();
            Status = "Welcome back. Everything is just where you left it.";
            SynchronizeScene();
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Status = "Could not continue: " + error.Message;
            return false;
        }
    }

    public bool BeginLoad()
    {
        if (pendingLoad is not null || pendingSave is not null)
        {
            Status = "A persistence operation is already in progress.";
            return false;
        }
        try
        {
            pendingLoad = Persistence.Deliverance.LoadAsync(
                "supper",
                Persistence.GetLoadDefinitions("supper"),
                Persistence.GetLoadCompatibility("supper"));
            Status = "Loading in the background...";
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Status = "Could not continue: " + error.Message;
            return false;
        }
    }

    private void CompletePendingPersistence()
    {
        if (pendingSave?.IsCompleted == true)
        {
            try
            {
                pendingSave.GetAwaiter().GetResult();
                Status = "Saved. Your supper, world, and conversation are safe. N continues from here.";
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Status = "Could not save: " + error.Message;
            }
            pendingSave = null;
        }

        if (pendingLoad?.IsCompleted == true)
        {
            try
            {
                LoadedSaveCandidate candidate = pendingLoad.GetAwaiter().GetResult();
                Persistence.CommitLoadedCandidate("supper", candidate);
                Screen = SupperScreen.Playing;
                completionShown = TinyFarmSupper.IsComplete(State);
                effectsScene = null;
                FeedbackEpoch++;
                Effects = NewEffects();
                PendingAudio.Clear();
                Status = "Welcome back. Everything is just where you left it.";
                SynchronizeScene();
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Status = "Could not continue: " + error.Message;
            }
            pendingLoad = null;
        }
    }

    public string[] Objectives()
    {
        string Mark(bool done, string text) => (done ? "[done] " : "[  ] ") + text;
        return
        [
            Mark(State.Facts.Contains(WorldFact.SupperSeedPlanted), "Plant a turnip / 1 + SPACE"),
            Mark(State.ProductCount(TinyFarmIds.Player, TinyFarmIds.SauteedHenOfTheWoods) > 0, "River mushrooms to home stove / E"),
            Mark(State.Enemy(TinyFarmIds.DungeonSlime).Lifecycle == EnemyLifecycle.Defeated, "Clear Old Burrow / 4 + SPACE"),
            Mark(State.Item(TinyFarmIds.WildMint).Owner is not null, "Mint by the farm plots / E"),
            Mark(TinyFarmSupper.IsComplete(State), "Return to Mara with supper / E")
        ];
    }

    private void CheckCompletion()
    {
        if (TinyFarmSupper.IsComplete(State) && !Dialogue.IsActive && !completionShown)
        {
            completionShown = true;
            Screen = SupperScreen.Complete;
            Status = "Supper is ready. Tomorrow can wait.";
        }
    }

    private void SynchronizeScene()
    {
        SceneId scene = State.ActorScene(TinyFarmIds.Player).Scene;
        if (effectsScene == scene)
        {
            return;
        }
        effectsScene = scene;
        Effects = NewEffects();
        Effects.TryEmit(effectProjector.ProjectAmbience(scene), out _);
    }

    private void ProjectFeedback(IReadOnlyList<IntentResult> results)
    {
        foreach (VisualEffectEvent effect in effectProjector.Project(results, State, Definitions))
        {
            if (Effects.TryEmit(effect, out _))
            {
                EffectEvents++;
            }
        }
        foreach (AudioCue cue in audioProjector.Project(results))
        {
            if (PendingAudio.Count >= 32)
            {
                PendingAudio.Dequeue();
            }
            PendingAudio.Enqueue(cue with { EventId = new AudioEventId($"{FeedbackEpoch}:{cue.EventId.Value}") });
            AudioEvents++;
        }
    }

    private static EffectRuntime NewEffects() => new(EffectCatalog.CreateSmallGameDefaults(), 256, 32);
}
