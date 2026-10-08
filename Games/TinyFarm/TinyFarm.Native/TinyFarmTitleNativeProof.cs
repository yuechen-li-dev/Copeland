using System.Text.Json;
using System.Text.Json.Serialization;
using Aurelian.Composition;
using Aurelian.GameHost;
using InputMan.Core;
using Silk.NET.Maths;
using TinyFarm.Core;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

internal sealed record TinyFarmTitleProof(string Mode, bool InitialFocus, string Screen, bool Quit,
    bool Checkpoint, string Device, string Hash);
[JsonSerializable(typeof(TinyFarmTitleProof))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class TinyFarmTitleProofJsonContext : JsonSerializerContext;

/// <summary>Visible-window proof: deliberately does not use forced hidden-proof focus.</summary>
internal static class TinyFarmTitleNativeProof
{
    public static void Run(string root, TinyFarmGame game, TinyFarmNativeWindow window,
        TinyFarmNativeRenderer renderer, AurelianGameHost host, string[] args)
    {
        string output = Path.Combine(root, "artifacts", "tinyfarm-title-menu");
        Directory.CreateDirectory(output);
        string mode = "mouse";
        if (args.Contains("--title-load"))
        {
            mode = "load";
        }
        else if (args.Contains("--title-quit"))
        {
            mode = "quit";
        }
        else if (args.Contains("--title-keyboard"))
        {
            mode = "keyboard";
        }
        void Frame() => host.RunFrame(TimeSpan.Zero);
        void Click(string action)
        {
            LayerPoint point = renderer.MenuActionCenter(action);
            window.MenuInput.Enqueue(new LayerPointerButtonChanged(point, LayerPointerButton.Primary, true));
            Frame();
            window.MenuInput.Enqueue(new LayerPointerButtonChanged(point, LayerPointerButton.Primary, false));
            Frame();
        }
        bool initialFocus = window.IsFocused;
        Require(initialFocus, "Visible launch was not focused; title proof must exercise actual HWND focus.");
        Frame();
        string before = TinyFarmSemanticHash.Compute(game.State);
        if (mode == "mouse")
        {
            window.NativeWindow.WindowBorder = Silk.NET.Windowing.WindowBorder.Hidden;
            foreach ((int width, int height) in new[] { (1280, 720), (1920, 1080), (2560, 1440), (1600, 1000) })
            {
                window.NativeWindow.Size = new Vector2D<int>(width, height);
                Frame();
                Frame();
                Require(renderer.Layout.Width == width && renderer.Layout.Height == height, "Framebuffer resize did not reach requested dimensions.");
                TinyFarmM25NativeProof.Capture(Path.Combine(output, $"title-{width}x{height}.png"), renderer, host);
                Require(TinyFarmSemanticHash.Compute(game.State) == before, "Title resize mutated the world.");
            }
            Click("new-game");
            Require(game.Screen == TinyFarmScreen.Playing, "Mouse New Game did not start.");
            Require(game.Save(), "Could not create title-load checkpoint.");
            window.InjectKey(KeyboardKey.Escape, true);
            Frame();
            window.InjectKey(KeyboardKey.Escape, false);
            Frame();
            Require(game.Screen == TinyFarmScreen.Paused, "Escape did not open pause.");
            TinyFarmM25NativeProof.Capture(Path.Combine(output, "pause-1600x1000.png"), renderer, host);
            Require(TinyFarmSemanticHash.Compute(game.State) == before, "Pause presentation mutated the world.");
            window.InjectKey(KeyboardKey.Escape, true);
            Frame();
            window.InjectKey(KeyboardKey.Escape, false);
            Frame();
            Require(game.Screen == TinyFarmScreen.Playing, "Escape did not return from pause.");
        }
        else if (mode == "keyboard")
        {
            window.InjectKey(KeyboardKey.Enter, true);
            Frame();
            window.InjectKey(KeyboardKey.Enter, false);
            Frame();
            Require(game.Screen == TinyFarmScreen.Playing, "Enter did not start in the visible launch path.");
        }
        else if (mode == "load")
        {
            Require(game.MenuSaveAvailable, "Load checkpoint not detected at boot.");
            Click("load");
            for (int attempt = 0; attempt < 10000 && game.LoadInProgress; attempt++)
            {
                Frame();
            }
            Require(!game.LoadInProgress && game.Screen == TinyFarmScreen.Playing, "Mouse Load did not restore gameplay.");
            Require(TinyFarmSemanticHash.Compute(game.State) == before, "Checkpoint restoration changed the expected initial state.");
        }
        else
        {
            Click("quit");
            Require(game.ShouldQuit, "Mouse Quit did not request exit.");
        }
        var proof = new TinyFarmTitleProof(mode, initialFocus, game.Screen.ToString(), game.ShouldQuit,
            game.MenuSaveAvailable, renderer.Device, TinyFarmSemanticHash.Compute(game.State));
        File.WriteAllText(Path.Combine(output, mode + "-proof.json"),
            JsonSerializer.Serialize(proof, TinyFarmTitleProofJsonContext.Default.TinyFarmTitleProof));
        Console.WriteLine("TINYFARM_TITLE_QUALIFIED " + mode);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
