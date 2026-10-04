namespace TinyFarm.Core;

public sealed record TinyFarmFoodEffect(int HealthRestore, int SpiritRestore);
public sealed record TinyFarmRecipeModifier(ProductId Ingredient, ProductId Output,
    int DifficultyBonus, string Description);
public sealed record TinyFarmCraftingRules(int Level, int BaseSpiritCost, int BaseExperience,
    IReadOnlyList<ProductId> StationSupplies, IReadOnlyList<TinyFarmRecipeModifier> Modifiers)
{
    public void Validate(CookingRecipeDefinition recipe, IReadOnlyList<ItemDefinition> items)
    {
        if (recipe.OutputCount is < 1 or > 99 || Level is < 0 or > 50 || BaseSpiritCost is < 1 or > 1000 || BaseExperience is < 1 or > 100
            || StationSupplies is null || Modifiers is null
            || StationSupplies.Distinct().Count() != StationSupplies.Count
            || StationSupplies.Any(product => !items.Any(item => item.Id == product))
            || Modifiers.Any(modifier => modifier is null)
            || Modifiers.Select(modifier => modifier.Ingredient).Distinct().Count() != Modifiers.Count
            || Modifiers.Any(modifier => modifier.DifficultyBonus is < 0 or > 50
                || Level + modifier.DifficultyBonus > 50 || string.IsNullOrWhiteSpace(modifier.Description)
                || recipe.Inputs.Any(input => input.Product == modifier.Ingredient)
                || StationSupplies.Contains(modifier.Ingredient)
                || !items.Any(item => item.Id == modifier.Ingredient)
                || !items.Any(item => item.Id == modifier.Output)))
        {
            throw new InvalidDataException("Invalid crafting difficulty, supplies or modifier: " + recipe.Id);
        }
    }
}

public sealed record TinyFarmRecipeKnowledge(CookingRecipeId Recipe, string Source);
public sealed record TinyFarmCraftingProgress(IReadOnlyList<TinyFarmRecipeKnowledge> KnownRecipes)
{
    public bool Knows(CookingRecipeId recipe) => KnownRecipes.Any(known => known.Recipe == recipe);
}

public enum TinyFarmConditionKind
{
    Exhausted
}

public sealed record TinyFarmActiveCondition(TinyFarmConditionKind Kind, SceneObjectId SourceStation);

public sealed record TinyFarmCraftQuote(CookingRecipeDefinition? Recipe, ProductId? Output,
    int Difficulty, int SpiritCost, int Experience, int FireExperience, bool Known,
    bool IngredientsAvailable, string Description);

/// <summary>Shared deterministic preview policy; the resolver recomputes it from semantic state.</summary>
public static class TinyFarmCrafting
{
    public const int CraftExperiencePerRank = 100;
    public const int ExperimentCost = 2;
    public const int BagSlots = 16;
    public const int StackLimit = 99;

    public static bool IsCraftSkill(TinyFarmSkill skill) => skill is TinyFarmSkill.Cooking or TinyFarmSkill.Forging
        or TinyFarmSkill.Alchemy or TinyFarmSkill.Carpentry or TinyFarmSkill.Leatherworking or TinyFarmSkill.Tailoring;

    public static int Rank(TinyFarmRpgProfile profile, TinyFarmSkill skill) =>
        profile.Skills.SingleOrDefault(progress => progress.Skill == skill)?.Rank ?? 0;

    public static bool IsSupplied(TinyFarmDefinitions definitions, ProductId product) =>
        definitions.CookingRecipes.Any(recipe => recipe.Crafting?.StationSupplies.Contains(product) == true);

    public static TinyFarmCraftQuote Quote(TinyFarmState state, TinyFarmDefinitions definitions, ActorId actor,
        IReadOnlyList<CookingRecipeInput> inputs)
    {
        TinyFarmRpgProfile? profile = state.Actor(actor).Rpg;
        bool available = inputs.All(input => IsSupplied(definitions, input.Product)
            || state.ProductCount(actor, input.Product) >= input.Count);
        foreach (CookingRecipeDefinition recipe in definitions.CookingRecipes.Where(recipe => recipe.Crafting is not null))
        {
            TinyFarmCraftingRules rules = recipe.Crafting!;
            foreach (TinyFarmRecipeModifier? modifier in new TinyFarmRecipeModifier?[] { null }.Concat(rules.Modifiers))
            {
                CookingRecipeInput[] required = modifier is null ? recipe.Inputs.ToArray()
                    : recipe.Inputs.Append(new CookingRecipeInput(modifier.Ingredient, 1)).ToArray();
                if (inputs.Count != required.Length || required.Any(input =>
                    !inputs.Any(candidate => candidate.Product == input.Product && candidate.Count == input.Count)))
                {
                    continue;
                }
                int difficulty = rules.Level + (modifier?.DifficultyBonus ?? 0);
                int rank = profile is null ? 0 : Rank(profile, TinyFarmSkill.Cooking);
                int gap = difficulty - rank;
                bool known = profile?.Crafting?.Knows(recipe.Id) == true;
                int cost = gap > 0 ? rules.BaseSpiritCost + 2 * gap + gap * gap
                    : Math.Max(1, rules.BaseSpiritCost + gap);
                if (!known)
                {
                    cost = (cost * 3 + 1) / 2;
                }
                int experience;
                if (gap < 0)
                {
                    experience = Math.Max(1, rules.BaseExperience / (1 + gap * gap));
                }
                else
                {
                    int challenge = Math.Min(9, gap);
                    experience = rules.BaseExperience * (1 << (challenge / 3)) * (3 + challenge % 3) / 3;
                }
                return new TinyFarmCraftQuote(recipe, modifier?.Output ?? recipe.OutputProduct, difficulty, cost,
                    experience, Math.Min(5, experience / 4), known, available,
                    modifier?.Description ?? "Plain soup; no seasoning.");
            }
        }
        return new TinyFarmCraftQuote(null, null, 0, ExperimentCost, 0, 0, false, available,
            "Unmatched combination: experiment costs 2 SP, keeps ingredients, grants no XP.");
    }

    public static TinyFarmRpgProfile Award(TinyFarmRpgProfile profile, TinyFarmSkill skill, int experience)
    {
        if (!profile.ProgressionEnabled || experience <= 0)
        {
            return profile;
        }
        TinyFarmSkillProgress progress = profile.Skills.SingleOrDefault(candidate => candidate.Skill == skill)
            ?? new TinyFarmSkillProgress(skill);
        int rank = progress.Rank;
        int remainder = checked(progress.Experience + experience);
        while (rank < 50 && remainder >= profile.ExperienceToNextRank(skill, rank))
        {
            remainder -= profile.ExperienceToNextRank(skill, rank);
            rank++;
        }
        if (rank == 50)
        {
            remainder = 0;
        }
        return profile with
        {
            Skills = profile.Skills.Where(candidate => candidate.Skill != skill)
                .Append(new TinyFarmSkillProgress(skill, rank, remainder)).OrderBy(candidate => candidate.Skill).ToArray()
        };
    }

    public static TinyFarmRpgProfile Recover(TinyFarmRpgProfile profile, int amount, bool rest = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        int spirit = rest ? profile.SpiritMaximum : (int)Math.Min(profile.SpiritMaximum, (long)profile.SpiritCurrent + amount);
        bool recovered = rest || spirit > 0 && (long)spirit * 100 >= (long)profile.SpiritMaximum * 20;
        return profile with
        {
            SpiritCurrent = spirit,
            ActiveConditions = recovered ? profile.ActiveConditions?.Where(condition => condition.Kind != TinyFarmConditionKind.Exhausted).ToArray()
                : profile.ActiveConditions
        };
    }
}
