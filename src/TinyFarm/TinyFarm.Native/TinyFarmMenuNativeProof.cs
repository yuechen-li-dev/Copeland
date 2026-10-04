using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurelian.Composition;
using Aurelian.GameHost;
using InputMan.Core;
using Silk.NET.Maths;
using TinyFarm.Core;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

internal sealed record TinyFarmMenuProofResult(string Outcome, string Device, int Width, int Height,
    bool PointerQualified, bool TextSearchQualified, bool KeyboardQualified, bool MenuFreezesWorld,
    bool EquipmentAffectsSword, bool SaveLoadRestoresEquipment, bool ResizeQualified, string[] Captures,
    int UiRebuilds, int DynamicTextureUploads, bool ReflectionSerializationDisabled,
    TinyFarmMenuPerformance[] Performance, string SemanticHash);

internal sealed record TinyFarmMenuPerformance(int Width, int Height, int Frames, double P50Milliseconds,
    double P95Milliseconds, double P99Milliseconds, double DrawsPerFrame, int UiRebuilds, int UiTextureUploads);

[JsonSerializable(typeof(TinyFarmMenuProofResult))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class TinyFarmMenuProofJsonContext : JsonSerializerContext;

internal static class TinyFarmMenuNativeProof
{
    public static void Run(string root, TinyFarmGame game, TinyFarmNativeWindow window,
        TinyFarmNativeRenderer renderer, AurelianGameHost host)
    {
        string output = Path.Combine(root, "artifacts", "tinyfarm-ui-subsystems");
        Directory.CreateDirectory(output);
        var performance = new List<TinyFarmMenuPerformance>();
        void Frame() => host.RunFrame(TimeSpan.FromSeconds(1.0 / 60));
        void Key(KeyboardKey key)
        {
            window.InjectKey(key, true);
            Frame();
            window.InjectKey(key, false);
            Frame();
        }
        void Click(string action)
        {
            LayerPoint center = renderer.MenuActionCenter(action);
            window.MenuInput.Enqueue(new LayerPointerButtonChanged(center, LayerPointerButton.Primary, true));
            Frame();
            window.MenuInput.Enqueue(new LayerPointerButtonChanged(center, LayerPointerButton.Primary, false));
            Frame();
        }
        void Capture(string name)
        {
            TinyFarmM25NativeProof.Capture(Path.Combine(output, name), renderer, host);
        }
        void AwaitPersistence()
        {
            var timer = Stopwatch.StartNew();
            while (game.SaveInProgress || game.LoadInProgress)
            {
                Require(timer.Elapsed < TimeSpan.FromSeconds(15), "Persistence did not complete in the native host.");
                Frame();
            }
        }

        void Measure()
        {
            renderer.ResetPerformanceMetrics();
            int before = renderer.UiRebuilds;
            var samples = new List<double>();
            for (int frame = 0; frame < 120; frame++)
            {
                long started = Stopwatch.GetTimestamp();
                Frame();
                samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            samples.Sort();
            performance.Add(new TinyFarmMenuPerformance(renderer.Layout.Width, renderer.Layout.Height, samples.Count,
                samples[59], samples[113], samples[118], renderer.DrawCalls / 120.0,
                renderer.UiRebuilds - before, renderer.DynamicUiTextureUploads));
        }

        Key(KeyboardKey.Enter);
        Key(KeyboardKey.I);
        Require(game.Screen == TinyFarmScreen.Inventory, "I did not open inventory.");
        string world = Hash(game);
        for (int frame = 0; frame < 60; frame++)
        {
            Frame();
        }
        Require(Hash(game) == world, "Inventory did not freeze semantic world and field.");
        Capture("inventory-1080p.png");
        Measure();
        Key(KeyboardKey.F9);
        Capture("inventory-hud-hidden.png");
        Require(game.Screen == TinyFarmScreen.Inventory && Hash(game) == world, "HUD toggle affected modal or world.");
        Key(KeyboardKey.F9);
        Click("search");
        window.MenuInput.Enqueue(new LayerTextEntered("axefnqi"));
        Frame();
        Key(KeyboardKey.F);
        Key(KeyboardKey.N);
        Key(KeyboardKey.Q);
        Key(KeyboardKey.I);
        Key(KeyboardKey.E);
        Require(game.Menus.SearchFocused, "Text letters stole search focus.");
        Require(game.Menus.Search == "axefnqi" && !game.ShouldQuit && !game.SaveInProgress, "Text leaked into shortcuts.");
        for (int character = 0; character < 4; character++)
        {
            window.MenuInput.Enqueue(new LayerKeyChanged(LayerKey.Backspace, true));
        }
        Frame();
        Require(game.Menus.Rows(game.State, game.Definitions).Count == 1, "Search did not filter the table.");
        Capture("inventory-filter.png");
        Key(KeyboardKey.Escape);
        Require(!game.Menus.SearchFocused && game.Screen == TinyFarmScreen.Inventory, "Escape did not leave search first.");
        Click("clear");
        Click("sort:value");
        Require(game.Menus.Sort == InventorySort.Value, "Header did not change sorting.");
        Click("equipment");
        Click("row:item:sword");
        Capture("equipment-1080p.png");
        Click("primary");
        Require(!TinyFarmEquipmentRules.IsEquipped(game.State, TinyFarmIds.Sword), "Unequip did not change semantic loadout.");
        Key(KeyboardKey.I);
        Key(KeyboardKey.J);
        Require(game.State.Slice!.SwordTicks == 0, "Unequipped sword still attacked.");
        Key(KeyboardKey.Escape);
        Capture("pause-1080p.png");
        Click("save");
        AwaitPersistence();
        Require(game.MenuSaveAvailable && game.HasSave, "Save menu did not create a checkpoint.");
        Click("inventory");
        Click("row:item:sword");
        Click("primary");
        Require(TinyFarmEquipmentRules.IsEquipped(game.State, TinyFarmIds.Sword), "Equip failed.");
        Key(KeyboardKey.Escape);
        Require(game.Screen == TinyFarmScreen.Paused, "Bag did not return to its pause parent.");
        Click("load");
        Require(game.Menus.Confirmation == "load", "Load did not ask about unsaved progress.");
        Capture("load-confirmation.png");
        Click("confirm");
        AwaitPersistence();
        Require(game.Screen == TinyFarmScreen.Playing && !TinyFarmEquipmentRules.IsEquipped(game.State, TinyFarmIds.Sword),
            "Native menu load did not restore the saved equipment.");
        Key(KeyboardKey.I);
        Click("row:item:sword");
        Click("primary");
        Key(KeyboardKey.ArrowUp);
        Key(KeyboardKey.ArrowDown);
        Require(game.Menus.SelectedKey == "item:sword", "Keyboard row navigation failed.");
        int rebuilds = renderer.UiRebuilds;
        for (int frame = 0; frame < 30; frame++)
        {
            Frame();
        }
        Require(renderer.UiRebuilds == rebuilds, "Unchanged menu rebuilt every frame.");

        string beforeResize = Hash(game);
        window.NativeWindow.Size = new Vector2D<int>(2560, 1440);
        window.NativeWindow.DoEvents();
        Frame();
        Require(renderer.Layout.Width == 2560 && renderer.Layout.Height == 1440, "1440p resize did not reach Vulkan.");
        Click("bag");
        Capture("inventory-1440p.png");
        Measure();
        window.NativeWindow.Size = new Vector2D<int>(1600, 1000);
        window.NativeWindow.DoEvents();
        Frame();
        Click("category:Tools");
        Require(game.Menus.Category == InventoryCategory.Tools, "Non-16:9 inverse pointer projection failed.");
        Capture("inventory-1600x1000.png");
        Require(Hash(game) == beforeResize, "Resize or filtering changed the semantic world.");
        var result = new TinyFarmMenuProofResult("A: native inventory, equipment and pause/save menu work", renderer.Device,
            renderer.Layout.Width, renderer.Layout.Height, true, true, true, true, true, true, true,
            ["inventory-1080p.png", "equipment-1080p.png", "inventory-filter.png", "pause-1080p.png",
                "load-confirmation.png", "inventory-1440p.png", "inventory-1600x1000.png", "inventory-hud-hidden.png"],
            renderer.UiRebuilds, renderer.DynamicUiTextureUploads, !JsonSerializer.IsReflectionEnabledByDefault,
            performance.ToArray(), Hash(game));
        File.WriteAllText(Path.Combine(output, "native-proof.json"),
            JsonSerializer.Serialize(result, TinyFarmMenuProofJsonContext.Default.TinyFarmMenuProofResult));
        Console.WriteLine("TINYFARM_MENUS_NATIVE_QUALIFIED " + result.SemanticHash);
    }

    private static string Hash(TinyFarmGame game)
    {
        return TinyFarmSemanticHash.Compute(game.State) + ":" + game.Host.Session.Field.SemanticHash;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
