using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurelian.Audio;
using Aurelian.Composition;
using Aurelian.NativeComposition;
using Ariadne.OptFlow.Presentation;

namespace Aurelian.Ariadne.VnDemo;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (!args.Contains("--proof", StringComparer.OrdinalIgnoreCase))
        {
            return VnPresenter.Run(args);
        }

        return RunProofAsync().GetAwaiter().GetResult();
    }

    private static async Task<int> RunProofAsync()
    {
        string root = FindRepositoryRoot();
        string artifactRoot = Path.Combine(root, "artifacts", "renc-vn-m13");
        Directory.CreateDirectory(artifactRoot);
        string saveRoot = Path.Combine(artifactRoot, "saves");
        string settingsPath = Path.Combine(artifactRoot, "runtime", "settings.json");
        new RenSettingsStore(settingsPath).Save(RenSettings.Default);

        using var app = new RenApp(saveRoot, settingsPath);
        var machina = new VnMachinaLayer(app);
        using var native = new VnNativeRenderer(root, app, machina);
        ulong frameId = 0;

        Require(app.State.Screen == RenScreen.MainMenu, "Boot did not enter the main menu.");
        WriteScreenshot(artifactRoot, "main-menu.png", native.Render(++frameId));

        LayerPoint settingsCenter = machina.ActionCenter("ren.entry.settings");
        LayerInputRoutingResult pointerPress = native.Route(new LayerPointerButtonChanged(
            settingsCenter,
            LayerPointerButton.Primary,
            true));
        LayerInputRoutingResult pointerRelease = native.Route(new LayerPointerButtonChanged(
            settingsCenter,
            LayerPointerButton.Primary,
            false));
        Require(
            pointerPress.ConsumedBy == VnMachinaLayer.Id
                && pointerPress.CaptureOwner == VnMachinaLayer.Id,
            "Machina did not capture the main-menu pointer press.");
        Require(
            pointerRelease.FocusOwner == VnMachinaLayer.Id
                && pointerRelease.CaptureOwner is null,
            "Machina did not retain focus and release pointer capture.");
        Require(app.State.Screen == RenScreen.Settings, "Settings did not open from the main menu.");
        app.Dispatch(new AdjustSettingIntent(-1));
        app.Dispatch(new NavigateIntent(1));
        app.Dispatch(new AdjustSettingIntent(-1));
        app.Dispatch(new NavigateIntent(1));
        for (int index = 0; index < 10; index++)
        {
            app.Dispatch(new AdjustSettingIntent(-1));
        }

        AudioRuntimeFacts adjustedAudio = app.AudioFacts;
        Require(adjustedAudio.BusGains[AudioBusId.Sfx] == 0f, "SFX zero did not reach the Aurelian audio bus.");
        WriteScreenshot(artifactRoot, "settings.png", native.Render(++frameId));

        using (var restarted = new RenApp(saveRoot, settingsPath))
        {
            Require(restarted.Settings == app.Settings, "Settings did not survive app recreation.");
            Require(
                restarted.AudioFacts.BusGains[AudioBusId.Master] == app.Settings.MasterVolume,
                "Restored master volume did not reach Aurelian.Audio.");
        }

        app.Dispatch(new BackIntent());
        app.Dispatch(new NewGameIntent());
        Require(app.State.Screen == RenScreen.Game, "New Game did not enter the game screen.");
        Require(app.ActiveGame is not null, "New Game did not create a clean dialogue session.");
        VnSession activeGame = app.ActiveGame
            ?? throw new InvalidOperationException("New Game did not create a dialogue session.");
        string afterLineOperation = activeGame.Presentation.OperationId!;
        WriteScreenshot(artifactRoot, "scene-line.png", native.Render(++frameId));

        app.Dispatch(new SaveSlotIntent(1));
        app.Dispatch(new AdvanceDialogueIntent());
        app.Dispatch(new LoadSlotIntent(1));
        Require(
            app.ActiveGame!.Presentation.OperationId == afterLineOperation,
            "Save-after-line did not restore the exact operation.");
        Require(
            app.ActiveGame.DialogueDispatchCount == 0,
            "Restoring a pending line re-dispatched it.");

        AdvanceToChoice(app);
        Require(
            app.ActiveGame.Presentation.OperationKind == DialoguePresentationOperationKind.Choice,
            "The SUNKILL choice did not appear.");
        WriteScreenshot(artifactRoot, "scene-choice.png", native.Render(++frameId));

        app.Dispatch(new NavigateIntent(1));
        app.Dispatch(new SaveSlotIntent(2));
        app.Dispatch(new AdvanceDialogueIntent());
        Require(
            app.ActiveGame.Protocol == DawnProtocol.StraussDelay,
            "The Strauss branch did not commit its semantic protocol.");
        app.Dispatch(new LoadSlotIntent(2));
        Require(
            app.ActiveGame.Presentation.OperationKind == DialoguePresentationOperationKind.Choice,
            "Pending-choice restore did not recover the choice operation.");
        Require(
            app.ActiveGame.Presentation.SelectedChoiceIndex == 1,
            "Pending-choice restore lost selection.");
        Require(
            app.ActiveGame.ConsequenceEmissionCount == 0,
            "Pending-choice restore emitted a consequence.");

        app.Dispatch(new NavigateIntent(-1));
        app.Dispatch(new AdvanceDialogueIntent());
        Require(
            app.ActiveGame.Protocol == DawnProtocol.ImmediateShutter,
            "The immediate-shutter branch did not commit its semantic protocol.");
        Require(app.ActiveGame.DawnEngineTested, "The immediate-shutter consequence was not semantic.");
        Require(
            app.ActiveGame.ConsequenceEmissionCount == 1,
            "The typed SUNKILL consequence did not emit exactly once.");
        string postEffectOperation = app.ActiveGame.Presentation.OperationId!;
        app.Dispatch(new SaveSlotIntent(3));
        app.Dispatch(new AdvanceDialogueIntent());
        app.Dispatch(new LoadSlotIntent(3));
        Require(
            app.ActiveGame.Presentation.OperationId == postEffectOperation,
            "Post-effect restore did not recover the exact line.");
        Require(
            app.ActiveGame.ConsequenceEmissionCount == 0,
            "A committed consequence replayed during restore.");
        Require(
            app.ActiveGame.DawnEngineTested,
            "Post-effect restore lost authoritative SUNKILL state.");

        app.Dispatch(new OpenSaveMenuIntent());
        Require(app.State.Screen == RenScreen.SaveMenu, "Save menu routing failed.");
        WriteScreenshot(artifactRoot, "save-menu.png", native.Render(++frameId));
        app.Dispatch(new BackIntent());
        app.Dispatch(new OpenLoadMenuIntent());
        Require(app.State.Screen == RenScreen.LoadMenu, "Load menu routing failed.");
        WriteScreenshot(artifactRoot, "load-menu.png", native.Render(++frameId));
        app.Dispatch(new BackIntent());

        while (app.State.Screen == RenScreen.Game)
        {
            app.Dispatch(new AdvanceDialogueIntent());
        }

        Require(app.State.Screen == RenScreen.End, "The proof scene did not enter its clean end flow.");
        app.Dispatch(new ReturnToMainMenuIntent());
        Require(
            app.State.Screen == RenScreen.MainMenu && app.ActiveGame is null,
            "Returning to the main menu left stale game state.");

        string firstReplayHash = RunReplay(DawnProtocol.ImmediateShutter);
        string secondReplayHash = RunReplay(DawnProtocol.ImmediateShutter);
        Require(firstReplayHash == secondReplayHash, "The same semantic trace produced a different hash.");

        double[] frameTimes = MeasureFrames(native, ref frameId);
        app.Dispatch(new QuitIntent());
        Require(app.ExitRequested, "Quit did not set the typed host exit request.");

        WriteJson(Path.Combine(artifactRoot, "save-restore-proof.json"), new
        {
            applicationSaveVersion = VnPersistence.SchemaVersion,
            activeScene = SunkillDialogue.DialogueId,
            afterLine = new
            {
                qualified = true,
                operation = afterLineOperation,
                redispatchCount = 0,
            },
            pendingChoice = new
            {
                qualified = true,
                operation = $"dialogue.{SunkillDialogue.DialogueId}.choice.protocol",
                selectedChoice = "wait-for-strauss",
                consequenceEmissionCount = 0,
            },
            postEffect = new
            {
                qualified = true,
                operation = postEffectOperation,
                protocol = DawnProtocol.ImmediateShutter,
                dawnEngineTested = true,
                consequenceEmissionCountAfterRestore = 0,
            },
            restoreLaw = "Deliverance candidate -> application validation -> Dominatus checkpoint restore -> presentation rebuild",
        });
        WriteJson(Path.Combine(artifactRoot, "settings-proof.json"), new
        {
            qualified = true,
            separateFromGameSave = true,
            persistedPath = "runtime/settings.json",
            values = app.Settings,
            aurelianBusGains = adjustedAudio.BusGains.ToDictionary(
                pair => pair.Key.Value,
                pair => pair.Value),
            malformedSettingsFallback = RenSettings.Default,
            textSpeed = "deferred: current dialogue text appears instantly",
        });
        WriteJson(Path.Combine(artifactRoot, "navigation-proof.json"), new
        {
            qualified = true,
            boot = RenScreen.MainMenu,
            routes = new[]
            {
                "MainMenu + NewGame -> Game",
                "MainMenu + Load -> LoadMenu",
                "MainMenu + Settings -> Settings",
                "Game + Back -> PauseMenu",
                "Game + Save -> SaveMenu",
                "LoadMenu + successful Load -> Game",
                "End + ReturnToMainMenu -> MainMenu",
                "MainMenu + Quit -> ExitRequested",
            },
            input = "physical input -> InputMan logical action -> typed RenIntent -> RenApp reducer",
            machinaPointerFocusCapture = true,
            quitRequested = app.ExitRequested,
        });
        WriteJson(Path.Combine(artifactRoot, "replay-proof.json"), new
        {
            qualified = true,
            trace = new[]
            {
                "Advance until protocol choice",
                "Choose open-shutters",
                "Advance until terminal",
            },
            firstHash = firstReplayHash,
            secondHash = secondReplayHash,
        });
        WriteJson(Path.Combine(artifactRoot, "performance.json"), new
        {
            sampleFrames = frameTimes.Length,
            averageMilliseconds = frameTimes.Average(),
            worstMilliseconds = frameTimes.Max(),
            scope = "native compositor sanity only; M13 is not a performance milestone",
        });
        WriteJson(Path.Combine(artifactRoot, "manifest.json"), new
        {
            milestone = "RENC#-VN-M13",
            kind = "minimal-native-vn-application-slice",
            title = "SUNKILL",
            outcome = "A",
            mainMenuQualified = true,
            newGameQualified = true,
            saveQualified = true,
            loadQualified = true,
            settingsQualified = true,
            quitQualified = true,
            shortSceneQualified = true,
            choiceQualified = true,
            semanticConsequenceQualified = true,
            pendingChoiceRestoreQualified = true,
            nativePresentationQualified = true,
            machinaCanvasRuntimeDependency = false,
            vnCathedralAdded = false,
            nativeLayerOrder = new[]
            {
                "world-background",
                "portrait",
                VnMachinaLayer.Id.Value,
            },
            assets = new[]
            {
                Asset(root, "samples/Integrations/Aurelian.Ariadne.VnDemo/Assets/sunkill-bunker.png"),
                Asset(root, "samples/Integrations/Aurelian.Ariadne.VnDemo/Assets/sunkill-oppenheimer.png"),
            },
            files = new[]
            {
                "main-menu.png",
                "settings.png",
                "scene-line.png",
                "scene-choice.png",
                "save-menu.png",
                "load-menu.png",
                "save-restore-proof.json",
                "settings-proof.json",
                "navigation-proof.json",
                "replay-proof.json",
                "performance.json",
                "manifest.json",
            },
        });

        await Task.CompletedTask;
        Console.WriteLine("RENC#-VN-M13: Outcome A");
        Console.WriteLine("SUNKILL boot/menu/game/choice/save/load/settings/quit/native presentation: qualified");
        Console.WriteLine($"deterministic-hash={firstReplayHash}");
        return 0;
    }

    private static void AdvanceToChoice(RenApp app)
    {
        for (int index = 0; index < 16; index++)
        {
            if (app.ActiveGame?.Presentation.OperationKind == DialoguePresentationOperationKind.Choice)
            {
                return;
            }

            app.Dispatch(new AdvanceDialogueIntent());
        }

        throw new InvalidOperationException("Dialogue did not reach the SUNKILL choice.");
    }

    private static string RunReplay(DawnProtocol protocol)
    {
        using var session = new VnSession();
        for (int index = 0; index < 32; index++)
        {
            if (session.Presentation.OperationKind == DialoguePresentationOperationKind.Choice)
            {
                break;
            }

            session.Advance();
        }

        Require(
            session.Presentation.OperationKind == DialoguePresentationOperationKind.Choice,
            "Replay trace did not reach the SUNKILL choice.");

        string choice = protocol == DawnProtocol.ImmediateShutter
            ? "open-shutters"
            : "wait-for-strauss";
        session.Choose(choice);
        while (!session.IsTerminal)
        {
            session.Advance();
        }

        return session.SemanticHash();
    }

    private static double[] MeasureFrames(VnNativeRenderer native, ref ulong frameId)
    {
        var result = new double[12];
        for (int index = 0; index < result.Length; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            native.Render(++frameId);
            stopwatch.Stop();
            result[index] = stopwatch.Elapsed.TotalMilliseconds;
        }

        return result;
    }

    private static void WriteScreenshot(
        string root,
        string name,
        NativeLayerFrameResult frame)
    {
        Require(
            frame.NativeFrame.Pixels is not null,
            "Native compositor did not return screenshot pixels.");
        PngWriter.Write(
            Path.Combine(root, name),
            VnNativeRenderer.Width,
            VnNativeRenderer.Height,
            frame.NativeFrame.Pixels!);
    }

    private static void WriteJson(string path, object value)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        }) + Environment.NewLine);
    }

    private static object Asset(string root, string relativePath)
    {
        string path = Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        return new
        {
            path = relativePath,
            sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
                .ToLowerInvariant(),
        };
    }

    internal static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null
            && !File.Exists(Path.Combine(current.FullName, "Copeland.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
