using System.Text.Json.Serialization;
using Aurelian.Composition;
using Aurelian.Games;
using Aurelian.Machina;
using InputMan.Core;
using Machina.Presentation;

namespace Aurelian.Ariadne.VnDemo;

/// <summary>The dialogue world and its existing artwork, within the shared application shell.</summary>
public sealed class SunkillGame : IGame
{
    private VnSession world = new();
    private readonly GameSaveSchema saves;
    private VnMachinaLayer? ui;
    private NativeGameGraphics? graphics;
    private static readonly LayerId Background = new("sunkill.background");
    private static readonly LayerId Portrait = new("sunkill.portrait");

    public SunkillGame(string? legacyDirectory = null)
    {
        string directory = legacyDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SUNKILL", "Saves");
        saves = new GameSaveSchema<VnSessionCheckpoint>(SunkillShellJson.Default.VnSessionCheckpoint,
            "sunkill-dawn-engine-v1", () => world.Capture(), Validate, Restore)
        {
            Legacy = () => ImportLegacy(directory),
        };
    }

    public GameManifest Manifest { get; } = new("sunkill-vn", "SUNKILL", new GameInputLayout(
    [
        new("advance", "Advance dialogue", GameControl.Key(KeyboardKey.Enter), GameControl.Key(KeyboardKey.Space)),
        new("choice.up", "Previous choice", GameControl.Key(KeyboardKey.ArrowUp)),
        new("choice.down", "Next choice", GameControl.Key(KeyboardKey.ArrowDown)),
    ]) { Pointer = true })
    {
        Tagline = "THE DAWN ENGINE IS WAITING",
        ControlsHint = "ENTER / SPACE ADVANCE   ARROWS CHOOSE   CLICK DIALOGUE   ESC PAUSE / SAVE",
        DefaultDisplay = new(1280, 720),
    };

    public VnSession World => world;
    public GameSaveSchema Saves => saves;

    public void NewGame()
    {
        world.Dispose();
        world = new VnSession();
    }

    public void FixedUpdate(GameTick tick)
    {
        if (tick.Input.WasPressed("choice.up"))
        {
            world.MoveChoice(-1);
        }
        if (tick.Input.WasPressed("choice.down"))
        {
            world.MoveChoice(1);
        }
        if (tick.Input.WasPressed("advance"))
        {
            world.Advance();
        }
        var pointer = tick.Input.Pointer;
        if (pointer.Present && ui is not null)
        {
            var transform = MachinaViewportTransform.Create(1280, 720, (int)pointer.Surface.X, (int)pointer.Surface.Y);
            (double x, double y) = transform.ToLogical(pointer.Position.X, pointer.Position.Y);
            if (pointer.WasPressed(GamePointerButton.Primary))
            {
                ui.HandleInput(new LayerPointerButtonChanged(new(x, y), LayerPointerButton.Primary, true));
            }
            if (pointer.WasReleased(GamePointerButton.Primary))
            {
                ui.HandleInput(new LayerPointerButtonChanged(new(x, y), LayerPointerButton.Primary, false));
            }
        }
    }

    public void Start(GameContext context) => ui = new VnMachinaLayer(Presentation, Dispatch);

    public void OnFrame(GameFrame frame)
    {
        if (!frame.Playing)
        {
            ui?.CancelPointer();
        }
    }

    private RenPresentationSnapshot Presentation()
    {
        var dialogue = world.Presentation;
        string? portrait = dialogue.OperationId is { } operation ? SunkillDialogue.Get(operation).PortraitKey : null;
        return new(RenScreen.Game, "SUNKILL", "", [], 0, dialogue, "sunkill-bunker.png",
            portrait is null ? null : "sunkill-oppenheimer.png", RenSettings.Default, [], "");
    }

    private void Dispatch(RenIntent intent)
    {
        switch (intent)
        {
            case AdvanceDialogueIntent:
                world.Advance();
                break;
            case ChooseDialogueOptionIntent choice:
                world.Choose(choice.ChoiceId);
                break;
        }
    }

    public (double X, double Y)? ActionPoint(string action, GameViewRequest surface)
    {
        if (action != "ren.advance" && !world.Presentation.Choices.Any(choice => action == "ren.choice." + choice.Id))
        {
            return null;
        }
        var point = ui!.ActionCenter(action);
        var transform = MachinaViewportTransform.Create(1280, 720, surface.Width, surface.Height);
        (double x, double y) = transform.ToPhysical(point.X, point.Y);
        return (x / surface.Width, y / surface.Height);
    }

    public void BuildHud(GameHud hud)
    {
        if (world.IsTerminal)
        {
            hud.StatusBar("THE SUN HAS FILED ITS REPORT.   ESC FOR MAIN MENU");
        }
    }

    public void Observe(GameFacts facts)
    {
        facts.Set("hash", world.SemanticHash());
        facts.Set("operation", world.Presentation.OperationId ?? "terminal");
        facts.Set("terminal", world.IsTerminal);
        facts.Set("choice", world.SelectedChoiceIndex);
    }

    public void AttachGraphics(NativeGameGraphics graphics)
    {
        this.graphics = graphics;
        var background = VnNativeRenderer.LoadImage("sunkill-bunker.png", "sunkill-bunker", true);
        var portrait = VnNativeRenderer.LoadPortrait("sunkill-oppenheimer.png", "oppenheimer");
        var empty = VnNativeRenderer.Transparent("portrait-empty");
        var program = VnNativeRenderer.CompileShader(AppContext.BaseDirectory, "Assets/ForwardTexturedM3.v.ts", null);
        var atlas = VnNativeRenderer.LoadRawImage(ui!.Skin.Atlas.ResolvedImagePath, VnUiSkin.AtlasAssetId,
            ui.Skin.Atlas.Width, ui.Skin.Atlas.Height);
        graphics.Layers.Add(new TextureLayerPresenter(Background, graphics.Plant, program, () => background));
        graphics.Layers.Add(new TextureLayerPresenter(Portrait, graphics.Plant, program,
            () => Presentation().PortraitAsset is null ? empty : portrait), 50);
        graphics.Layers.Add(new VnUiLayerPresenter(graphics.Plant, program, ui, atlas,
            VnNativeRenderer.CreateSeamFixture(), () => VnNativeRenderer.Resource("machina-overlay", ui.Rgba8)), 100);
    }

    public void DetachGraphics()
    {
        graphics?.Layers.Remove(VnMachinaLayer.Id);
        graphics?.Layers.Remove(Portrait);
        graphics?.Layers.Remove(Background);
        graphics = null;
    }

    private static void Validate(VnSessionCheckpoint checkpoint)
    {
        if (checkpoint.DominatusChunks is null || checkpoint.DominatusChunks.Length == 0
            || checkpoint.DominatusChunks.Any(chunk => chunk is null || string.IsNullOrWhiteSpace(chunk.Id) || chunk.Payload is null)
            || checkpoint.SelectedChoiceIndex < 0)
        {
            throw new InvalidDataException("The dialogue checkpoint is incomplete.");
        }
        using var candidate = new VnSession();
        candidate.Restore(checkpoint);
        if (checkpoint.SelectedChoiceIndex >= Math.Max(1, candidate.Presentation.Choices.Count))
        {
            throw new InvalidDataException("Invalid dialogue choice index.");
        }
    }

    private void Restore(VnSessionCheckpoint checkpoint)
    {
        var candidate = new VnSession();
        candidate.Restore(checkpoint);
        world.Dispose();
        world = candidate;
    }

    private static VnSessionCheckpoint? ImportLegacy(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }
        using var candidate = new VnSession();
        try
        {
            new VnPersistence(directory).LoadAsync(1, candidate).GetAwaiter().GetResult();
            return candidate.Capture();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        ui?.Detach();
        world.Dispose();
    }
}

[JsonSerializable(typeof(VnSessionCheckpoint))]
internal partial class SunkillShellJson : JsonSerializerContext;
