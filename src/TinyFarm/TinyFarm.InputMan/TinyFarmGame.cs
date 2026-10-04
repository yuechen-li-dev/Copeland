using Aurelian.Audio;
using Aurelian.Effects2D;
using Deliverance.Core;
using Deliverance.Core.Storage;
using InputMan.Core;
using TinyFarm.Core;
using TinyFarm.Runtime;
using TinyFarm.Oblivion;

namespace TinyFarm.InputMan;

public enum TinyFarmScreen
{
    Title,
    Playing,
    Paused,
    Inventory,
    Stats,
    Crafting,
    Complete
}

/// <summary>Small-game application policy; all world changes go through the simulation host.</summary>
public sealed partial class TinyFarmGame
{
    private readonly TinyFarmInputController controls = new();
    private readonly TinyFarmAudioProjector audioProjector = new();
    private readonly TinyFarmVisualEffectProjector effectProjector = new();
    private readonly ISaveStore store;
    private SceneId? effectsScene;
    private bool completionShown;
    private Task? pendingSave;
    private Task<LoadedSaveCandidate>? pendingLoad;

    public TinyFarmGame(ISaveStore store, bool slice = false, TinyFarmAuthoredWorld? authored = null, bool crafting = false)
    {
        this.store = store;
        Definitions = authored?.Definitions ?? (crafting ? TinyFarmCraftingContent.Load() : slice ? TinyFarmSliceContent.Load() : TinyFarmDefinitionLoader.LoadM21());
        TinyFarmState initial = authored?.State ?? (crafting ? TinyFarmCraftingContent.Start(Definitions) : slice ? TinyFarmSliceContent.Start(Definitions) : TinyFarmSupperStart.Create(Definitions));
        Host = new TinyFarmSimulationHost(new TinyFarmSession(initial, Definitions), Definitions,
            rates: slice ? new TinyFarmSimulationRates(NormalRealSecondsPerGameMinute: 1) : null);
        Dialogue = new TinyFarmDialogueCoordinator(Host);
        Persistence = new TinyFarmDeliverancePersistence(Host, Definitions, store, dialogue: Dialogue);
        LiveInspection = new TinyFarmOblivionLiveSurfaces(Host);
        MenuSaveAvailable = HasSave;
    }

    public TinyFarmDefinitions Definitions { get; }
    public TinyFarmSimulationHost Host { get; }
    public TinyFarmDialogueCoordinator Dialogue { get; }
    public TinyFarmDeliverancePersistence Persistence { get; }
    public TinyFarmOblivionLiveSurfaces LiveInspection { get; }
    public TinyFarmState State => Host.Session.State;
    public TinyFarmScreen Screen { get; private set; } = TinyFarmScreen.Title;
    public string Status { get; private set; } = "A note from Mara: let us make this place feel like home.";
    public bool ShouldQuit { get; private set; }
    public TinyFarmPresentationPreferences Presentation { get; } = new();
    public TinyFarmMenus Menus { get; } = new();
    public bool CapturesGameplay => Screen != TinyFarmScreen.Playing || Dialogue.IsActive;
    public EffectRuntime Effects { get; private set; } = NewEffects();
    public Queue<AudioCue> PendingAudio { get; } = new();
    public int AcceptedActions { get; private set; }
    public int RejectedActions { get; private set; }
    public int EffectEvents { get; private set; }
    public int AudioEvents { get; private set; }
    public int FeedbackEpoch { get; private set; }
    private string SaveSlot => State.Version >= TinyFarmState.CraftingSaveVersion ? "sleeping-spring-a2" : State.Slice is not null ? "sleeping-spring-gate-a" : "supper";
    public bool HasSave => store.ExistsAsync(SaveSlot).GetAwaiter().GetResult();
    public bool SaveInProgress => pendingSave is not null;
    public bool LoadInProgress => pendingLoad is not null;

    public ActionMapId[] Contexts
    {
        get
        {
            if (IsAgentMenu && Menus.SearchFocused)
            {
                return [GameControls.System, GameControls.TextEntry];
            }
            if (Dialogue.IsActive)
            {
                return [GameControls.System, GameControls.Dialogue];
            }
            if (CapturesGameplay)
            {
                return [GameControls.System, GameControls.Shortcuts, GameControls.Ui];
            }
            return [GameControls.System, GameControls.Shortcuts, GameControls.Gameplay];
        }
    }

    public bool IsAgentMenu => Screen is TinyFarmScreen.Inventory or TinyFarmScreen.Stats;
    public bool IsModalMenu => IsAgentMenu || Screen is TinyFarmScreen.Crafting or TinyFarmScreen.Title;

    public void Start()
    {
        if (Screen == TinyFarmScreen.Title)
        {
            Status = State.Slice is not null
                ? "A turnip is ready. Harvest it, make broth at home, then follow the path to Old Burrow."
                : "Plant a seed by the house. Mara is in town until noon, then by the river.";
        }
        Screen = TinyFarmScreen.Playing;
    }

    public void Handle(InputFrame input)
    {
        Presentation.Handle(input);
        if (IsAgentMenu && Menus.SearchFocused)
        {
            if (input.WasPressed(GameControls.UiSearchFinish))
            {
                Menus.SearchFocused = false;
            }
            return;
        }
        if (input.WasPressed(GameControls.Save) && Screen != TinyFarmScreen.Title)
        {
            BeginSave();
            return;
        }
        if (input.WasPressed(GameControls.Load))
        {
            if (Screen == TinyFarmScreen.Title && !MenuSaveAvailable)
            {
                Status = "No saved game yet. Choose New Game to begin.";
                return;
            }
            BeginLoad();
            return;
        }
        if (Screen != TinyFarmScreen.Playing && input.WasPressed(GameControls.Quit))
        {
            if (SaveInProgress || LoadInProgress)
            {
                Status = "Wait for the checkpoint operation before quitting.";
                return;
            }
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
            HandleMenuInput(input);
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
                    Screen = TinyFarmScreen.Paused;
                    Menus.Confirmation = null;
                    MenuSaveAvailable = HasSave;
                    break;
                case ToggleInventoryCommand:
                    OpenInventory(false);
                    break;
                case ToggleStatsCommand:
                    OpenStats(false);
                    break;
            }
            if (CapturesGameplay)
            {
                break;
            }
        }
        if (State.Slice is not null && !CapturesGameplay)
        {
            var direction = input.GetAxis2(GameControls.Move);
            if (input.WasPressed(GameControls.Sword))
            {
                Execute(new SwordIntent());
            }
            if (input.WasPressed(GameControls.Dodge))
            {
                Execute(new DodgeIntent(Math.Sign(direction.X), -Math.Sign(direction.Y)));
            }
            if (input.WasPressed(GameControls.Eat))
            {
                Execute(new EatIntent());
            }
            if (input.WasPressed(GameControls.Sleep))
            {
                Execute(new SleepIntent());
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
        int y = playing && (State.Slice is not null || x == 0) ? -Math.Sign(move.Y) : 0;
        Host.SetPlayerMovement(x, y);
        TinyFarmHostAdvanceResult advanced = Host.AdvanceHostTime(elapsed);
        SynchronizeScene();
        if (playing)
        {
            IntentResult[] feedback = advanced.Results.Where(result =>
                (result.Envelope.Intent is not SliceTickIntent || result.Events.Count > 0) && (result.Envelope.Intent is not SpatialMoveIntent
                || result.Envelope.Actor == TinyFarmIds.Player
                && result.Envelope.Sequence % 12 == 0)).ToArray();
            ProjectFeedback(feedback);
            Effects.Update(elapsed);
            if (State.Slice is not null && advanced.Results.Any(result => result.Events.Any(item => item.Kind == GameEventKind.PlayerReturnedForRest)))
            {
                if (Save())
                {
                    Status = "Night brought you home. Rested, saved, and ready for a new morning.";
                }
            }
            else if (State.Slice is not null && State.Minute % 1440 == 1290
                && advanced.WorldMinutesAdvanced > 0)
            {
                Status = "Evening is settling. At 22:00 you will return home for rest.";
            }
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
        if (State.Slice is not null)
        {
            Status = result.Status == IntentResultStatus.Rejected ? result.Reason switch
            {
                IntentReason.MissingIngredient => "Broth needs one turnip. Harvest the cream bulb in your garden.",
                IntentReason.WrongWeapon or IntentReason.MissingSword => "Equip your sword in I / Equipment before striking. Finish an active dodge first.",
                IntentReason.MissingAxe => "Equip your axe in I / Equipment, then use tool 3 beside a tree.",
                IntentReason.WrongLocation => intent is SleepIntent ? "Rest beside your bed at home."
                    : "This road is closed for now. Follow the woodland path east.",
                _ => "Move closer and face the object. E interacts; K uses your selected tool."
            } : intent switch
            {
                SleepIntent => "Morning. Watered crops have grown. Your garden is waiting.",
                EatIntent when result.Status == IntentResultStatus.Accepted => "Warm broth restores health. Ready for another try.",
                _ => result.Events.LastOrDefault()?.Kind switch
                {
                    GameEventKind.DayStarted => "Morning. Watered crops have grown. Your garden is waiting.",
                    GameEventKind.PlotWatered => "Watered. Rest in your bed tonight, then return to your garden.",
                    GameEventKind.SceneEntered => OpeningSceneStatus(),
                    GameEventKind.CropPlanted => "Your seed is planted. E waters it; sleep at home to grow it.",
                    GameEventKind.CropHarvested => State.Slice.OwnHarvest
                        ? "A turnip of your own. The stove turns it into healing broth."
                        : "Starter turnip harvested. Bring it to the house stove for broth.",
                    GameEventKind.RecipeCooked => "One turnip, one broth. R eats it when you need health.",
                    _ => Status
                }
            };
        }
        if (State.Slice is not null && result.Status == IntentResultStatus.Accepted
            && result.Events.Any(item => item.Kind == GameEventKind.DayStarted))
        {
            if (Save())
            {
                Status = "Morning. Your garden has grown. Rest saved your progress.";
            }
        }
        ApplyCraftingFeedback(intent, result);
        CheckCompletion();
        return step;
    }

    public void ApplyDialogue(TinyFarmDialogueAction action)
    {
        Dialogue.Apply(action);
        CheckCompletion();
    }

    private string OpeningSceneStatus()
    {
        if (State.CurrentScene == TinyFarmSceneIds.DungeonEntrance)
        {
            return "Watch the amber jump line. Dodge sideways; strike while the slime recovers.";
        }
        if (State.CurrentScene == TinyFarmSceneIds.Overworld)
        {
            return "Follow the path across the bridge, then north to Old Burrow.";
        }
        if (State.CurrentScene == TinyFarmSceneIds.Residence)
        {
            return State.Version >= TinyFarmState.CraftingSaveVersion
                ? "E opens the stove. Pick up the recipe card beside your bed, or experiment."
                : "The stove cooks broth. Your bed is beside the south wall.";
        }
        return "Home again. Watered crops grow after a night's rest.";
    }

    public bool Save()
    {
        if (Host.Session.HasActiveCombat)
        {
            Status = "Finish the swing before saving.";
            return false;
        }
        try
        {
            // Sleep checkpoints must follow any earlier background manual save to this slot.
            Task? earlierSave = pendingSave;
            pendingSave = null;
            earlierSave?.GetAwaiter().GetResult();
            Persistence.Deliverance.SaveAsync(SaveSlot, Persistence.CaptureSave(SaveSlot)).GetAwaiter().GetResult();
            MenuSaveAvailable = true;
            Status = State.Slice is not null
                ? "Saved. N continues your garden and adventure from here."
                : "Saved. Your supper, world, and conversation are safe. N continues from here.";
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
        if (Host.Session.HasActiveCombat)
        {
            Status = "Finish the swing before saving.";
            return false;
        }
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
                SaveRequest request = Persistence.CreateSaveRequest(SaveSlot, snapshot);
                await Persistence.Deliverance.SaveAsync(SaveSlot, request).ConfigureAwait(false);
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
            LoadedSaveCandidate candidate = Persistence.Deliverance.LoadAsync(SaveSlot,
                Persistence.GetLoadDefinitions(SaveSlot), Persistence.GetLoadCompatibility(SaveSlot)).GetAwaiter().GetResult();
            Persistence.CommitLoadedCandidate(SaveSlot, candidate);
            Screen = TinyFarmScreen.Playing;
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
                SaveSlot,
                Persistence.GetLoadDefinitions(SaveSlot),
                Persistence.GetLoadCompatibility(SaveSlot));
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
                MenuSaveAvailable = true;
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
                Persistence.CommitLoadedCandidate(SaveSlot, candidate);
                Screen = TinyFarmScreen.Playing;
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
        if (State.Slice is TinyFarmSliceState slice)
        {
            return
            [
                Mark(slice.CookedBroth, State.Version >= TinyFarmState.CraftingSaveVersion ? "Harvest a turnip; E opens your stove to cook soup" : "Harvest the starter turnip; E cooks broth at home"),
                Mark(slice.Defeats > 0, "Follow the east path; clear Old Burrow"),
                Mark(slice.ReturnedHome, "Return home from your adventure"),
                Mark(slice.Slept, "Plant with 1 + K, water with E, sleep in your bed"),
                Mark(slice.OwnHarvest, "Return in the morning and harvest your own turnip")
            ];
        }
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
        if (State.Slice is TinyFarmSliceState slice)
        {
            if (slice.LoopComplete && !completionShown)
            {
                completionShown = true;
                Status = "Home, garden, adventure. Opening loop complete. Stay a little longer.";
            }
            return;
        }
        if (TinyFarmSupper.IsComplete(State) && !Dialogue.IsActive && !completionShown)
        {
            completionShown = true;
            Screen = TinyFarmScreen.Complete;
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
        if (results.Any(result => result.Events.Any(item => item.Kind == GameEventKind.PlayerRescued)))
        {
            Status = "Caught your breath at the entrance. Try again, or retreat home for rest.";
        }
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
