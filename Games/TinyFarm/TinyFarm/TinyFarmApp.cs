using Aurelian.Audio;
using Aurelian.Composition;
using Aurelian.Games;
using Aurelian.Profile.Graphics;
using Aurelian.Rendering.Contracts.Shaders;
using Copeland.Profile;
using Copeland.TS.Profiles;
using TinyFarm.Core;
using TinyFarm.InputMan;
using TinyFarm.Native;
using TinyFarm.Runtime;

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
    private TinyFarmFrame frame;
    private TinyFarmPresentationLayout layout = new(1920, 1080, Legacy: false, Opening: true);
    private bool musicStarted;
    private long uiSounds;

    public TinyFarmApp(string? assetDirectory = null)
    {
        this.assetDirectory = assetDirectory ?? Path.Combine(AppContext.BaseDirectory, "Assets");
        // The full opening: garden, home, stove crafting and the shipping chest.
        game = new TinyFarmGame(new TinyFarmMemorySaveStore(), slice: true, crafting: true, shipping: true)
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
        ControlsHint = "WASD MOVE   E USE   J SWORD   SPACE DODGE   I POCKETS   ESC PAUSE",
        DefaultDisplay = new GameDisplaySettings(1920, 1080),
    };

    public GameSaveSchema Saves { get; }

    /// <summary>TinyFarm's game logic, for tests and inspection.</summary>
    public TinyFarmGame Game => game;

    public IReadOnlyCollection<string> InputContexts => game.Dialogue.IsActive
        ? [TinyFarmShellInput.Dialogue]
        : game.CapturesGameplay ? [TinyFarmShellInput.Menu] : [GameInputContext.Play];

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
        var input = TinyFarmShellInput.Frame(tick.Input, tick.Index, tick.Seconds);
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
        if (request.Width != layout.Width || request.Height != layout.Height)
        {
            layout = layout with { Width = request.Width, Height = request.Height };
        }
        frame = TinyFarmFrameProjector.Project(game.State, game.Definitions);
        // 2D: the world and HUD are native layers.
        return null;
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
        if (state.Slice is TinyFarmSliceState slice)
        {
            facts.Set("tinyfarm.broth-cooked", slice.CookedBroth);
            facts.Set("tinyfarm.defeats", slice.Defeats);
            facts.Set("tinyfarm.returned-home", slice.ReturnedHome);
            facts.Set("tinyfarm.slept", slice.Slept);
            facts.Set("tinyfarm.own-harvest", slice.OwnHarvest);
        }
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
        graphics.Layers.Add(world, 0);
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
            if (world is not null)
            {
                graphics.Layers.Remove(world.Layer);
            }
        }
        overlay = null;
        world = null;
        graphics = null;
    }

    public void Dispose()
    {
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
