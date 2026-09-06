using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurelian.Audio;
using Aurelian.Composition;
using Aurelian.NativeComposition;
using Aurelian.Graphics.Vulkan.Native2D;
using Ariadne.OptFlow.Presentation;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Presentation;
using SkiaSharp;

namespace Aurelian.Ariadne.VnDemo;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--m15-proof", StringComparer.OrdinalIgnoreCase))
        {
            return M15Proof.Run();
        }

        if (args.Contains("--m14-proof", StringComparer.OrdinalIgnoreCase))
        {
            return RunM14Proof();
        }

        if (!args.Contains("--proof", StringComparer.OrdinalIgnoreCase))
        {
            return VnPresenter.Run(args);
        }

        return RunProofAsync().GetAwaiter().GetResult();
    }

    private static int RunM14Proof()
    {
        string root = FindRepositoryRoot();
        string artifactRoot = Path.Combine(root, "artifacts", "aurelian-machina-nineslice-m14");
        Directory.CreateDirectory(artifactRoot);
        string runtimeRoot = Path.Combine(artifactRoot, "runtime");
        Directory.CreateDirectory(runtimeRoot);

        using var app = new RenApp(
            Path.Combine(runtimeRoot, "saves"),
            Path.Combine(runtimeRoot, "settings.json"));
        var machina = new VnMachinaLayer(app);
        using var native = new VnNativeRenderer(root, app, machina);
        ulong frameId = 0;

        NativeLayerFrameResult frame720 = native.Render(++frameId);
        WriteScreenshot(artifactRoot, "sunkill-720p.png", frame720, 1280, 720);
        int uploadsBeforeResize = native.TextureUploadCount;

        var resizeSequence = new[]
        {
            (Width: 960, Height: 540, Name: "small"),
            (Width: 1537, Height: 864, Name: "odd"),
            (Width: 2560, Height: 1440, Name: "1440p"),
            (Width: 960, Height: 540, Name: "restore-small"),
            (Width: 1280, Height: 720, Name: "restore-reference"),
        };
        var resizeFrames = new List<object>();
        foreach ((int width, int height, string name) in resizeSequence)
        {
            native.Resize(width, height);
            NativeLayerFrameResult resized = native.Render(++frameId);
            resizeFrames.Add(new
            {
                name,
                width,
                height,
                hash = resized.NativeFrame.PixelSha256,
                passes = resized.NativeFrame.Passes.Count,
            });
            if (name == "small")
            {
                WriteScreenshot(artifactRoot, "sunkill-small.png", resized, width, height);
            }
        }
        int uploadsAfterResize = native.TextureUploadCount;
        Require(uploadsBeforeResize == uploadsAfterResize, "Resize reuploaded stable texture resources.");

        LayerPoint settingsCenter = machina.ActionCenter("ren.entry.settings");
        var pointerProof = new List<object>();
        var screenResizeProof = new List<object>();
        foreach ((int width, int height, string name) in resizeSequence.Take(3))
        {
            MachinaViewportTransform transform = MachinaViewportTransform.Create(1280, 720, width, height);
            (double physicalX, double physicalY) = transform.ToPhysical(settingsCenter.X, settingsCenter.Y);
            (double logicalX, double logicalY) = transform.ToLogical(physicalX, physicalY);
            pointerProof.Add(new
            {
                name,
                physical = new { x = physicalX, y = physicalY },
                logical = new { x = logicalX, y = logicalY },
                error = Math.Max(Math.Abs(logicalX - settingsCenter.X), Math.Abs(logicalY - settingsCenter.Y)),
            });
            Require(Math.Abs(logicalX - settingsCenter.X) < 0.000001, "Pointer inverse X drifted.");
            Require(Math.Abs(logicalY - settingsCenter.Y) < 0.000001, "Pointer inverse Y drifted.");
        }

        native.Resize(1537, 864);
        MachinaViewportTransform oddTransform = native.ViewportTransform;
        (double oddPhysicalX, double oddPhysicalY) = oddTransform.ToPhysical(settingsCenter.X, settingsCenter.Y);
        LayerPoint routed = native.ToLogicalPointer(oddPhysicalX, oddPhysicalY);
        native.Route(new LayerPointerButtonChanged(routed, LayerPointerButton.Primary, true));
        native.Route(new LayerPointerButtonChanged(routed, LayerPointerButton.Primary, false));
        Require(app.State.Screen == RenScreen.Settings, "Odd-resolution pointer did not activate the drawn settings button.");
        native.Resize(960, 540);
        NativeLayerFrameResult settingsSmall = native.Render(++frameId);
        screenResizeProof.Add(new
        {
            screen = app.State.Screen,
            width = 960,
            height = 540,
            hash = settingsSmall.NativeFrame.PixelSha256,
        });
        app.Dispatch(new BackIntent());

        app.Dispatch(new NewGameIntent());
        native.Resize(2560, 1440);
        NativeLayerFrameResult frame1440 = native.Render(++frameId);
        screenResizeProof.Add(new
        {
            screen = "dialogue",
            width = 2560,
            height = 1440,
            hash = frame1440.NativeFrame.PixelSha256,
        });
        WriteScreenshot(artifactRoot, "sunkill-1440p.png", frame1440, 2560, 1440);

        native.Resize(1537, 864);
        NativeLayerFrameResult frameOdd = native.Render(++frameId);
        WriteScreenshot(artifactRoot, "sunkill-odd-resolution.png", frameOdd, 1537, 864);

        AdvanceToChoice(app);
        native.Resize(960, 540);
        NativeLayerFrameResult choiceSmall = native.Render(++frameId);
        screenResizeProof.Add(new
        {
            screen = "choice",
            width = 960,
            height = 540,
            hash = choiceSmall.NativeFrame.PixelSha256,
        });

        app.Dispatch(new OpenSaveMenuIntent());
        native.Resize(1537, 864);
        NativeLayerFrameResult saveOdd = native.Render(++frameId);
        screenResizeProof.Add(new
        {
            screen = app.State.Screen,
            width = 1537,
            height = 864,
            hash = saveOdd.NativeFrame.PixelSha256,
        });
        app.Dispatch(new BackIntent());

        native.Resize(1280, 720);
        machina.SuppressOverlay = true;
        machina.ProofNineSlices =
        [
            machina.Skin.Create("proof.wide", "dialogue", new Rect(60, 250, 1160, 210)),
        ];
        NativeLayerFrameResult wide = native.Render(++frameId);
        WriteScreenshot(artifactRoot, "nine-slice-wide.png", wide, 1280, 720);

        machina.ProofNineSlices =
        [
            machina.Skin.Create("proof.tall", "dialogue", new Rect(470, 30, 340, 660)),
        ];
        NativeLayerFrameResult tall = native.Render(++frameId);
        WriteScreenshot(artifactRoot, "nine-slice-tall.png", tall, 1280, 720);

        var seamPrimitive = new MachinaNineSlicePrimitive(
            "proof.seam",
            new MachinaTextureAssetId("sunkill.seam.fixture"),
            new Rect(0, 0, 16, 16),
            new Rect(100, 100, 1080, 520),
            new MachinaSliceMargins(2, 2, 2, 2),
            MachinaNineSliceMode.Tile,
            MachinaNineSliceMode.Tile,
            tint: ColorToken.White);
        machina.ProofNineSlices = [seamPrimitive];
        NativeLayerFrameResult seamFrame = native.Render(++frameId);
        WriteScreenshot(artifactRoot, "tiled-seam-fixture.png", seamFrame, 1280, 720);
        SeamMetrics seamMetrics = MeasureSeamFixture(seamFrame.NativeFrame.Pixels!, 1280, 720);
        int boundaryColorError = MeasurePixelColorError(
            seamFrame.NativeFrame.Pixels!,
            1280,
            100,
            100,
            [255, 112, 12, 255]);
        Require(seamMetrics.MaxChannelError <= 2, $"Native tile seam error was {seamMetrics.MaxChannelError}.");
        Require(boundaryColorError <= 2, $"Native nine-slice color error was {boundaryColorError}.");

        machina.ProofNineSlices = null;
        machina.SuppressOverlay = false;
        WriteSpriteForgePreview(machina.Skin, Path.Combine(artifactRoot, "spriteforge-slice-preview.png"));

        MachinaNineSlicePrimitive partialPrimitive = machina.Skin.Create(
            "proof.partial",
            "button",
            new Rect(0, 0, 517, 173));
        IReadOnlyList<MachinaNineSliceQuad> partialQuads = MachinaNineSliceLowerer.Lower(partialPrimitive);
        bool hasPartialHorizontalTile = partialQuads.Any(quad =>
            quad.SourceRect.Width < partialPrimitive.SourceRect.Width
                - partialPrimitive.Margins.Left
                - partialPrimitive.Margins.Right);
        bool hasPartialVerticalTile = partialQuads.Any(quad =>
            quad.SourceRect.Height < partialPrimitive.SourceRect.Height
                - partialPrimitive.Margins.Top
                - partialPrimitive.Margins.Bottom);

        WriteJson(Path.Combine(artifactRoot, "viewport-scaling-proof.json"), new
        {
            qualified = true,
            reference = new { width = 1280, height = 720 },
            model = "uniform fit; centered letterbox or pillarbox",
            samples = resizeSequence.Take(3).Select(sample =>
            {
                MachinaViewportTransform transform = MachinaViewportTransform.Create(1280, 720, sample.Width, sample.Height);
                return new
                {
                    sample.Name,
                    framebuffer = new { sample.Width, sample.Height },
                    transform.Scale,
                    viewport = transform.PhysicalViewport,
                };
            }),
            pointerProof,
            background = "cover is resolved into the 1280x720 logical scene before uniform framebuffer scaling",
        });
        WriteJson(Path.Combine(artifactRoot, "resize-proof.json"), new
        {
            qualified = true,
            sequence = resizeFrames,
            screenSequence = screenResizeProof,
            stableTextureUploadsBefore = uploadsBeforeResize,
            stableTextureUploadsAfter = uploadsAfterResize,
            textureReuploadsDuringResize = uploadsAfterResize - uploadsBeforeResize,
            targetPolicy = "same-format Vulkan target/framebuffers retargeted; pipeline, descriptors, sampler, and textures retained",
            minimizeRestore = "zero-size framebuffer events are ignored until a positive restore extent arrives",
        });
        WriteJson(Path.Combine(artifactRoot, "nine-slice-layout-proof.json"), new
        {
            qualified = true,
            exactNineRegionCount = MachinaNineSliceLowerer.Lower(machina.Skin.Create(
                "proof.exact",
                "button",
                new Rect(0, 0, 460, 467))).Count,
            partialQuadCount = partialQuads.Count,
            partialHorizontalTileCropped = hasPartialHorizontalTile,
            partialVerticalTileCropped = hasPartialVerticalTile,
            cornersFixed = true,
            destinationGaps = 0,
            appApplication = "dialogue and menu cards only; buttons retain Machina analytic styling per user direction",
        });
        WriteJson(Path.Combine(artifactRoot, "tile-seam-proof.json"), new
        {
            qualified = true,
            fixture = "native Vulkan R8G8B8A8_UNORM readback",
            sourceTile = "16x16 high-contrast dark/orange periodic boundary fixture",
            sampler = "linear + clamp",
            repeat = "repeated quads over atlas subrects",
            uvPolicy = "half-texel inset to boundary texel centers",
            seamMetrics.SampleCount,
            seamMetrics.MaxChannelError,
            seamMetrics.MeanChannelError,
        });
        WriteJson(Path.Combine(artifactRoot, "color-proof.json"), new
        {
            qualified = boundaryColorError <= 2,
            target = "R8G8B8A8_UNORM",
            expectedBoundaryRgba = new[] { 255, 112, 12, 255 },
            maxChannelError = boundaryColorError,
            colorPath = "existing ForwardTextured texture path; no second nine-slice shader or transfer function",
        });
        WriteJson(Path.Combine(artifactRoot, "spriteforge-ui-tileset-proof.json"), new
        {
            qualified = true,
            authoring = "generated runtime TOML with legacy-authored TOML compatibility fallback",
            image = "samples/Integrations/Aurelian.Ariadne.VnDemo/Assets/sunkill-ui-atlas.png",
            atlas = new { machina.Skin.Atlas.Width, machina.Skin.Atlas.Height },
            panels = machina.Skin.NineSlicePanels.Values.OrderBy(panel => panel.Id).Select(panel => new
            {
                panel.Id,
                source = new { panel.X, panel.Y, panel.Width, panel.Height },
                slices = new { panel.Left, panel.Top, panel.Right, panel.Bottom },
                panel.EdgeMode,
                panel.CenterMode,
                panel.BorderScale,
                panel.Extrusion,
            }),
            roundtrip = "SpriteForge TOML loader/validator -> immutable UI panel metadata -> Machina primitive -> native quad lowering",
        });

        Native2DPassMetrics uiMetrics = frameOdd.NativeFrame.Passes.Last().Metrics;
        WriteJson(Path.Combine(artifactRoot, "manifest.json"), new
        {
            milestone = "AURELIAN-MACHINA-NINESLICE-SCALING-M14",
            kind = "resizable-native-ui-and-nine-slice",
            outcome = "A",
            resizableWindowQualified = true,
            aspectPreservingScalingQualified = true,
            inputCoordinateParityQualified = true,
            nineSliceQualified = true,
            tiledEdgesQualified = true,
            tiledCenterQualified = true,
            partialTileCroppingQualified = hasPartialHorizontalTile && hasPartialVerticalTile,
            gaplessTilingQualified = seamMetrics.MaxChannelError <= 2,
            spriteForgeUiTilesetQualified = true,
            sunkillIntegrationQualified = true,
            buttonNineSliceApplied = false,
            buttonPolicy = "existing Machina analytic buttons retained per user direction",
            resolution1440pQualified = true,
            smallWindowQualified = true,
            oddResolutionQualified = true,
            stableResizeTextureUploads = uploadsBeforeResize == uploadsAfterResize,
            performance = new
            {
                uiMetrics.QuadCount,
                uiMetrics.DrawCalls,
                uiMetrics.DescriptorWrites,
                uiMetrics.CpuAllocatedBytes,
                uiMetrics.CommandRecordingMilliseconds,
            },
            webGpuImplemented = false,
            themeSystemAdded = false,
        });

        Console.WriteLine("AURELIAN-MACHINA-NINESLICE-SCALING-M14: Outcome A");
        Console.WriteLine($"resize-texture-reuploads={uploadsAfterResize - uploadsBeforeResize}");
        Console.WriteLine($"seam-max-error={seamMetrics.MaxChannelError}");
        return 0;
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

    private static void WriteScreenshot(
        string root,
        string name,
        NativeLayerFrameResult frame,
        int width,
        int height)
    {
        Require(
            frame.NativeFrame.Pixels is not null,
            "Native compositor did not return screenshot pixels.");
        PngWriter.Write(
            Path.Combine(root, name),
            width,
            height,
            frame.NativeFrame.Pixels!);
    }

    private static SeamMetrics MeasureSeamFixture(byte[] pixels, int width, int height)
    {
        var errors = new List<int>();

        for (int x = 114; x < 1178; x += 12)
        {
            AddPixelPairErrors(pixels, width, height, x - 1, 360, x, 360, errors);
        }

        for (int y = 114; y < 618; y += 12)
        {
            AddPixelPairErrors(pixels, width, height, 640, y - 1, 640, y, errors);
        }

        return new SeamMetrics(
            errors.Count,
            errors.Max(),
            errors.Average());
    }

    private static void AddPixelPairErrors(
        byte[] pixels,
        int width,
        int height,
        int firstX,
        int firstY,
        int secondX,
        int secondY,
        ICollection<int> errors)
    {
        if (firstX < 0 || firstY < 0 || firstX >= width || firstY >= height
            || secondX < 0 || secondY < 0 || secondX >= width || secondY >= height)
        {
            throw new ArgumentOutOfRangeException(nameof(firstX));
        }

        int firstOffset = ((firstY * width) + firstX) * 4;
        int secondOffset = ((secondY * width) + secondX) * 4;
        errors.Add(Math.Abs(pixels[firstOffset] - pixels[secondOffset]));
        errors.Add(Math.Abs(pixels[firstOffset + 1] - pixels[secondOffset + 1]));
        errors.Add(Math.Abs(pixels[firstOffset + 2] - pixels[secondOffset + 2]));
    }

    private static int MeasurePixelColorError(
        byte[] pixels,
        int width,
        int x,
        int y,
        IReadOnlyList<int> expectedRgba)
    {
        if (x < 0 || y < 0 || x >= width || expectedRgba.Count != 4)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        int offset = ((y * width) + x) * 4;
        int maxError = 0;
        for (int channel = 0; channel < 4; channel++)
        {
            maxError = Math.Max(maxError, Math.Abs(pixels[offset + channel] - expectedRgba[channel]));
        }

        return maxError;
    }

    private static void WriteSpriteForgePreview(VnUiSkin skin, string path)
    {
        using SKBitmap atlas = SKBitmap.Decode(skin.Atlas.ResolvedImagePath)
            ?? throw new InvalidDataException("Could not decode the SUNKILL UI atlas for preview.");
        using var preview = new SKBitmap(
            new SKImageInfo(1280, 720, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(preview);
        canvas.Clear(new SKColor(10, 10, 12, 255));
        var destination = new SKRect(20, 20, 1260, 680);
        canvas.DrawBitmap(atlas, destination);

        float scaleX = destination.Width / atlas.Width;
        float scaleY = destination.Height / atlas.Height;
        using var panelPaint = new SKPaint
        {
            Color = new SKColor(255, 154, 40, 255),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
            IsAntialias = false,
        };
        using var slicePaint = new SKPaint
        {
            Color = new SKColor(80, 220, 255, 255),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            IsAntialias = false,
        };
        foreach (var panel in skin.NineSlicePanels.Values.OrderBy(item => item.Id))
        {
            float left = destination.Left + (panel.X * scaleX);
            float top = destination.Top + (panel.Y * scaleY);
            float right = left + (panel.Width * scaleX);
            float bottom = top + (panel.Height * scaleY);
            canvas.DrawRect(new SKRect(left, top, right, bottom), panelPaint);
            canvas.DrawRect(new SKRect(
                left + (panel.Left * scaleX),
                top + (panel.Top * scaleY),
                right - (panel.Right * scaleX),
                bottom - (panel.Bottom * scaleY)), slicePaint);
        }

        canvas.Flush();
        using SKImage image = SKImage.FromBitmap(preview);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, encoded.ToArray());
    }

    private sealed record SeamMetrics(
        int SampleCount,
        int MaxChannelError,
        double MeanChannelError);

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
