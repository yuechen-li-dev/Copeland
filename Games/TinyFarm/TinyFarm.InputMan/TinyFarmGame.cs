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
    Container
}

/// <summary>
/// The Sleeping Spring grows in authored chapters. Each adds content to the one before and keeps its own definition
/// identity, so older chapters stay reproducible for their tests.
/// </summary>
public enum TinyFarmChapter
{
    /// <summary>Gate A: garden, broth, Old Burrow, sleep and your own harvest.</summary>
    Opening,

    /// <summary>A2: the stove, recipes, experiments and rock salt.</summary>
    Crafting,

    /// <summary>A3: the shipping chest.</summary>
    Shipping,

    /// <summary>A4: Mara's supper, the river road, mushrooms and mint. The full game.</summary>
    Supper,
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
    private bool supperShown;
    private Task? pendingSave;
    private Task<LoadedSaveCandidate>? pendingLoad;

    /// <param name="authored">A specific world instead of the chapter's start (tests and tools).</param>
    public TinyFarmGame(ISaveStore store, TinyFarmChapter chapter = TinyFarmChapter.Supper, TinyFarmAuthoredWorld? authored = null)
    {
        this.store = store;
        Chapter = chapter;
        Definitions = authored?.Definitions ?? LoadDefinitions(chapter);
        TinyFarmState initial = authored?.State ?? StartState(chapter, Definitions);
        Host = new TinyFarmSimulationHost(new TinyFarmSession(initial, Definitions), Definitions,
            rates: new TinyFarmSimulationRates(NormalRealSecondsPerGameMinute: 1));
        Dialogue = new TinyFarmDialogueCoordinator(Host);
        Persistence = new TinyFarmDeliverancePersistence(Host, Definitions, store, dialogue: Dialogue);
        LiveInspection = new TinyFarmOblivionLiveSurfaces(Host);
        MenuSaveAvailable = HasSave;
    }

    public TinyFarmChapter Chapter { get; }
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
    private string SaveSlot => Chapter switch
    {
        TinyFarmChapter.Supper => "sleeping-spring-a4",
        TinyFarmChapter.Shipping => "sleeping-spring-a3",
        TinyFarmChapter.Crafting => "sleeping-spring-a2",
        _ => "sleeping-spring-gate-a",
    };
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
    public bool IsModalMenu => IsAgentMenu || Screen is TinyFarmScreen.Crafting or TinyFarmScreen.Title or TinyFarmScreen.Container;

    public void Start()
    {
        if (Screen == TinyFarmScreen.Title)
        {
            ShowOpeningStatus();
        }
        Screen = TinyFarmScreen.Playing;
    }

    /// <summary>The first hint of a fresh game.</summary>
    public void ShowOpeningStatus()
    {
        Status = "A turnip is ready. Harvest it, make broth at home, then follow the path to Old Burrow.";
    }

    /// <summary>Show the title backdrop again (for example after the host returns to its title menu).</summary>
    public void ShowTitle()
    {
        Screen = TinyFarmScreen.Title;
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
                    OpenPause();
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
        if (!CapturesGameplay)
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
        int y = playing ? -Math.Sign(move.Y) : 0;
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
        }
        else
        {
            AcceptedActions++;
        }
        Dialogue.TryBeginFrom(step);
        Status = DescribeResult(intent, result);
        if (State.Slice is not null && result.Status == IntentResultStatus.Accepted
            && result.Events.Any(item => item.Kind == GameEventKind.DayStarted))
        {
            if (Save())
            {
                Status = "Morning. Your garden has grown. Rest saved your progress.";
            }
        }
        ApplyCraftingFeedback(intent, result);
        ApplyContainerFeedback(result);
        CheckCompletion();
        return step;
    }

    public void ApplyDialogue(TinyFarmDialogueAction action)
    {
        Dialogue.Apply(action);
        CheckCompletion();
    }

    private string DescribeResult(GameIntent intent, IntentResult result)
    {
        if (result.Status == IntentResultStatus.Rejected)
        {
            return result.Reason switch
            {
                IntentReason.MissingIngredient => "Broth needs one turnip. Harvest the cream bulb in your garden.",
                IntentReason.WrongWeapon or IntentReason.MissingSword => "Equip your sword in I / Equipment before striking. Finish an active dodge first.",
                IntentReason.MissingAxe => "Equip your axe in I / Equipment, then use tool 3 beside a tree.",
                IntentReason.WrongLocation when intent is SleepIntent => "Rest beside your bed at home.",
                IntentReason.WrongLocation => "This road is closed for now. Follow the woodland path east.",
                IntentReason.SupperNotReady => "A few supper jobs remain. Your journal shows what is missing.",
                _ => "Move closer and face the object. E interacts; K uses your selected tool.",
            };
        }
        if (intent is SleepIntent)
        {
            return "Morning. Watered crops have grown. Your garden is waiting.";
        }
        if (intent is EatIntent && result.Status == IntentResultStatus.Accepted)
        {
            return "Warm broth restores health. Ready for another try.";
        }
        if (result.Events.Any(item => item.Kind == GameEventKind.RecipeLearned && item.Recipe == TinyFarmIds.SauteedHenOfTheWoodsRecipe))
        {
            return "Supper with Mara: the river road is open. Mushrooms, mint and a seed for tomorrow.";
        }
        return result.Events.LastOrDefault()?.Kind switch
        {
            GameEventKind.DayStarted => "Morning. Watered crops have grown. Your garden is waiting.",
            GameEventKind.PlotWatered => "Watered. Rest in your bed tonight, then return to your garden.",
            GameEventKind.SceneEntered => OpeningSceneStatus(),
            GameEventKind.CropPlanted => "Your seed is planted. E waters it; sleep at home to grow it.",
            GameEventKind.CropHarvested when State.Slice?.OwnHarvest == true => "A turnip of your own. The stove turns it into healing broth.",
            GameEventKind.CropHarvested => "Starter turnip harvested. Bring it to the house stove for broth.",
            GameEventKind.RecipeCooked => "One turnip, one broth. R eats it when you need health.",
            GameEventKind.EnemyDefeated => "Old Burrow is quiet again. One fewer uninvited dinner guest.",
            _ => Status,
        };
    }

    private string OpeningSceneStatus()
    {
        if (State.CurrentScene == TinyFarmSceneIds.DungeonEntrance)
        {
            return "Watch the amber jump line. Dodge sideways; strike while the slime recovers.";
        }
        if (State.CurrentScene == TinyFarmSceneIds.Overworld)
        {
            return TinyFarmSupper.IsRequested(State) && !TinyFarmSupper.IsComplete(State)
                ? "The river road is open: Riverside lies along the water."
                : "Follow the path across the bridge, then north to Old Burrow.";
        }
        if (State.CurrentScene == TinyFarmSceneIds.Riverside)
        {
            return "Hen-of-the-woods grows by the water; wild mint on the bank. E gathers.";
        }
        if (State.CurrentScene == TinyFarmSceneIds.Residence)
        {
            return State.Version >= TinyFarmState.CraftingSaveVersion
                ? "E opens the stove. Pick up the recipe card beside your bed, or experiment."
                : "The stove cooks broth. Your bed is beside the south wall.";
        }
        return "Home again. Watered crops grow after a night's rest.";
    }

    /// <summary>
    /// When hosted by the game shell, checkpoint saves (sleeping, being brought home at night) are handed to the shell
    /// instead of TinyFarm's own save slots. Return true when the checkpoint was accepted.
    /// </summary>
    public Func<bool>? CheckpointHost { get; set; }

    public bool Save(string? slot = null)
    {
        if (Host.Session.HasActiveCombat)
        {
            Status = "Finish the swing before saving.";
            return false;
        }
        if (CheckpointHost is not null && slot is null)
        {
            return CheckpointHost();
        }
        try
        {
            // Sleep checkpoints must follow any earlier background manual save to this slot.
            Task? earlierSave = pendingSave;
            pendingSave = null;
            earlierSave?.GetAwaiter().GetResult();
            string targetSlot = slot ?? SaveSlot;
            Persistence.Deliverance.SaveAsync(targetSlot, Persistence.CaptureSave(targetSlot)).GetAwaiter().GetResult();
            MenuSaveAvailable = HasSave;
            Status = "Saved. N continues your garden and adventure from here.";
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

    public bool Load(string? slot = null)
    {
        try
        {
            string targetSlot = slot ?? SaveSlot;
            LoadedSaveCandidate candidate = Persistence.Deliverance.LoadAsync(targetSlot,
                Persistence.GetLoadDefinitions(targetSlot), Persistence.GetLoadCompatibility(targetSlot)).GetAwaiter().GetResult();
            Persistence.CommitLoadedCandidate(targetSlot, candidate);
            AfterRestore();
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
                Status = "Saved. Your garden, world and conversation are safe. N continues from here.";
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
                AfterRestore();
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Status = "Could not continue: " + error.Message;
            }
            pendingLoad = null;
        }
    }

    /// <summary>Reset presentation after the world was replaced by a save (TinyFarm's own or the shell's).</summary>
    public void AfterRestore()
    {
        Screen = TinyFarmScreen.Playing;
        completionShown = State.Slice?.LoopComplete == true;
        supperShown = TinyFarmSupper.IsComplete(State);
        effectsScene = null;
        FeedbackEpoch++;
        Effects = NewEffects();
        PendingAudio.Clear();
        Status = "Welcome back. Everything is just where you left it.";
        SynchronizeScene();
        RestoreContainerPresentation();
    }

    /// <summary>The journal heading for <see cref="Objectives"/>.</summary>
    public string JournalTitle => SupperInProgress ? "SUPPER WITH MARA" : "THE SLEEPING SPRING";

    private bool SupperInProgress => TinyFarmSupper.IsRequested(State) && !TinyFarmSupper.IsComplete(State);

    /// <summary>A cheap change key for the journal (which list, and which entries are done).</summary>
    public int JournalProgress
    {
        get
        {
            if (SupperInProgress)
            {
                return 32
                    | (TinyFarmSupper.SeedPlanted(State) ? 1 : 0)
                    | (TinyFarmSupper.HasCookedMushrooms(State) ? 2 : 0)
                    | (TinyFarmSupper.HasMint(State) ? 4 : 0)
                    | (TinyFarmSupper.BurrowCleared(State) ? 8 : 0);
            }
            if (State.Slice is not TinyFarmSliceState slice)
            {
                return 0;
            }
            return 64
                | (slice.CookedBroth ? 1 : 0)
                | (slice.Defeats > 0 ? 2 : 0)
                | (slice.ReturnedHome ? 4 : 0)
                | (slice.Slept ? 8 : 0)
                | (slice.OwnHarvest ? 16 : 0);
        }
    }

    /// <summary>The current journal: the opening loop, or Mara's supper while it is under way.</summary>
    public string[] Objectives()
    {
        string Mark(bool done, string text) => (done ? "[done] " : "[  ] ") + text;
        if (SupperInProgress)
        {
            return
            [
                Mark(TinyFarmSupper.SeedPlanted(State), "Plant a turnip for tomorrow / 1 + K"),
                Mark(TinyFarmSupper.HasCookedMushrooms(State), "Riverside mushrooms, sauteed at your stove"),
                Mark(TinyFarmSupper.HasMint(State), "Wild mint from the riverbank / E"),
                Mark(TinyFarmSupper.BurrowCleared(State), "One fewer slime in Old Burrow"),
                Mark(false, "Bring supper to Mara / E"),
            ];
        }
        if (State.Slice is not TinyFarmSliceState slice)
        {
            return [];
        }
        return
        [
            Mark(slice.CookedBroth, State.Version >= TinyFarmState.CraftingSaveVersion ? "Harvest a turnip; E opens your stove to cook soup" : "Harvest the starter turnip; E cooks broth at home"),
            Mark(slice.Defeats > 0, "Follow the east path; clear Old Burrow"),
            Mark(slice.ReturnedHome, "Return home from your adventure"),
            Mark(slice.Slept, "Plant with 1 + K, water with E, sleep in your bed"),
            Mark(slice.OwnHarvest, "Return in the morning and harvest your own turnip"),
        ];
    }

    private void CheckCompletion()
    {
        if (TinyFarmSupper.IsComplete(State) && !Dialogue.IsActive && !supperShown)
        {
            supperShown = true;
            Status = "Supper with Mara. The stove is warm and you are rested. Tomorrow can wait.";
            return;
        }
        if (State.Slice is { LoopComplete: true } && !completionShown)
        {
            completionShown = true;
            Status = "Home, garden, adventure. Opening loop complete. Stay a little longer.";
        }
    }

    private static TinyFarmDefinitions LoadDefinitions(TinyFarmChapter chapter) => chapter switch
    {
        TinyFarmChapter.Opening => TinyFarmSliceContent.Load(),
        TinyFarmChapter.Crafting => TinyFarmCraftingContent.Load(),
        TinyFarmChapter.Shipping => TinyFarmShippingContent.Load(),
        TinyFarmChapter.Supper => TinyFarmSupperContent.Load(),
        _ => throw new ArgumentOutOfRangeException(nameof(chapter)),
    };

    private static TinyFarmState StartState(TinyFarmChapter chapter, TinyFarmDefinitions definitions) => chapter switch
    {
        TinyFarmChapter.Opening => TinyFarmSliceContent.Start(definitions),
        TinyFarmChapter.Crafting => TinyFarmCraftingContent.Start(definitions),
        TinyFarmChapter.Shipping => TinyFarmShippingContent.Start(definitions),
        TinyFarmChapter.Supper => TinyFarmSupperContent.Start(definitions),
        _ => throw new ArgumentOutOfRangeException(nameof(chapter)),
    };

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
        foreach (IntentResult result in results.Where(result => result.Events.Any(item => item.Kind == GameEventKind.ShipmentCollected)))
        {
            ApplyContainerFeedback(result);
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
