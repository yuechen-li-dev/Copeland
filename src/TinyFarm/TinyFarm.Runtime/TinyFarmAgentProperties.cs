namespace TinyFarm.Core;

public sealed record TinyFarmAgentProperty(string Group, string Name, string Value, string Source);

/// <summary>Read-only projection of real owners. Missing state is reported, never manufactured by the UI.</summary>
public static class TinyFarmAgentProperties
{
    public static IReadOnlyList<TinyFarmAgentProperty> Project(
        TinyFarmState state, TinyFarmDefinitions definitions, ActorId id)
    {
        ActorState actor = state.Actor(id);
        TinyFarmAgentInspection agent = TinyFarmAgentInspector.Inspect(state, definitions, id);
        var rows = new List<TinyFarmAgentProperty>();
        void Add(string group, string name, string value, string source)
        {
            rows.Add(new TinyFarmAgentProperty(group, name, value, source));
        }

        Add("Identity", "Name", actor.Name, "Agent");
        Add("Identity", "Agent ID", id.Value, "Agent");
        Add("Identity", "Template", actor.Agent?.TemplateId ?? "Opening cast / legacy", "Authoring");
        Add("Identity", "Kind", agent.Kind.ToString(), "Agent");
        Add("Identity", "Controller", agent.Control.ToString(), "Control binding");
        Add("Identity", "Level", agent.Level.ToString(), "Authored; growth pending");
        Add("Identity", "Scene", agent.Scene.Value, "Semantic placement");
        Add("Identity", "Position", $"{agent.Position.XUnits}, {agent.Position.YUnits}", "Fixed-point world units");
        Add("Identity", "Facing", state.ActorScene(id).Facing.ToString(), "Semantic placement");
        Add("Resources", "HP", agent.Health is null ? "Not authored" : $"{agent.Health.Current} / {agent.Health.Maximum}",
            actor.IsPlayer && state.Slice is not null ? "Live opening combat" : "Agent health");
        ActorEnergyState? energy = state.ActorEnergy.SingleOrDefault(entry => entry.Actor == id);
        Add("Resources", "Work energy", energy is null ? "Not applicable" : $"{energy.Energy} / {TinyFarmEnergy.MaximumUnits}",
            "Existing energy owner");
        Add("Resources", "Coins", actor.Money.ToString(), "Agent wallet");
        if (actor.Agent?.Container is TinyFarmContainerState container)
        {
            Add("Resources", "Container policy", container.Shipping ? "Shipping / daily 09:00" : "Storage", "Agent container capability");
            Add("Resources", "Opened by", container.OpenedBy?.Value ?? "Closed", "Container reducer");
            Add("Resources", "Last pickup day", container.LastCollectionDay.ToString(), "Persisted daily gate");
            Add("Resources", "Last shipment", $"{container.LastCollectionItems} items / {container.LastCollectionCoins} coins", "Shipping receipt");
        }

        if (actor.Rpg is TinyFarmRpgProfile rpg)
        {
            Add("Resources", "Spirit Points", $"{rpg.SpiritCurrent} / {rpg.SpiritMaximum}", rpg.Crafting is null ? "Authored; techniques pending" : "Live crafting / food / rest");
            Add("Stats", "Progression eligible", rpg.ProgressionEnabled ? "Yes" : "No", "Agent profile, independent of control");
            foreach (TinyFarmAbility ability in Enum.GetValues<TinyFarmAbility>())
            {
                Add("Stats", ability.ToString(), rpg.BaseAbilities.Get(ability).ToString(), "Authored base; modifiers pending");
            }
            foreach (TinyFarmSkill skill in Enum.GetValues<TinyFarmSkill>())
            {
                TinyFarmSkillProgress progress = rpg.Skills.SingleOrDefault(entry => entry.Skill == skill)
                    ?? new TinyFarmSkillProgress(skill);
                string next = progress.Rank == 50 ? "MAX" : $"{progress.Experience}/{rpg.ExperienceToNextRank(skill, progress.Rank)} XP";
                Add("Skills", skill.ToString(), $"Rank {progress.Rank} / {next}", rpg.Crafting is not null && skill is TinyFarmSkill.Cooking or TinyFarmSkill.Fire ? "Live cooking practice" : "Authored profile; practice pending");
            }
            foreach (string trait in rpg.Traits)
            {
                Add("Traits", trait, "Declared", "Agent profile; effects pending");
            }
            if (rpg.Traits.Count == 0)
            {
                Add("Traits", "Traits", "None", "Agent profile");
            }
        }
        else
        {
            Add("Stats", "RPG profile", agent.Kind == TinyFarmAgentKind.Object ? "Not applicable" : "Not authored in this save", "No inferred stats");
        }
        foreach (string condition in agent.Conditions)
        {
            Add("Conditions", condition, "Declared", "Legacy metadata; effects pending");
        }
        foreach (TinyFarmActiveCondition condition in actor.Rpg?.ActiveConditions ?? [])
        {
            Add("Conditions", condition.Kind.ToString(), "Active", "Clears on rest or recovery to 20% SP");
        }
        if (agent.Conditions.Count == 0 && actor.Rpg?.ActiveConditions?.Count is not > 0)
        {
            Add("Conditions", "Conditions", "None", "Agent metadata");
        }
        if (actor.IsPlayer && state.Slice is { Health: 0 })
        {
            Add("Conditions", "Zero HP", "Defeat/recovery policy", "Live combat, not a removable label");
        }
        Add("Equipment", "Weapon", Name(agent.Equipment.Weapon), "Current equipment owner");
        Add("Equipment", "Tool", Name(agent.Equipment.Tool), "Current equipment owner");
        foreach (TinyFarmInventoryRow item in agent.Inventory)
        {
            Add("Inventory", item.Name, $"{item.Quantity} / {item.Value} coins each", "Agent inventory");
        }
        if (agent.Inventory.Count == 0)
        {
            Add("Inventory", "Inventory", "Empty", "Agent inventory");
        }
        Add("Presentation", "Overworld sprite", agent.Appearance.OverworldSprite.ToString(), "Presentation");
        Add("Presentation", "Conversation sprite", agent.Appearance.ConversationSprite ?? "Not authored", "Presentation");
        Add("Presentation", "Walking animation", agent.Appearance.WalkingAnimation ? "Enabled" : "Disabled", "Presentation");
        Add("Presentation", "Visual scale", agent.Appearance.ScalePercent + "%", "Presentation; collision unchanged");
        if (agent.ObjectPose is not null)
        {
            Add("Presentation", "Object pose", agent.ObjectPose.ToString()!, "Agent pose");
        }
        return rows;

        string Name(ItemId? item) => item is null ? "None" : state.Item(item.Value).Name;
    }
}
