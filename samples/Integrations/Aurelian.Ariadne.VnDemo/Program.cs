using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurelian.Composition;
using Aurelian.NativeComposition;
using InputMan.Core;

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
        string artifactRoot = Path.Combine(root, "artifacts", "aurelian-ariadne-machina-dialogue-m7b");
        Directory.CreateDirectory(artifactRoot);
        string saveRoot = Path.Combine(artifactRoot, "saves");

        using var session = new VnSession();
        var machina = new VnMachinaLayer(session);
        var persistence = new VnPersistence(saveRoot);
        session.SaveRequested = () => persistence.SaveAsync("mid-line", session).GetAwaiter().GetResult();
        session.LoadRequested = () => persistence.LoadAsync("mid-line", session).GetAwaiter().GetResult();

        Require(session.Presentation.StepId == "after-school.intro", "Dialogue did not enter the authored M7a opening line.");
        session.Press(KeyboardKey.A);
        Require(session.AutoEnabled, "InputMan auto toggle did not update presentation state.");
        session.PulseAutomatic();
        session.Press(KeyboardKey.A);
        Require(session.Presentation.StepId == "after-school.rei-angry", "Logical advance did not reach Rei's confrontation.");

        using var native = new VnNativeRenderer(root, session, machina);
        NativeLayerFrameResult lineFrame = native.Render(1);
        WriteScreenshot(artifactRoot, "01-line-presentation.png", lineFrame);

        session.Press(KeyboardKey.F);
        session.Press(KeyboardKey.Enter);
        Require(session.Presentation.StepId == "after-school.mika-warning", "Subdialogue call did not enter Mika's line.");
        WriteScreenshot(artifactRoot, "01b-subdialogue-mika.png", native.Render(2));
        session.Press(KeyboardKey.I);
        Require(session.Presentation.StepId == "after-school.rei-angry", "Mid-line load did not restore the exact pending line.");
        Require(session.DialogueDispatchCount == 0, "Restored pending line was re-emitted.");

        LayerPoint advanceCenter = machina.ActionCenter("vn.advance");
        LayerInputRoutingResult press = native.Route(new LayerPointerButtonChanged(
            advanceCenter,
            LayerPointerButton.Primary,
            true));
        LayerInputRoutingResult release = native.Route(new LayerPointerButtonChanged(
            advanceCenter,
            LayerPointerButton.Primary,
            false));
        Require(press.ConsumedBy == VnMachinaLayer.Id && press.CaptureOwner == VnMachinaLayer.Id, "Machina did not capture the advance click.");
        Require(release.FocusOwner == VnMachinaLayer.Id && release.CaptureOwner is null, "Machina did not retain focus and release pointer capture.");
        Require(session.Presentation.StepId == "after-school.mika-warning", "Machina click did not advance the semantic runtime.");

        session.Press(KeyboardKey.W);
        Require(!session.GameplayInputLeaked, "Gameplay input leaked through the opaque VN context.");
        session.Press(KeyboardKey.Enter);
        Require(session.Presentation.Kind == DialoguePresentationStepKind.Choice, "Subdialogue return did not reach the authored choice.");
        session.Press(KeyboardKey.ArrowDown);
        Require(session.Presentation.SelectedChoiceIndex == 1, "InputMan logical down did not move choice focus.");

        NativeLayerFrameResult choiceFrame = native.Render(3);
        WriteScreenshot(artifactRoot, "02-choice-presentation.png", choiceFrame);
        session.Press(KeyboardKey.A);
        session.Press(KeyboardKey.S);
        await persistence.SaveAsync("pending-choice", session);
        session.Press(KeyboardKey.Enter);
        Require(session.Presentation.StepId == "after-school.deflect", "Selected branch did not execute.");
        await persistence.LoadAsync("pending-choice", session);
        Require(session.Presentation.StepId == "after-school.response", "Pending choice load did not restore the exact operation.");
        Require(session.Presentation.SelectedChoiceIndex == 1, "Pending choice load did not restore UI selection.");
        Require(session.Presentation.Choices.Select(choice => choice.Id).SequenceEqual(["apologize", "deflect"]), "Choice declaration order changed on restore.");
        Require(session.AutoEnabled && session.SkipEnabled, "Auto/skip presentation state did not follow the declared persisted law.");
        session.Press(KeyboardKey.A);
        session.Press(KeyboardKey.Escape);
        Require(!session.AutoEnabled && !session.SkipEnabled, "Cancel did not deterministically clear skip state.");

        LayerPoint apologyCenter = machina.ActionCenter("vn.choice.apologize");
        native.Route(new LayerPointerButtonChanged(apologyCenter, LayerPointerButton.Primary, true));
        native.Route(new LayerPointerButtonChanged(apologyCenter, LayerPointerButton.Primary, false));
        Require(session.Presentation.StepId == "after-school.apology", "Conditional apology path did not execute.");
        Require(session.ConsequenceEmissionCount == 1, "Typed letter-return consequence was not emitted exactly once.");
        Require(session.Agent.Bb.GetOrDefault(VnDialogueDefinition.LetterReturned, false), "Typed consequence was not reflected in semantic state.");
        await persistence.SaveAsync("post-effect", session);
        session.Press(KeyboardKey.Enter);
        Require(session.Presentation.StepId == "after-school.end", "Apology path did not reach the ending line.");
        await persistence.LoadAsync("post-effect", session);
        Require(session.Presentation.StepId == "after-school.apology", "Post-effect load did not resume at the pending line.");
        Require(session.ConsequenceEmissionCount == 0, "A completed consequence was re-emitted during restore.");
        Require(session.Agent.Bb.GetOrDefault(VnDialogueDefinition.LetterReturned, false), "Loaded semantic state lost the completed consequence.");

        NativeLayerFrameResult restoredFrame = native.Render(4);
        WriteScreenshot(artifactRoot, "03-save-load-restored.png", restoredFrame);

        object proof = new
        {
            milestone = "AURELIAN-ARIADNE-MACHINA-DIALOGUE-M7B",
            outcome = "A",
            dialogueAuthority = "Ariadne.OptFlow.Dialogue via Diag explicit authored operations and Dominatus HFSM",
            presentationProjection = "definition + active semantic checkpoint",
            layers = restoredFrame.NativeLayerOrder.Select(layer => layer.Value).ToArray(),
            nativePasses = restoredFrame.NativeFrame.RenderPassCount,
            nativePixelHash = restoredFrame.NativeFrame.PixelSha256,
            linePresentation = true,
            choicePresentation = true,
            subdialogueCall = true,
            conditionalBranch = true,
            typedConsequence = true,
            midLineRestoreExact = true,
            pendingChoiceRestoreExact = true,
            completedEffectNotReemitted = true,
            autoSkipPersisted = true,
            autoAdvanceBounded = true,
            saveLoadLogicalControls = true,
            inputManLogicalActions = true,
            machinaPointerFocusCapture = true,
            machinaPointerChoiceActivation = true,
            gameplayInputSuppressed = true,
            generatedAssets = new[]
            {
                "Assets/rei-angry.png",
                "Assets/rei-soft-cutout.png",
                "Assets/mika-concerned.png",
                "Assets/classroom-sunset.png",
            },
        };
        WriteJson(Path.Combine(artifactRoot, "proof.json"), proof);
        WriteJson(Path.Combine(artifactRoot, "manifest.json"), new
        {
            milestone = "AURELIAN-ARIADNE-MACHINA-DIALOGUE-M7B",
            files = new[]
            {
                "proof.json",
                "manifest.json",
                "01-line-presentation.png",
                "01b-subdialogue-mika.png",
                "02-choice-presentation.png",
                "03-save-load-restored.png",
                "saves/mid-line.dlv",
                "saves/pending-choice.dlv",
                "saves/post-effect.dlv",
            },
            assetKeys = new
            {
                background = "classroom.sunset",
                portraits = new[] { "rei:angry", "rei:soft", "mika:concerned" },
            },
            generation = new
            {
                mode = "OpenAI built-in image generation",
                originalCharactersOnly = true,
                prompts = new[]
                {
                    "Transparent full-body anime portrait: stern black-haired red-eyed schoolgirl, angry expression, navy uniform and red ribbon.",
                    "Matching transparent portrait variant of Rei: softened guarded smile, same design and pose.",
                    "Transparent anime portrait: brown-haired green-eyed schoolgirl, concerned expression, complementary school uniform.",
                    "Empty Japanese classroom at sunset, warm amber light, visual-novel background, no people or text.",
                },
                alphaRepair = "MachinaCanvas deriveAlphaMapPixels, threshold 32",
            },
            assets = new[]
            {
                Asset(root, "samples/Integrations/Aurelian.Ariadne.VnDemo/Assets/rei-angry.png"),
                Asset(root, "samples/Integrations/Aurelian.Ariadne.VnDemo/Assets/rei-soft-cutout.png"),
                Asset(root, "samples/Integrations/Aurelian.Ariadne.VnDemo/Assets/mika-concerned.png"),
                Asset(root, "samples/Integrations/Aurelian.Ariadne.VnDemo/Assets/classroom-sunset.png"),
            },
        });

        Console.WriteLine("AURELIAN-ARIADNE-MACHINA-DIALOGUE-M7B: Outcome A");
        Console.WriteLine($"native-layers={string.Join(",", restoredFrame.NativeLayerOrder)}; passes={restoredFrame.NativeFrame.RenderPassCount}; hash={restoredFrame.NativeFrame.PixelSha256}");
        Console.WriteLine("dialogue, choice, call, condition, consequence, save/load, InputMan suppression, Machina focus/capture: qualified");
        return 0;
    }

    private static void WriteScreenshot(string root, string name, NativeLayerFrameResult frame)
    {
        Require(frame.NativeFrame.Pixels is not null, "Native compositor did not return screenshot pixels.");
        PngWriter.Write(Path.Combine(root, name), VnNativeRenderer.Width, VnNativeRenderer.Height, frame.NativeFrame.Pixels!);
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
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return new
        {
            path = relativePath,
            sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
        };
    }

    internal static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Copeland.slnx"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
