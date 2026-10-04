using System.Text.Json;
using System.Text.Json.Serialization;
using Aurelian.Composition;
using Aurelian.GameHost;
using InputMan.Core;
using Silk.NET.Maths;
using TinyFarm.Core;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

internal sealed record TinyFarmCraftingProof(string Outcome, string Device, bool FarmHarvest,
    bool SaltGathered, bool ExperimentDiscovery, bool Exhaustion, bool FoodSpiritRecovery,
    bool ModifierCraft, bool InputMaps, bool SaveLoad, bool ReflectionDisabled, string SemanticHash);

[JsonSerializable(typeof(TinyFarmCraftingProof))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class TinyFarmCraftingProofJsonContext : JsonSerializerContext;

/// <summary>Real native input walkthrough, with no mid-loop fixture or coordinate writes.</summary>
internal static class TinyFarmCraftingNativeProof
{
    public static void Run(string root, TinyFarmGame game, TinyFarmNativeWindow window,
        TinyFarmNativeRenderer renderer, AurelianGameHost host)
    {
        string output = Path.Combine(root, "artifacts", "tinyfarm-crafting-gate-a2");
        Directory.CreateDirectory(output);
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
            LayerPoint point = renderer.MenuActionCenter(action);
            window.MenuInput.Enqueue(new LayerPointerButtonChanged(point, LayerPointerButton.Primary, true));
            Frame();
            window.MenuInput.Enqueue(new LayerPointerButtonChanged(point, LayerPointerButton.Primary, false));
            Frame();
        }
        void Hold(int x, int y)
        {
            window.InjectKey(KeyboardKey.A, x < 0);
            window.InjectKey(KeyboardKey.D, x > 0);
            window.InjectKey(KeyboardKey.W, y < 0);
            window.InjectKey(KeyboardKey.S, y > 0);
        }
        void Walk(float x, float y)
        {
            var target = new ScenePosition((int)(x * 1024), (int)(y * 1024));
            for (int step = 0; step < 600; step++)
            {
                ScenePosition current = game.State.ActorScene(TinyFarmIds.Player).WorldPosition;
                int dx = target.XUnits - current.XUnits;
                int dy = target.YUnits - current.YUnits;
                if (Math.Abs(dx) < 65 && Math.Abs(dy) < 65)
                {
                    Hold(0, 0);
                    Frame();
                    return;
                }
                Hold(Math.Abs(dx) < 40 ? 0 : Math.Sign(dx), Math.Abs(dy) < 40 ? 0 : Math.Sign(dy));
                Frame();
            }
            throw new InvalidOperationException($"Walk stalled at {game.State.ActorScene(TinyFarmIds.Player).WorldPosition} toward {x},{y}.");
        }
        void Face(ActorFacing facing)
        {
            (int x, int y) = facing switch
            {
                ActorFacing.Left => (-1, 0), ActorFacing.Right => (1, 0), ActorFacing.Up => (0, -1), _ => (0, 1)
            };
            Hold(x, y);
            Frame();
            Hold(0, 0);
            Frame();
        }
        void Shot(string name) => TinyFarmM25NativeProof.Capture(Path.Combine(output, name + ".png"), renderer, host);
        void OpenStove()
        {
            if (game.State.ActorScene(TinyFarmIds.Player).WorldPosition.XUnits < 4 * 1024)
            {
                Walk(3.5f, 7.5f);
                Walk(6.5f, 7.5f);
            }
            Walk(6.5f, 5.5f);
            Face(ActorFacing.Up);
            Key(KeyboardKey.E);
            Require(game.Screen == TinyFarmScreen.Crafting, "Stove did not open: " + game.Status);
        }

        Key(KeyboardKey.Enter);
        Walk(7.5f, 6.5f);
        Face(ActorFacing.Up);
        Key(KeyboardKey.E);
        Require(game.State.ProductCount(TinyFarmIds.Player, TinyFarmIds.Turnip) > 0, "Starter harvest failed.");
        Walk(9.5f, 6.5f);
        Face(ActorFacing.Up);
        Key(KeyboardKey.K);
        Key(KeyboardKey.E);
        Walk(11.5f, 8.5f);
        Walk(13.5f, 9.5f);
        Face(ActorFacing.Up);
        Shot("salt-outcrop");
        Key(KeyboardKey.E);
        Require(game.State.ProductCount(TinyFarmIds.Player, TinyFarmCraftingContent.Salt) == 2, "Salt gather failed: " + game.Status);
        Shot("farm-ingredients");
        Walk(5.5f, 4.5f);
        Face(ActorFacing.Left);
        Key(KeyboardKey.E);
        Shot("recipe-card-at-home");
        OpenStove();
        string worldBefore = TinyFarmSemanticHash.Compute(game.State);
        Click("add:turnip");
        Click("add:water");
        Click("add:rock-salt");
        window.MenuInput.Enqueue(new LayerKeyChanged(LayerKey.Tab, true));
        Frame();
        window.MenuInput.Enqueue(new LayerKeyChanged(LayerKey.Tab, false));
        Frame();
        Require(!game.Menus.SearchFocused, "Stove focused an invisible search field.");
        Key(KeyboardKey.W);
        Key(KeyboardKey.J);
        Require(!game.Contexts.Contains(GameControls.Gameplay) && TinyFarmSemanticHash.Compute(game.State) == worldBefore,
            "Crafting input leaked into the world.");
        Shot("unknown-salted-preview");
        Click("craft");
        Require(game.State.Actor(TinyFarmIds.Player).Rpg!.SpiritCurrent == 0
            && game.State.Actor(TinyFarmIds.Player).Rpg!.ActiveConditions!.Count == 1
            && game.State.ProductCount(TinyFarmIds.Player, TinyFarmIds.Turnip) > 0, "Exhaustion did not preserve ingredients.");
        Shot("exhausted-at-stove");
        Click("close");
        Walk(3.5f, 6.5f);
        Face(ActorFacing.Up);
        Key(KeyboardKey.B);
        Require(game.State.Actor(TinyFarmIds.Player).Rpg!.SpiritCurrent == 20, "Rest failed to restore SP.");
        OpenStove();
        Click("add:turnip");
        Click("add:water");
        Click("craft");
        Require(game.State.Actor(TinyFarmIds.Player).Rpg!.Crafting!.Knows(TinyFarmCraftingContent.Recipe), "Experiment did not discover recipe.");
        Require(game.State.Actor(TinyFarmIds.Player).Rpg!.SpiritCurrent == 14, "Unknown plain recipe cost incorrect.");
        Shot("recipe-discovered-1080p");
        Click("eat");
        Require(game.State.Actor(TinyFarmIds.Player).Rpg!.SpiritCurrent == 20, "Food failed to restore SP at full HP.");
        Click("close");
        Walk(6.5f, 6.5f);
        Walk(4.5f, 6.5f);
        Face(ActorFacing.Up);
        Key(KeyboardKey.E);
        Require(game.State.Item(TinyFarmCraftingContent.RecipeCard).Owner == TinyFarmIds.Player, "Recipe card pickup failed.");
        Key(KeyboardKey.I);
        Click("row:item:turnip-soup-recipe-card");
        Click("primary");
        Shot("recipe-card-inventory");
        Click("close");
        Walk(10.5f, 7.1f);
        Face(ActorFacing.Down);
        Key(KeyboardKey.E);
        Walk(9.5f, 6.5f);
        Face(ActorFacing.Up);
        Key(KeyboardKey.E);
        Require(game.State.Slice!.OwnHarvest, "Player-grown harvest failed.");
        Walk(5.5f, 4.5f);
        Face(ActorFacing.Left);
        Key(KeyboardKey.E);
        OpenStove();
        Click("recipe");
        Click("add:rock-salt");
        Require(game.CraftQuote.SpiritCost == 19, "Known modifier cost incorrect.");
        Shot("learned-salted-preview-1080p");
        Click("craft");
        Require(game.State.ProductCount(TinyFarmIds.Player, TinyFarmCraftingContent.SaltedSoup) == 1
            && game.State.Actor(TinyFarmIds.Player).Rpg!.SpiritCurrent == 1, "Salted craft failed.");
        Shot("salted-soup-crafted");
        Click("eat");
        Require(game.State.Actor(TinyFarmIds.Player).Rpg!.SpiritCurrent == 20, "Salted food recovery failed.");
        window.NativeWindow.Size = new Vector2D<int>(2560, 1440);
        Frame();
        Frame();
        Shot("stove-1440p");
        Click("close");
        Key(KeyboardKey.C);
        Click("group:Skills");
        Shot("cooking-practice-1440p");
        Click("close");
        string saved = TinyFarmSemanticHash.Compute(game.State);
        Require(game.Save() && game.Load(), "A2 save/load failed: " + game.Status);
        Require(TinyFarmSemanticHash.Compute(game.State) == saved, "A2 checkpoint changed semantic state.");
        var proof = new TinyFarmCraftingProof("A: crafting vertical slice qualified", renderer.Device,
            true, true, true, true, true, true, true, true, !JsonSerializer.IsReflectionEnabledByDefault, saved);
        File.WriteAllText(Path.Combine(output, "native-proof.json"),
            JsonSerializer.Serialize(proof, TinyFarmCraftingProofJsonContext.Default.TinyFarmCraftingProof));
        Console.WriteLine("TINYFARM_CRAFTING_A2_NATIVE_QUALIFIED " + saved);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
