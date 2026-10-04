using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public enum TinyFarmAbility
{
    Strength, Agility, Constitution, Intelligence, Wisdom, Charisma
}

public enum TinyFarmSkill
{
    Farming, Woodcutting, Foraging, Fishing, Cooking, Forging, Alchemy,
    Carpentry, Leatherworking, Tailoring, Nourishment, Rest, Traversal,
    SocialInsight, Sword, Axe, Hammer, Scythe,
    Water, Fire, Earth, Storm, Wood, Light, Dark, Aether
}

public sealed record TinyFarmAbilities(
    int Strength = 10, int Agility = 10, int Constitution = 10,
    int Intelligence = 10, int Wisdom = 10, int Charisma = 10)
{
    public int Get(TinyFarmAbility ability) => ability switch
    {
        TinyFarmAbility.Strength => Strength,
        TinyFarmAbility.Agility => Agility,
        TinyFarmAbility.Constitution => Constitution,
        TinyFarmAbility.Intelligence => Intelligence,
        TinyFarmAbility.Wisdom => Wisdom,
        TinyFarmAbility.Charisma => Charisma,
        _ => throw new ArgumentOutOfRangeException(nameof(ability))
    };
}

public sealed record TinyFarmSkillProgress(TinyFarmSkill Skill, int Rank = 0, int Experience = 0);

/// <summary>Authored RPG foundation with optional A2 crafting progression; general action modifiers remain pending.</summary>
public sealed record TinyFarmRpgProfile(
    TinyFarmAbilities BaseAbilities,
    bool ProgressionEnabled,
    int SpiritCurrent,
    int SpiritMaximum,
    IReadOnlyList<TinyFarmSkillProgress> Skills,
    IReadOnlyList<string> Traits)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TinyFarmCraftingProgress? Crafting { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TinyFarmActiveCondition>? ActiveConditions { get; init; }
    public static TinyFarmRpgProfile Starter(bool progressionEnabled = true) => new(
        new TinyFarmAbilities(), progressionEnabled, 20, 20, [], []);

    public TinyFarmRpgProfile Copy() => this with
    {
        Skills = Skills.ToArray(), Traits = Traits.ToArray(),
        Crafting = Crafting is null ? null : Crafting with { KnownRecipes = Crafting.KnownRecipes.ToArray() },
        ActiveConditions = ActiveConditions?.ToArray()
    };

    public TinyFarmRpgProfile Canonical() => this with
    {
        Skills = Skills.OrderBy(skill => skill.Skill).ToArray(),
        Traits = Traits.OrderBy(trait => trait, StringComparer.Ordinal).ToArray(),
        Crafting = Crafting is null ? null : Crafting with
        {
            KnownRecipes = Crafting.KnownRecipes.OrderBy(known => known.Recipe.Value, StringComparer.Ordinal).ToArray()
        },
        ActiveConditions = ActiveConditions?.OrderBy(condition => condition.Kind).ToArray()
    };

    public void Validate()
    {
        if (BaseAbilities is null || Skills is null || Traits is null
            || SpiritMaximum < 0 || SpiritCurrent < 0 || SpiritCurrent > SpiritMaximum
            || Enum.GetValues<TinyFarmAbility>().Any(ability => BaseAbilities.Get(ability) is < 1 or > 100)
            || Skills.Any(skill => skill is null || !Enum.IsDefined(skill.Skill)
                || skill.Rank is < 0 or > 50 || skill.Experience < 0
                || skill.Rank == 50 && skill.Experience != 0
                || skill.Rank < 50 && skill.Experience >= ExperienceToNextRank(skill.Skill, skill.Rank))
            || Skills.Select(skill => skill.Skill).Distinct().Count() != Skills.Count
            || Traits.Any(trait => string.IsNullOrWhiteSpace(trait)
                || trait.Any(character => !char.IsAsciiLetterOrDigit(character)
                    && character is not '-' and not '_' and not '.'))
            || Traits.Distinct(StringComparer.Ordinal).Count() != Traits.Count)
        {
            throw new InvalidDataException("Invalid agent RPG profile: abilities, pools, ranks, XP or duplicate IDs.");
        }
        if (Crafting is not null && (Crafting.KnownRecipes is null
            || Crafting.KnownRecipes.Any(known => known is null || string.IsNullOrWhiteSpace(known.Recipe.Value) || string.IsNullOrWhiteSpace(known.Source))
            || Crafting.KnownRecipes.Select(known => known.Recipe).Distinct().Count() != Crafting.KnownRecipes.Count)
            || ActiveConditions is not null && (ActiveConditions.Any(condition => condition is null
                || !Enum.IsDefined(condition.Kind) || string.IsNullOrWhiteSpace(condition.SourceStation.Value))
                || ActiveConditions.Select(condition => condition.Kind).Distinct().Count() != ActiveConditions.Count))
        {
            throw new InvalidDataException("Invalid recipe knowledge or active condition state.");
        }
    }

    public int ExperienceToNextRank(TinyFarmSkill skill, int rank) => Crafting is not null && TinyFarmCrafting.IsCraftSkill(skill)
        ? TinyFarmCrafting.CraftExperiencePerRank : ExperienceToNextRank(rank);

    public static int ExperienceToNextRank(int rank) => 25 + 10 * rank + 5 * rank * rank;
}

[JsonSerializable(typeof(TinyFarmRpgProfile))]
internal partial class TinyFarmRpgJsonContext : JsonSerializerContext;
