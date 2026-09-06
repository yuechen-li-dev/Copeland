using Aurelian.Audio;
using Aurelian.GameHost;
using Ariadne.OptFlow.Presentation;
using InputMan.Aurelian;
using InputMan.Core;

namespace Aurelian.Ariadne.VnDemo;

public enum RenScreen
{
    MainMenu,
    Game,
    PauseMenu,
    SaveMenu,
    LoadMenu,
    Settings,
    End,
}

public abstract record RenIntent;
public sealed record NavigateIntent(int Delta) : RenIntent;
public sealed record AdjustSettingIntent(int Delta) : RenIntent;
public sealed record ConfirmIntent : RenIntent;
public sealed record BackIntent : RenIntent;
public sealed record AdvanceDialogueIntent : RenIntent;
public sealed record ChooseDialogueOptionIntent(string ChoiceId) : RenIntent;
public sealed record NewGameIntent : RenIntent;
public sealed record OpenPauseMenuIntent : RenIntent;
public sealed record OpenSaveMenuIntent : RenIntent;
public sealed record OpenLoadMenuIntent : RenIntent;
public sealed record OpenSettingsIntent : RenIntent;
public sealed record SaveSlotIntent(int SlotNumber) : RenIntent;
public sealed record LoadSlotIntent(int SlotNumber) : RenIntent;
public sealed record ReturnToMainMenuIntent : RenIntent;
public sealed record QuitIntent : RenIntent;

public sealed record RenMenuEntry(string Id, string Label);

public sealed record RenAppState(
    RenScreen Screen,
    RenSettings Settings,
    IReadOnlyList<RenSaveSlotMetadata> SaveSlots,
    int SelectedItem,
    bool ExitRequested,
    string Notice);

public sealed record RenPresentationSnapshot(
    RenScreen Screen,
    string Title,
    string Subtitle,
    IReadOnlyList<RenMenuEntry> MenuEntries,
    int SelectedItem,
    DialoguePresentationSnapshot? Dialogue,
    string BackgroundAsset,
    string? PortraitAsset,
    RenSettings Settings,
    IReadOnlyList<RenSaveSlotMetadata> SaveSlots,
    string Notice);

public static class RenControls
{
    public static readonly ActionMapId Ui = new("RenC.UI");
    public static readonly ActionId Up = new("RenC.Up");
    public static readonly ActionId Down = new("RenC.Down");
    public static readonly ActionId Left = new("RenC.Left");
    public static readonly ActionId Right = new("RenC.Right");
    public static readonly ActionId Confirm = new("RenC.Confirm");
    public static readonly ActionId Back = new("RenC.Back");
    public static readonly ActionId QuickSave = new("RenC.QuickSave");
    public static readonly ActionId QuickLoad = new("RenC.QuickLoad");

    public static InputProfile CreateProfile()
    {
        return Input.Profile(
        [
            Input.Map(Ui, 100,
            [
                Bind.Action(Controls.Key(KeyboardKey.ArrowUp), Up, consume: ConsumeMode.ControlOnly),
                Bind.Action(Controls.Key(KeyboardKey.ArrowDown), Down, consume: ConsumeMode.ControlOnly),
                Bind.Action(Controls.Key(KeyboardKey.ArrowLeft), Left, consume: ConsumeMode.ControlOnly),
                Bind.Action(Controls.Key(KeyboardKey.ArrowRight), Right, consume: ConsumeMode.ControlOnly),
                Bind.Action(Controls.Key(KeyboardKey.Enter), Confirm, consume: ConsumeMode.ControlOnly),
                Bind.Action(Controls.Key(KeyboardKey.Space), Confirm, consume: ConsumeMode.ControlOnly),
                Bind.Action(Controls.Key(KeyboardKey.Escape), Back, consume: ConsumeMode.ControlOnly),
                Bind.Action(Controls.Key(KeyboardKey.F), QuickSave, consume: ConsumeMode.ControlOnly),
                Bind.Action(Controls.Key(KeyboardKey.I), QuickLoad, consume: ConsumeMode.ControlOnly),
            ]),
        ]);
    }
}

public sealed class RenApp : IDisposable
{
    private readonly VnPersistence persistence;
    private readonly RenSettingsStore settingsStore;
    private readonly RenAudioSettingsProjection audio;
    private readonly InputManEngine inputEngine;
    private readonly AurelianInputAdapter inputAdapter;
    private IReadOnlyList<RenSaveSlotMetadata> saveSlots = [];
    private RenScreen screen = RenScreen.MainMenu;
    private RenScreen returnScreen = RenScreen.MainMenu;
    private int selectedItem;
    private bool exitRequested;
    private string notice = "READY";
    private ulong inputSequence;

    public RenApp(string saveDirectory, string settingsPath)
    {
        persistence = new VnPersistence(saveDirectory);
        settingsStore = new RenSettingsStore(settingsPath);
        Settings = settingsStore.Load();
        audio = new RenAudioSettingsProjection();
        audio.Apply(Settings);
        inputEngine = new InputManEngine(RenControls.CreateProfile());
        inputAdapter = new AurelianInputAdapter(inputEngine);
        inputAdapter.SetContexts(RenControls.Ui);
        RefreshSlots();
    }

    public VnSession? ActiveGame { get; private set; }
    public RenSettings Settings { get; private set; }
    public AudioRuntimeFacts AudioFacts => audio.Facts;
    public bool ExitRequested => exitRequested;

    public RenAppState State => new(
        screen,
        Settings,
        saveSlots,
        selectedItem,
        exitRequested,
        notice);

    public RenPresentationSnapshot Presentation
    {
        get
        {
            DialoguePresentationSnapshot? dialogue =
                screen == RenScreen.Game ? ActiveGame?.Presentation : null;
            string? portrait = null;
            if (dialogue?.OperationId is string operationId)
            {
                portrait = SunkillDialogue.Get(operationId).PortraitKey;
            }

            (string title, string subtitle) = Heading(screen);
            return new RenPresentationSnapshot(
                screen,
                title,
                subtitle,
                BuildMenuEntries(),
                selectedItem,
                dialogue,
                "sunkill-bunker.png",
                portrait is null ? null : "sunkill-oppenheimer.png",
                Settings,
                saveSlots,
                notice);
        }
    }

    public void Dispatch(RenIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        switch (intent)
        {
            case NavigateIntent navigate:
                Navigate(navigate.Delta);
                break;
            case AdjustSettingIntent adjust:
                AdjustSelectedSetting(adjust.Delta);
                break;
            case ConfirmIntent:
                Confirm();
                break;
            case BackIntent:
                Back();
                break;
            case AdvanceDialogueIntent:
                AdvanceDialogue();
                break;
            case ChooseDialogueOptionIntent choose:
                Choose(choose.ChoiceId);
                break;
            case NewGameIntent:
                StartNewGame();
                break;
            case OpenPauseMenuIntent:
                OpenPauseMenu();
                break;
            case OpenSaveMenuIntent:
                OpenMenu(RenScreen.SaveMenu);
                break;
            case OpenLoadMenuIntent:
                OpenMenu(RenScreen.LoadMenu);
                break;
            case OpenSettingsIntent:
                OpenMenu(RenScreen.Settings);
                break;
            case SaveSlotIntent save:
                Save(save.SlotNumber);
                break;
            case LoadSlotIntent load:
                Load(load.SlotNumber);
                break;
            case ReturnToMainMenuIntent:
                ReturnToMainMenu();
                break;
            case QuitIntent:
                exitRequested = true;
                notice = "EXIT REQUESTED";
                break;
        }

        ClampSelection();
    }

    public void Activate(string entryId)
    {
        RenIntent intent = entryId switch
        {
            "new-game" => new NewGameIntent(),
            "resume" => new BackIntent(),
            "save" => new OpenSaveMenuIntent(),
            "load" => new OpenLoadMenuIntent(),
            "settings" => new OpenSettingsIntent(),
            "main-menu" => new ReturnToMainMenuIntent(),
            "quit" => new QuitIntent(),
            "back" => new BackIntent(),
            "master-volume" => new AdjustSettingIntent(1),
            "music-volume" => new AdjustSettingIntent(1),
            "sfx-volume" => new AdjustSettingIntent(1),
            "slot-1" when screen == RenScreen.SaveMenu => new SaveSlotIntent(1),
            "slot-2" when screen == RenScreen.SaveMenu => new SaveSlotIntent(2),
            "slot-3" when screen == RenScreen.SaveMenu => new SaveSlotIntent(3),
            "slot-1" => new LoadSlotIntent(1),
            "slot-2" => new LoadSlotIntent(2),
            "slot-3" => new LoadSlotIntent(3),
            _ => throw new InvalidOperationException($"Unknown RenC menu action '{entryId}'."),
        };
        Dispatch(intent);
    }

    public void Press(KeyboardKey key)
    {
        inputAdapter.RecordButton(Controls.Key(key), true);
        inputAdapter.BeginFrame(Frame());
        ApplyInput(inputAdapter.CurrentFrame);
        inputAdapter.RecordButton(Controls.Key(key), false);
        inputAdapter.BeginFrame(Frame());
    }

    public void Dispose()
    {
        ActiveGame?.Dispose();
        inputAdapter.Dispose();
        audio.Dispose();
    }

    private AurelianHostFrame Frame()
    {
        inputSequence++;
        return new AurelianHostFrame(
            inputSequence,
            TimeSpan.FromMilliseconds(16),
            TimeSpan.FromMilliseconds(inputSequence * 16));
    }

    private void ApplyInput(InputFrame frame)
    {
        if (frame.WasPressed(RenControls.Up))
        {
            Dispatch(new NavigateIntent(-1));
        }

        if (frame.WasPressed(RenControls.Down))
        {
            Dispatch(new NavigateIntent(1));
        }

        if (frame.WasPressed(RenControls.Left))
        {
            Dispatch(new AdjustSettingIntent(-1));
        }

        if (frame.WasPressed(RenControls.Right))
        {
            Dispatch(new AdjustSettingIntent(1));
        }

        if (frame.WasPressed(RenControls.Back))
        {
            Dispatch(new BackIntent());
        }

        if (frame.WasPressed(RenControls.QuickSave) && ActiveGame is not null)
        {
            Dispatch(new SaveSlotIntent(1));
        }

        if (frame.WasPressed(RenControls.QuickLoad))
        {
            Dispatch(new LoadSlotIntent(1));
        }

        if (frame.WasPressed(RenControls.Confirm))
        {
            Dispatch(new ConfirmIntent());
        }
    }

    private void Navigate(int delta)
    {
        if (screen == RenScreen.Game
            && ActiveGame?.Presentation.OperationKind == DialoguePresentationOperationKind.Choice)
        {
            ActiveGame.MoveChoice(delta);
            return;
        }

        int count = BuildMenuEntries().Count;
        if (count > 0)
        {
            selectedItem = (selectedItem + delta + count) % count;
        }
    }

    private void Confirm()
    {
        if (screen == RenScreen.Game)
        {
            AdvanceDialogue();
            return;
        }

        IReadOnlyList<RenMenuEntry> entries = BuildMenuEntries();
        if (entries.Count > 0)
        {
            Activate(entries[selectedItem].Id);
        }
    }

    private void AdvanceDialogue()
    {
        if (screen != RenScreen.Game || ActiveGame is null)
        {
            return;
        }

        ActiveGame.Advance();
        if (ActiveGame.IsTerminal)
        {
            screen = RenScreen.End;
            selectedItem = 0;
            notice = "THE SUN HAS FILED ITS REPORT";
        }
    }

    private void Choose(string choiceId)
    {
        if (screen != RenScreen.Game || ActiveGame is null)
        {
            return;
        }

        ActiveGame.Choose(choiceId);
        if (ActiveGame.IsTerminal)
        {
            screen = RenScreen.End;
        }
    }

    private void StartNewGame()
    {
        ActiveGame?.Dispose();
        ActiveGame = new VnSession();
        screen = RenScreen.Game;
        returnScreen = RenScreen.Game;
        selectedItem = 0;
        notice = "DAWN ENGINE ONLINE";
    }

    private void OpenPauseMenu()
    {
        if (screen == RenScreen.Game && ActiveGame is not null)
        {
            screen = RenScreen.PauseMenu;
            returnScreen = RenScreen.Game;
            selectedItem = 0;
        }
    }

    private void OpenMenu(RenScreen target)
    {
        if (target is not (RenScreen.SaveMenu or RenScreen.LoadMenu or RenScreen.Settings))
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        returnScreen = screen is RenScreen.Game or RenScreen.PauseMenu
            ? RenScreen.Game
            : RenScreen.MainMenu;
        screen = target;
        selectedItem = 0;
        if (target is RenScreen.SaveMenu or RenScreen.LoadMenu)
        {
            RefreshSlots();
        }
    }

    private void Back()
    {
        switch (screen)
        {
            case RenScreen.Game:
                OpenPauseMenu();
                break;
            case RenScreen.PauseMenu:
                screen = RenScreen.Game;
                selectedItem = 0;
                break;
            case RenScreen.SaveMenu:
            case RenScreen.LoadMenu:
            case RenScreen.Settings:
                screen = returnScreen;
                selectedItem = 0;
                break;
            case RenScreen.End:
                ReturnToMainMenu();
                break;
        }
    }

    private void Save(int slotNumber)
    {
        if (ActiveGame is null)
        {
            notice = "NO ACTIVE TEST TO SAVE";
            return;
        }

        persistence.SaveAsync(slotNumber, ActiveGame).GetAwaiter().GetResult();
        notice = $"SLOT {slotNumber} OVERWRITTEN";
        RefreshSlots();
    }

    private void Load(int slotNumber)
    {
        var candidate = new VnSession();
        try
        {
            persistence.LoadAsync(slotNumber, candidate).GetAwaiter().GetResult();
            ActiveGame?.Dispose();
            ActiveGame = candidate;
            screen = RenScreen.Game;
            returnScreen = RenScreen.Game;
            selectedItem = 0;
            notice = $"SLOT {slotNumber} RESTORED";
        }
        catch (FileNotFoundException)
        {
            candidate.Dispose();
            notice = $"SLOT {slotNumber} IS EMPTY";
        }
        catch (IOException)
        {
            candidate.Dispose();
            notice = $"SLOT {slotNumber} IS INVALID";
        }
    }

    private void AdjustSelectedSetting(int delta)
    {
        if (screen != RenScreen.Settings || delta == 0)
        {
            return;
        }

        RenSetting? setting = selectedItem switch
        {
            0 => RenSetting.MasterVolume,
            1 => RenSetting.MusicVolume,
            2 => RenSetting.SfxVolume,
            _ => null,
        };
        if (setting is null)
        {
            return;
        }

        Settings = Settings.Adjust(setting.Value, delta).Normalize();
        settingsStore.Save(Settings);
        audio.Apply(Settings);
        notice = "SETTINGS SAVED";
    }

    private void ReturnToMainMenu()
    {
        ActiveGame?.Dispose();
        ActiveGame = null;
        screen = RenScreen.MainMenu;
        returnScreen = RenScreen.MainMenu;
        selectedItem = 0;
        notice = "READY";
    }

    private void RefreshSlots()
    {
        saveSlots = persistence.ReadSlotMetadataAsync().GetAwaiter().GetResult();
    }

    private void ClampSelection()
    {
        int count = BuildMenuEntries().Count;
        selectedItem = count == 0 ? 0 : Math.Clamp(selectedItem, 0, count - 1);
    }

    private IReadOnlyList<RenMenuEntry> BuildMenuEntries()
    {
        return screen switch
        {
            RenScreen.MainMenu =>
            [
                new("new-game", "NEW GAME"),
                new("load", "LOAD"),
                new("settings", "SETTINGS"),
                new("quit", "QUIT"),
            ],
            RenScreen.PauseMenu =>
            [
                new("resume", "RESUME"),
                new("save", "SAVE"),
                new("load", "LOAD"),
                new("settings", "SETTINGS"),
                new("main-menu", "MAIN MENU"),
            ],
            RenScreen.SaveMenu => SlotEntries()
                .Append(new RenMenuEntry("back", "BACK"))
                .ToArray(),
            RenScreen.LoadMenu => SlotEntries()
                .Append(new RenMenuEntry("back", "BACK"))
                .ToArray(),
            RenScreen.Settings =>
            [
                new("master-volume", $"MASTER VOLUME     {Percent(Settings.MasterVolume)}"),
                new("music-volume", $"MUSIC VOLUME      {Percent(Settings.MusicVolume)}"),
                new("sfx-volume", $"SFX VOLUME        {Percent(Settings.SfxVolume)}"),
                new("back", "BACK"),
            ],
            RenScreen.End =>
            [
                new("main-menu", "RETURN TO MAIN MENU"),
            ],
            _ => [],
        };
    }

    private IEnumerable<RenMenuEntry> SlotEntries()
    {
        foreach (RenSaveSlotMetadata slot in saveSlots)
        {
            string detail = slot.Corrupt
                ? "CORRUPT"
                : slot.Available
                    ? $"{slot.LineLabel}  {slot.SavedAtUtc:MM-dd HH:mm}Z"
                    : "EMPTY";
            yield return new RenMenuEntry(
                slot.SlotId,
                $"SLOT {slot.SlotNumber}     {detail}");
        }
    }

    private static string Percent(float value)
    {
        return $"{Math.Round(value * 100):0} / 100";
    }

    private static (string Title, string Subtitle) Heading(RenScreen current)
    {
        return current switch
        {
            RenScreen.MainMenu => ("SUNKILL", "NIGHT HAD A GOOD RUN."),
            RenScreen.PauseMenu => ("TEST SUSPENDED", "THE SUN WILL WAIT. BRIEFLY."),
            RenScreen.SaveMenu => ("SAVE", "COMMIT THIS VERSION OF DAWN."),
            RenScreen.LoadMenu => ("LOAD", "RESTORE AN EARLIER MORNING."),
            RenScreen.Settings => ("SETTINGS", "CALIBRATE THE INEVITABLE."),
            RenScreen.End => ("END OF PROOF", "MORNING REMAINS UNDER REVIEW."),
            _ => ("SUNKILL", ""),
        };
    }
}
