namespace TinyFarm.Core;

public enum InventoryCategory
{
    All,
    Weapons,
    Tools,
    Seeds,
    Ingredients,
    Food,
    Materials,
    Keepsakes
}

public sealed record TinyFarmInventoryRow(string Key, string Name, InventoryCategory Category,
    int Quantity, int Value, string Description, ItemId? Item, ProductId? Product,
    EquipmentSlot? Slot, bool Equipped);

/// <summary>Read-only projection of the existing two inventory containers; no duplicate inventory store.</summary>
public static class TinyFarmInventory
{
    public static IReadOnlyList<TinyFarmInventoryRow> Project(TinyFarmState state, TinyFarmDefinitions definitions)
    {
        return Project(state, definitions, TinyFarmIds.Player);
    }

    public static IReadOnlyList<TinyFarmInventoryRow> Project(TinyFarmState state, TinyFarmDefinitions definitions, ActorId owner)
    {
        var rows = new List<TinyFarmInventoryRow>();
        ActorState actor = state.Actor(owner);
        foreach (ItemId id in actor.Inventory)
        {
            ItemState item = state.Item(id);
            EquipmentSlot? slot = TinyFarmEquipmentRules.Slot(state, id);
            bool equipped = owner == TinyFarmIds.Player && TinyFarmEquipmentRules.IsEquipped(state, id);
            if (actor.Agent is TinyFarmAgentState agent)
            {
                if (agent.Equipment.Weapon == id)
                {
                    slot = EquipmentSlot.Weapon;
                    equipped = true;
                }
                if (agent.Equipment.Tool == id)
                {
                    slot = EquipmentSlot.Tool;
                    equipped = true;
                }
            }
            InventoryCategory category = InventoryCategory.Keepsakes;
            string description = "A keepsake in your pockets. Nearby conversations handle gifts and delivery.";
            if (slot == EquipmentSlot.Weapon)
            {
                category = InventoryCategory.Weapons;
                description = id == TinyFarmIds.Sword
                    ? "A trusty sword. Equip it to strike with J; each opening-slice hit deals 2 damage."
                    : "An authored weapon. Its combat move is not configured in this opening slice.";
            }
            else if (slot == EquipmentSlot.Tool || id == TinyFarmIds.FishingRod)
            {
                category = InventoryCategory.Tools;
                if (id == TinyFarmIds.Axe)
                {
                    description = "Equip this axe, select tool 3, then K beside a tree to gather firewood.";
                }
                else if (id == TinyFarmIds.FishingRod)
                {
                    description = "A fishing rod. Fishing is outside this opening slice.";
                }
                else
                {
                    description = "An authored tool. Its gameplay use is not configured in this opening slice.";
                }
            }
            if (item.TeachesRecipe is not null)
            {
                description = "Read to learn a cooking recipe. The card is retained.";
            }
            rows.Add(new TinyFarmInventoryRow("item:" + id.Value, item.Name, category, 1, Math.Max(1, item.Price / 2),
                owner == TinyFarmIds.Player ? description : "Owned by " + actor.Name + ".",
                id, null, slot, equipped));
        }
        foreach (InventoryStack stack in state.InventoryStacks.Where(stack => stack.Actor == owner))
        {
            ItemDefinition definition = definitions.Item(stack.Product);
            InventoryCategory category = ProductCategory(definitions, stack.Product);
            string description = category switch
            {
                InventoryCategory.Seeds => "Plant with tool 1 + K beside an empty plot. E waters it; sleep at home to grow it.",
                InventoryCategory.Ingredients => "Bring this ingredient to the house stove.",
                InventoryCategory.Food when stack.Product.Value == "turnip-broth" => "Warm turnip broth restores 4 health, up to 12. It is consumed only when you need healing.",
                InventoryCategory.Food => "Food from your kitchen. Keep it for a neighbour or a later adventure.",
                InventoryCategory.Materials => "Gathered material. Keep it for future crafting; crafting is not part of this menu yet.",
                _ => "An item in your pockets."
            };
            if (definition.Food is TinyFarmFoodEffect food)
            {
                description = $"Restores {food.HealthRestore} HP and {food.SpiritRestore} SP. Consumed only when either is needed.";
            }
            rows.Add(new TinyFarmInventoryRow("product:" + stack.Product.Value, definition.Name, category,
                stack.Count, definition.SellPrice, description, null, stack.Product, null, false));
        }
        return rows;
    }

    private static InventoryCategory ProductCategory(TinyFarmDefinitions definitions, ProductId product)
    {
        if (definitions.Crops.Any(crop => crop.SeedItemId == product))
        {
            return InventoryCategory.Seeds;
        }
        if (product == TinyFarmIds.Wood)
        {
            return InventoryCategory.Materials;
        }
        if (definitions.Item(product).Food is not null || definitions.CookingRecipes.Any(recipe => recipe.OutputProduct == product))
        {
            return InventoryCategory.Food;
        }
        return InventoryCategory.Ingredients;
    }
}
