using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Standard.Authoring;
using TinyFarm.Core;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

internal static partial class TinyFarmMenuPresentation
{
    private static void Crafting(List<UiNode> nodes, TinyFarmGame game)
    {
        TinyFarmRpgProfile profile = game.State.Actor(TinyFarmIds.Player).Rpg!;
        TinyFarmCraftQuote quote = game.CraftQuote;
        TinyFarmNativeUi.Panel(nodes, "stove-panel", 44, 56, 1192, 612, 0x19362BFC);
        Label(nodes, "stove-title", "HOME / COOKING", 66, 78, 900, TextSize.H1, Gold);
        Label(nodes, "stove-summary", $"SP {profile.SpiritCurrent}/{profile.SpiritMaximum}   Cooking {TinyFarmCrafting.Rank(profile, TinyFarmSkill.Cooking)}   /   world paused", 68, 120, 960);
        Button(nodes, "close", "Close / ESC", 1022, 80, 190);
        Button(nodes, "recipe", "Learned: Turnip Soup", 66, 162, 310,
            disabled: !profile.Crafting!.Knows(TinyFarmCraftingContent.Recipe), selected: game.SelectedCraftRecipe is not null);
        Button(nodes, "experiment", "Experiment / clear inputs", 390, 162, 330, selected: game.SelectedCraftRecipe is null);
        Label(nodes, "supplies", "Water is supplied here. Other ingredients come from your bag.", 66, 210, 1100);

        ItemDefinition[] ingredients = game.Definitions.Items.Where(item => item.Food is null
            && (game.State.ProductCount(TinyFarmIds.Player, item.Id) > 0 || TinyFarmCrafting.IsSupplied(game.Definitions, item.Id)))
            .OrderBy(item => item.Name, StringComparer.Ordinal).Take(8).ToArray();
        UiTableColumn[] columns = [new("Ingredient", 270), new("Available", 130), new("Input", 90)];
        UiTableRow[] rows = ingredients.Select(item => new UiTableRow(item.Id.Value,
            [item.Name, TinyFarmCrafting.IsSupplied(game.Definitions, item.Id) ? "Unlimited" : game.State.ProductCount(TinyFarmIds.Player, item.Id).ToString(),
                (game.CraftInputs.SingleOrDefault(input => input.Product == item.Id)?.Count ?? 0).ToString()], UiAction.Named("add:" + item.Id.Value))).ToArray();
        nodes.Add(UI.Anchor(UiDataTable.Build("craft-inputs", columns, rows, colors: TableColors), left: 66, top: 250, width: 490, height: 330));
        for (int index = 0; index < ingredients.Length; index++)
        {
            Button(nodes, "remove:" + ingredients[index].Id.Value, "-", 566, 286 + index * 34, 48);
        }
        Label(nodes, "craft-result", quote.Recipe is null ? "Unmatched experiment" : game.Definitions.Items.Single(item => item.Id == quote.Output).Name,
            652, 252, 530, TextSize.H1, Gold);
        Label(nodes, "craft-cost", $"Cost: {quote.SpiritCost} SP   /   Difficulty {quote.Difficulty}", 652, 304, 530);
        Label(nodes, "craft-xp", $"Cooking XP: {(profile.ProgressionEnabled && TinyFarmCrafting.Rank(profile, TinyFarmSkill.Cooking) < 50 ? quote.Experience : 0)}   Fire XP: {(profile.ProgressionEnabled && TinyFarmCrafting.Rank(profile, TinyFarmSkill.Fire) < 50 ? quote.FireExperience : 0)}", 652, 342, 530);
        Label(nodes, "craft-knowledge", quote.Known ? "Learned recipe cost" : "Unknown recipe: +50% SP; success discovers it", 652, 382, 530);
        TinyFarmFoodEffect? food = game.Definitions.Items.SingleOrDefault(item => item.Id == quote.Output)?.Food;
        Label(nodes, "craft-food", food is null ? "Unmatched inputs cost 2 SP; ingredients retained." : $"Food restores {food.HealthRestore} HP + {food.SpiritRestore} SP", 652, 424, 530);
        Label(nodes, "craft-warning", profile.SpiritCurrent < quote.SpiritCost ? "Insufficient SP: attempt exhausts you; inputs retained." : "Click an ingredient to add one; use - to remove.", 652, 466, 530, TextSize.Sm, Gold);
        Button(nodes, "craft", "Cook / ENTER", 652, 512, 270, disabled: game.CraftInputs.Count == 0 || !quote.IngredientsAvailable);
        Button(nodes, "eat", "Eat result", 936, 512, 256,
            disabled: quote.Output is not ProductId output || game.State.ProductCount(TinyFarmIds.Player, output) == 0);
        Label(nodes, "craft-status", game.Status, 66, 610, 1130, TextSize.Sm, Gold);
        Label(nodes, "craft-help", "Recipe card: pick it up at home, then Read in I. Rock salt: gather the outcrop east of the garden.", 66, 650, 1140, TextSize.Sm);
    }
}

