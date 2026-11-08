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
        nodes.Add(StandardUI.Panel(44, 56, 1192, 612, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.StovePanel], style: TinyFarmUiTheme.Panel(TinyFarmUiTheme.MenuBackground)));
        nodes.Add(StandardUI.Label("HOME / COOKING", 66, 78, 900, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.StoveTitle],
            size: TextSize.H1, color: TinyFarmUiTheme.Accent));
        nodes.Add(StandardUI.Label($"SP {profile.SpiritCurrent}/{profile.SpiritMaximum}   Cooking {TinyFarmCrafting.Rank(profile, TinyFarmSkill.Cooking)}   /   world paused", 68, 120, 960, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.StoveSummary],
            size: TextSize.Md, color: TinyFarmUiTheme.Text));
        nodes.Add(StandardUI.Button("Close / ESC", TinyFarmUiActions.Close, 1022, 80, 190,
            style: TinyFarmUiTheme.Button()));
        // One button per stove recipe; unknown recipes stay visible but disabled until read, taught or discovered.
        int left = 66;
        foreach (CookingRecipeDefinition recipe in game.Definitions.CookingRecipes.Where(recipe => recipe.Crafting is not null))
        {
            string name = game.Definitions.Items.Single(item => item.Id == recipe.OutputProduct).Name;
            nodes.Add(StandardUI.Button("Learned: " + name, TinyFarmUiActions.Recipes[recipe.Id], left, 162, 300,
                disabled: !profile.Crafting!.Knows(recipe.Id),
                style: TinyFarmUiTheme.Button(selected: game.SelectedCraftRecipe == recipe.Id, disabled: !profile.Crafting!.Knows(recipe.Id))));
            left += 312;
        }
        nodes.Add(StandardUI.Button("Experiment / clear", TinyFarmUiActions.Experiment, left, 162, 250,
            style: TinyFarmUiTheme.Button(selected: game.SelectedCraftRecipe is null)));
        nodes.Add(StandardUI.Label("Water is supplied here. Other ingredients come from your bag.", 66, 210, 1100, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.Supplies],
            size: TextSize.Md, color: TinyFarmUiTheme.Text));

        ItemDefinition[] ingredients = game.Definitions.Items.Where(item => item.Food is null
            && (game.State.ProductCount(TinyFarmIds.Player, item.Id) > 0 || TinyFarmCrafting.IsSupplied(game.Definitions, item.Id)))
            .OrderBy(item => item.Name, StringComparer.Ordinal).Take(8).ToArray();
        UiTableColumn[] columns = [new("Ingredient", 270), new("Available", 130), new("Input", 90)];
        UiTableRow[] rows = ingredients.Select(item => new UiTableRow(TinyFarmUiIds.Ingredients[item.Id],
            [item.Name, TinyFarmCrafting.IsSupplied(game.Definitions, item.Id) ? "Unlimited" : game.State.ProductCount(TinyFarmIds.Player, item.Id).ToString(),
                (game.CraftInputs.SingleOrDefault(input => input.Product == item.Id)?.Count ?? 0).ToString()], TinyFarmUiActions.AddIngredients[item.Id])).ToArray();
        nodes.Add(UI.Anchor(UiDataTable.Build(TinyFarmUiIds.Nodes[TinyFarmUiNode.CraftInputs], columns, rows, colors: TableColors), left: 66, top: 250, width: 490, height: 330));
        for (int index = 0; index < ingredients.Length; index++)
        {
            nodes.Add(StandardUI.Button("-", TinyFarmUiActions.RemoveIngredients[ingredients[index].Id], 566, 286 + index * 34, 48,
                style: TinyFarmUiTheme.Button()));
        }
        nodes.Add(StandardUI.Label(quote.Recipe is null ? "Unmatched experiment" : game.Definitions.Items.Single(item => item.Id == quote.Output).Name, 652, 252, 530, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.CraftResult],
            size: TextSize.H1, color: TinyFarmUiTheme.Accent));
        nodes.Add(StandardUI.Label($"Cost: {quote.SpiritCost} SP   /   Difficulty {quote.Difficulty}", 652, 304, 530, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.CraftCost],
            size: TextSize.Md, color: TinyFarmUiTheme.Text));
        nodes.Add(StandardUI.Label($"Cooking XP: {(profile.ProgressionEnabled && TinyFarmCrafting.Rank(profile, TinyFarmSkill.Cooking) < 50 ? quote.Experience : 0)}   Fire XP: {(profile.ProgressionEnabled && TinyFarmCrafting.Rank(profile, TinyFarmSkill.Fire) < 50 ? quote.FireExperience : 0)}", 652, 342, 530, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.CraftXp],
            size: TextSize.Md, color: TinyFarmUiTheme.Text));
        nodes.Add(StandardUI.Label(quote.Known ? "Learned recipe cost" : "Unknown recipe: +50% SP; success discovers it", 652, 382, 530, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.CraftKnowledge],
            size: TextSize.Md, color: TinyFarmUiTheme.Text));
        TinyFarmFoodEffect? food = game.Definitions.Items.SingleOrDefault(item => item.Id == quote.Output)?.Food;
        nodes.Add(StandardUI.Label(food is null ? "Unmatched inputs cost 2 SP; ingredients retained." : $"Food restores {food.HealthRestore} HP + {food.SpiritRestore} SP", 652, 424, 530, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.CraftFood],
            size: TextSize.Md, color: TinyFarmUiTheme.Text));
        nodes.Add(StandardUI.Label(profile.SpiritCurrent < quote.SpiritCost ? "Insufficient SP: attempt exhausts you; inputs retained." : "Click an ingredient to add one; use - to remove.", 652, 466, 530, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.CraftWarning],
            size: TextSize.Sm, color: TinyFarmUiTheme.Accent));
        nodes.Add(StandardUI.Button("Cook / ENTER", TinyFarmUiActions.Craft, 652, 512, 270,
            disabled: game.CraftInputs.Count == 0 || !quote.IngredientsAvailable,
            style: TinyFarmUiTheme.Button(disabled: game.CraftInputs.Count == 0 || !quote.IngredientsAvailable)));
        bool cannotEat = quote.Output is not ProductId output || game.State.ProductCount(TinyFarmIds.Player, output) == 0;
        nodes.Add(StandardUI.Button("Eat result", TinyFarmUiActions.Eat, 936, 512, 256,
            disabled: cannotEat,
            style: TinyFarmUiTheme.Button(disabled: cannotEat)));
        nodes.Add(StandardUI.Label(game.Status, 66, 610, 1130, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.CraftStatus],
            size: TextSize.Sm, color: TinyFarmUiTheme.Accent));
        nodes.Add(StandardUI.Label("Recipe card: pick it up at home, then Read in I. Rock salt: gather the outcrop east of the garden.", 66, 650, 1140, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.CraftHelp],
            size: TextSize.Sm, color: TinyFarmUiTheme.Text));
    }
}

