using Aurelian.Audio;
using Aurelian.Composition;
using Aurelian.Games;
using Aurelian.Profile.Graphics;
using Aurelian.Rendering.Contracts.Shaders;
using Copeland.Profile;
using System.Numerics;
using Copeland.TS.Profiles;
using InputMan.Core;
using Machina.Runtime.Input;
using TinyFarm.Core;
using TinyFarm.InputMan;
using TinyFarm.Native;
using TinyFarm.Runtime;
using Controls = TinyFarm.InputMan.GameControls;

namespace TinyFarm;

/// <summary>
/// TinyFarm: The Sleeping Spring on the Aurelian game shell. The shell supplies the window, title, pause, settings,
/// rebinding, saves and playtesting. This class adapts TinyFarm's existing simulation host, dialogue, menus and
/// presentation: input becomes TinyFarm's InputMan frames, the world and HUD draw as two native layers, and the world
/// state is saved as TinyFarm's semantic snapshot.
/// </summary>
public sealed class TinyFarmApp : IGame
{
    private readonly TinyFarmGame game;
    private readonly TinyFarmSemanticSaveSnapshot initial;
    private readonly TinyFarmNativeUi ui;
    private readonly string assetDirectory;
    private GameContext? context;
    private NativeGameGraphics? graphics;
    private TinyFarmWorldPresenter? world;
    private TinyFarmNativeOverlay? overlay;
    private TinyFarmPortrait? portrait;
    private TinyFarmFrame frame;
    private TinyFarmPresentationLayout layout = new(1920, 1080, Legacy: false, Opening: true);
    private Vector2 lastPointer = new(-1, -1);
    private bool musicStarted;
    private long uiSounds;

    public TinyFarmApp(string? assetDirectory = null)
    {
        this.assetDirectory = assetDirectory ?? Path.Combine(AppContext.BaseDirectory, "Assets");
        // The full game: the opening, the stove, the shipping chest and supper with Mara.
        game = new TinyFarmGame(new TinyFarmMemorySaveStore(), TinyFarmChapter.Supper)
        {
            CheckpointHost = RequestCheckpoint,
        };
        game.Presentation.HostMenus = true;
        initial = game.Persistence.CaptureSnapshot();
        ui = new TinyFarmNativeUi(game);
        frame = TinyFarmFrameProjector.Project(game.State, game.Definitions);
        Saves = new GameSaveSchema<TinyFarmSemanticSaveSnapshot>(TinyFarmDeliverancePersistence.SnapshotJson,
            "tinyfarm.sleeping-spring|" + game.Definitions.Identity,
            game.Persistence.CaptureSnapshot,
            game.Persistence.Validate,
            Restore);
    }

    public GameManifest Manifest { get; } = new("tinyfarm", "TINYFARM", TinyFarmShellInput.Layout)
    {
        Tagline = "THE SLEEPING SPRING",
        ControlsHint = "WASD MOVE   E / RIGHT CLICK USE   K / LEFT CLICK TOOL   J SWORD   SPACE DODGE   I POCKETS",
        DefaultDisplay = new GameDisplaySettings(1920, 1080),
    };

    public GameSaveSchema Saves { get; }

    /// <summary>TinyFarm's game logic, for tests and inspection.</summary>
    public TinyFarmGame Game => game;

    public IReadOnlyCollection<string> InputContexts
    {
        get
        {
            if (game.IsAgentMenu && game.Menus.SearchFocused)
            {
                return [TinyFarmShellInput.Text];
            }
            if (game.Dialogue.IsActive)
            {
                return [TinyFarmShellInput.Dialogue];
            }
            if (game.CapturesGameplay)
            {
                return [TinyFarmShellInput.Menu];
            }
            return [GameInputContext.Play];
        }
    }

    public void Start(GameContext context)
    {
        this.context = context;
        TinyFarmAudio.Register(context.AudioResources);
    }

    public void NewGame()
    {
        Restore(initial);
        game.ShowOpeningStatus();
    }

    public void OnFrame(GameFrame frame)
    {
        // Behind the shell's title menu, show TinyFarm's title painting rather than the last frame of play.
        if (frame.Screen == "title" && game.Screen != TinyFarmScreen.Title)
        {
            game.ShowTitle();
        }
        if (!musicStarted && context is not null)
        {
            musicStarted = true;
            context.Audio.Play(new TinyFarmAudioProjector().FarmMusic(new AudioEventId("tinyfarm.music")) with { Priority = 100 });
        }
    }

    public void FixedUpdate(GameTick tick)
    {
        // The cursor and typing act on TinyFarm's screens directly; keys become TinyFarm's InputMan frame.
        ApplyPointer(tick.Input.Pointer);
        ApplyTyping(tick.Input.Typing);
        var input = TinyFarmShellInput.Frame(tick.Input, tick.Index, tick.Seconds, WheelHotbar(tick.Input.Pointer));
        string? dialogueBefore = game.Dialogue.Presentation?.OperationId;
        TinyFarmScreen screenBefore = game.Screen;
        int feedbackEpoch = game.FeedbackEpoch;
        game.Handle(input);
        // A restore inside Handle starts a new feedback epoch; that tick's input belongs to the old world.
        if (feedbackEpoch == game.FeedbackEpoch)
        {
            game.Advance(TimeSpan.FromSeconds(tick.Seconds), input, focused: true);
        }
        if (dialogueBefore != game.Dialogue.Presentation?.OperationId || screenBefore != game.Screen)
        {
            context?.PlaySound(TinyFarmAudio.Confirm.Value, 0.3f, AudioBusId.UI);
            uiSounds++;
        }
        while (game.PendingAudio.TryDequeue(out AudioCue? cue))
        {
            context?.Audio.Play(cue);
        }
    }

    public GameView? View(GameViewRequest request)
    {
        Resize(request.Width, request.Height);
        frame = TinyFarmFrameProjector.Project(game.State, game.Definitions);
        // 2D: the world and HUD are native layers.
        return null;
    }

    /// <summary>
    /// "walk STAGE" plays one stage of the supper chapter through ordinary resolver intents (the qualification
    /// walkthrough), so playtests can reach later scenes: burrow, request, riverside, gather, cook, mara (leaves Mara's
    /// conversation open) and finish.
    /// </summary>
    public bool Command(string command)
    {
        string[] words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length != 2 || words[0] != "walk")
        {
            return false;
        }
        var walkthrough = new TinyFarmWalkthrough(game);
        switch (words[1])
        {
            case "burrow":
                game.Start();
                walkthrough.TalkToMara();
                walkthrough.FinishDialogue();
                walkthrough.ClearOldBurrow();
                break;
            case "request":
                walkthrough.TalkToMara();
                walkthrough.FinishDialogue();
                break;
            case "riverside":
                walkthrough.PlantForTomorrow();
                walkthrough.GoToRiverside();
                break;
            case "gather":
                walkthrough.GatherAndReturnHome();
                break;
            case "cook":
                walkthrough.CookAtHome();
                break;
            case "mara":
                walkthrough.TalkToMara();
                break;
            case "finish":
                walkthrough.FinishDialogue();
                break;
            default:
                return false;
        }
        frame = TinyFarmFrameProjector.Project(game.State, game.Definitions);
        return true;
    }

    public (double X, double Y)? ActionPoint(string action, GameViewRequest surface)
    {
        if (!game.IsModalMenu || !ui.ActionNames(frame).Contains(action, StringComparer.Ordinal))
        {
            return null;
        }
        var target = layout with { Width = surface.Width, Height = surface.Height };
        PointerPoint point = ui.ActionCenter(frame, action);
        double x = target.UiLeft + point.X * target.UiScale;
        double y = target.UiTop + point.Y * target.UiScale;
        return (x / surface.Width, y / surface.Height);
    }

    public void Observe(GameFacts facts)
    {
        TinyFarmState state = game.State;
        facts.Set("tinyfarm.screen", game.Screen.ToString().ToLowerInvariant());
        facts.Set("tinyfarm.scene", state.ActorScene(TinyFarmIds.Player).Scene.Value);
        facts.Set("tinyfarm.minute", state.Minute);
        facts.Set("tinyfarm.dialogue", game.Dialogue.IsActive);
        facts.Set("tinyfarm.status", game.Status);
        facts.Set("tinyfarm.accepted", game.AcceptedActions);
        facts.Set("tinyfarm.rejected", game.RejectedActions);
        facts.Set("tinyfarm.semantic-hash", TinyFarmSemanticHash.Compute(state));
        facts.Set("tinyfarm.search", game.Menus.Search);
        facts.Set("tinyfarm.search-focused", game.Menus.SearchFocused);
        facts.Set("tinyfarm.hotbar", state.SelectedHotbarSlot);
        if (state.Slice is TinyFarmSliceState slice)
        {
            facts.Set("tinyfarm.broth-cooked", slice.CookedBroth);
            facts.Set("tinyfarm.defeats", slice.Defeats);
            facts.Set("tinyfarm.returned-home", slice.ReturnedHome);
            facts.Set("tinyfarm.slept", slice.Slept);
            facts.Set("tinyfarm.own-harvest", slice.OwnHarvest);
        }
        facts.Set("tinyfarm.supper", TinyFarmSupper.IsComplete(state) ? "complete" : TinyFarmSupper.IsRequested(state) ? "requested" : "not-started");
        facts.Set("tinyfarm.journal", game.JournalTitle);
    }

    public void AttachGraphics(NativeGameGraphics graphics)
    {
        this.graphics = graphics;
        graphics.Clear = new(0.11f, 0.16f, 0.12f, 1);
        GameAssets assets = context?.Assets ?? new GameAssets();
        TinyFarmSpriteAtlas atlas = TinyFarmSpriteAtlas.Load(Path.Combine(assetDirectory, "M11", "tinyfarm-sprite-atlas-source.png"));
        TinyFarmM24Assets painterly = TinyFarmM24Assets.Load(Path.Combine(assetDirectory, "m24"), legacy: false);
        world = new TinyFarmWorldPresenter(new LayerId("tinyfarm-world"), graphics.Plant,
            assets.Shader("AnalyticShape2D.v.ts"), assets.Shader("SoftShockwave.v.ts"),
            TinyFarmShaders.Compile(Path.Combine(assetDirectory, "Shaders", "ForwardTextured.v.ts")),
            TinyFarmShaders.Compile(Path.Combine(assetDirectory, "Shaders", "PainterlyWater.v.ts")),
            atlas, painterly, game, () => frame, () => layout);

        string profilePath = Path.Combine(assetDirectory, "M19", "mossward-tree.profile.tsx");
        ProfileCompositionCompilationResult profile = ProfileTsxCompiler.CompileComposition(File.ReadAllText(profilePath), profilePath);
        if (!profile.Success)
        {
            throw new InvalidOperationException(string.Join("; ", profile.Diagnostics.Select(item => item.Message)));
        }
        ProfileNativeCompileResult profileNative = ProfileNativeCompiler.Compile(profile.Composition!, profilePath);
        if (!profileNative.Success)
        {
            throw new InvalidOperationException(string.Join("; ", profileNative.Diagnostics.Select(item => item.Message)));
        }
        TinyFarmNativeUiFont font = TinyFarmNativeUiFont.Create(Path.Combine(assetDirectory, "SpaceMono-Regular.ttf"));
        overlay = new TinyFarmNativeOverlay(new LayerId("tinyfarm-hud"), graphics.Plant,
            assets.Shader("AnalyticShape2D.v.ts"), assets.Shader("MsdfText.v.ts"), assets.Shader("ProfileMsdf.v.ts"),
            font, profileNative.Resource!, () => ui.Resources(frame), game, () => layout, () => world.LastCamera);
        portrait = new TinyFarmPortrait(new LayerId("tinyfarm-portrait"), graphics.Plant,
            TinyFarmShaders.Compile(Path.Combine(assetDirectory, "Shaders", "ForwardTextured.v.ts")), game,
            Path.Combine(assetDirectory, "mara-dialogue.png"), () => layout);
        graphics.Layers.Add(world, 0);
        graphics.Layers.Add(portrait, 50);
        graphics.Layers.Add(overlay, 100);
    }

    public void DetachGraphics()
    {
        if (graphics is not null)
        {
            if (overlay is not null)
            {
                graphics.Layers.Remove(overlay.Layer);
            }
            if (portrait is not null)
            {
                graphics.Layers.Remove(portrait.Layer);
            }
            if (world is not null)
            {
                graphics.Layers.Remove(world.Layer);
            }
        }
        overlay = null;
        portrait = null;
        world = null;
        graphics = null;
    }

    public void Dispose()
    {
    }

    private void Resize(int width, int height)
    {
        if (width > 0 && height > 0 && (width != layout.Width || height != layout.Height))
        {
            layout = layout with { Width = width, Height = height };
        }
    }

    /// <summary>Hand the cursor to TinyFarm's menu hit-testing, in its 1280x720 menu space.</summary>
    private void ApplyPointer(GamePointer pointer)
    {
        if (!pointer.Present)
        {
            return;
        }
        // Headless sessions never call View; the pointer still says which surface its pixels belong to.
        Resize((int)pointer.Surface.X, (int)pointer.Surface.Y);
        var point = new PointerPoint((pointer.Position.X - layout.UiLeft) / layout.UiScale,
            (pointer.Position.Y - layout.UiTop) / layout.UiScale);
        bool moved = lastPointer != pointer.Position;
        lastPointer = pointer.Position;
        bool pressed = pointer.WasPressed(GamePointerButton.Primary);
        bool released = pointer.WasReleased(GamePointerButton.Primary);
        TinyFarmMenuInputRouter.Pointer(game, ui, new TinyFarmPointerInput(point, moved, pressed, released,
            pointer.Wheel.X, pointer.Wheel.Y));
    }

    private void ApplyTyping(IReadOnlyList<GameTyping> typing)
    {
        foreach (GameTyping item in typing)
        {
            switch (item.Edit)
            {
                case GameTextEdit.Backspace:
                    game.EditMenuSearch(false);
                    break;
                case GameTextEdit.Delete:
                    game.EditMenuSearch(true);
                    break;
                case GameTextEdit.Tab:
                    if (game.IsAgentMenu)
                    {
                        game.Menus.SearchFocused = !game.Menus.SearchFocused;
                    }
                    break;
                default:
                    game.EnterMenuText(item.Text);
                    break;
            }
        }
    }

    /// <summary>In free play the wheel steps through the four tool slots.</summary>
    private ActionId[] WheelHotbar(GamePointer pointer)
    {
        if (game.CapturesGameplay || pointer.Wheel.Y == 0)
        {
            return [];
        }
        int step = pointer.Wheel.Y > 0 ? -1 : 1;
        int slot = (game.State.SelectedHotbarSlot - 1 + step + 4) % 4 + 1;
        ActionId[] slots = [Controls.Hotbar1, Controls.Hotbar2, Controls.Hotbar3, Controls.Hotbar4];
        return [slots[slot - 1]];
    }

    private void Restore(TinyFarmSemanticSaveSnapshot snapshot)
    {
        game.Persistence.Restore(snapshot);
        game.AfterRestore();
        frame = TinyFarmFrameProjector.Project(game.State, game.Definitions);
    }

    private bool RequestCheckpoint()
    {
        if (context is null)
        {
            return false;
        }
        context.RequestSave();
        return true;
    }
}
