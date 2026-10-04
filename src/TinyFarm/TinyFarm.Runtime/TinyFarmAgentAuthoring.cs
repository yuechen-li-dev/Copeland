namespace TinyFarm.Core;

public sealed record TinyFarmAgentItemSeed(string Key, string Name, int Price = 0,
    EquipmentSlot? Slot = null, bool Equip = false);
public sealed record TinyFarmAgentProductSeed(ProductId Product, int Count);
public sealed record TinyFarmAgentScheduleStop(int StartMinute, int EndMinute, SceneAnchorId Anchor, string Reason);
public sealed record TinyFarmAuthoredWorld(TinyFarmDefinitions Definitions, TinyFarmState State);

/// <summary>Reusable defaults. A spawn supplies identity and placement, never a second copy of world state.</summary>
public sealed record TinyFarmAgentTemplate
{
    public IReadOnlyList<TinyFarmAgentScheduleStop> Routine { get; init; } = [];
    public required string Id { get; init; }
    public TinyFarmAgentKind Kind { get; init; } = TinyFarmAgentKind.Character;
    public TinyFarmAgentControl Control { get; init; } = TinyFarmAgentControl.Idle;
    public TinyFarmAgentHealth? Health { get; init; } = new(12, 12);
    public int Level { get; init; } = 1;
    public int Money { get; init; }
    public int Energy { get; init; } = TinyFarmEnergy.InitialUnits;
    public IReadOnlyList<string> Conditions { get; init; } = [];
    public IReadOnlyList<TinyFarmAgentItemSeed> Items { get; init; } = [];
    public IReadOnlyList<TinyFarmAgentProductSeed> Products { get; init; } = [];
    public TinyFarmObjectPose? ObjectPose { get; init; }
    public TinyFarmAgentAppearance Appearance { get; init; } = new(TinyFarmAgentSprite.Gardener);

    public static TinyFarmAgentTemplate Character(string id)
    {
        return new TinyFarmAgentTemplate { Id = id };
    }

    public static TinyFarmAgentTemplate Object(string id)
    {
        return new TinyFarmAgentTemplate
        {
            Id = id,
            Kind = TinyFarmAgentKind.Object,
            Health = null,
            ObjectPose = TinyFarmObjectPose.Closed,
            Appearance = new TinyFarmAgentAppearance(TinyFarmAgentSprite.ObjectMarker, WalkingAnimation: false)
        };
    }

    public TinyFarmAgentSpawn Spawn(string id, string name, SceneId scene, GridPosition tile)
    {
        return new TinyFarmAgentSpawn(new ActorId(id), name, scene, ScenePosition.FromGrid(tile), this) { Routine = Routine };
    }
}

public sealed record TinyFarmAgentSpawn(
    ActorId Id,
    string Name,
    SceneId Scene,
    ScenePosition Position,
    TinyFarmAgentTemplate? Template,
    ActorFacing Facing = ActorFacing.Down,
    LocationId? Location = null)
{
    public IReadOnlyList<TinyFarmAgentScheduleStop> Routine { get; init; } = [];
    /// <summary>Re-author an existing placement without manufacturing new health or equipment truth.</summary>
    public static TinyFarmAgentSpawn PlaceExisting(ActorId id, string name, SceneId scene, ScenePosition position,
        ActorFacing facing = ActorFacing.Down, LocationId? location = null)
    {
        return new TinyFarmAgentSpawn(id, name, scene, position, null, facing, location);
    }
}

/// <summary>
/// Atomic authoring compiler into existing actor, placement, inventory and energy owners.
/// No resolver mutation, reflection, blackboard world truth or separate spawn registry.
/// </summary>
public static class TinyFarmAgentAuthoring
{
    public static TinyFarmAuthoredWorld Build(TinyFarmState source, TinyFarmDefinitions definitions,
        IEnumerable<TinyFarmAgentSpawn> declarations)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(declarations);
        TinyFarmAgentSpawn[] spawns = declarations.OrderBy(spawn => spawn.Id.Value, StringComparer.Ordinal).ToArray();
        var windows = definitions.Schedules.Windows.ToList();
        foreach (TinyFarmAgentSpawn spawn in spawns)
        {
            if (spawn.Routine is null)
            {
                throw Invalid(spawn, "has a null routine");
            }
            if (spawn.Routine.Count > 0 && spawn.Template?.Control != TinyFarmAgentControl.Schedule)
            {
                throw Invalid(spawn, "has a routine without a Schedule controller");
            }
            foreach (TinyFarmAgentScheduleStop stop in spawn.Routine)
            {
                if (stop.StartMinute < 0 || stop.EndMinute > 1440 || stop.StartMinute >= stop.EndMinute
                    || string.IsNullOrWhiteSpace(stop.Reason))
                {
                    throw Invalid(spawn, "has an invalid daily routine interval");
                }
                windows.Add(new TinyFarmScheduleWindow(spawn.Id, TinyFarmScheduleDay.EveryDay,
                    stop.StartMinute, stop.EndMinute, stop.Anchor, 100, stop.Reason));
            }
        }
        string json = System.Text.Json.JsonSerializer.Serialize(spawns,
            TinyFarmAgentAuthoringJsonContext.Default.TinyFarmAgentSpawnArray);
        string fingerprint = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(json)));
        var authoredDefinitions = new TinyFarmDefinitions(definitions.Identity + ";agents=" + fingerprint,
            definitions.Items, definitions.Crops, definitions.Scenes, definitions.SceneContent,
            new TinyFarmScheduleCatalog(windows, definitions.Schedules.Candidates), definitions.ScheduleContent,
            definitions.ForageNodes, definitions.CookingRecipes, definitions.Trees, definitions.Enemies);
        return new TinyFarmAuthoredWorld(authoredDefinitions, Compile(source, authoredDefinitions, spawns));
    }

    public static TinyFarmState Compile(TinyFarmState source, TinyFarmDefinitions definitions,
        IEnumerable<TinyFarmAgentSpawn> declarations)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(declarations);
        if (source.Version < TinyFarmState.DungeonCombatSaveVersion)
        {
            throw new InvalidDataException("Agent authoring requires a complete version-10 or later world.");
        }
        TinyFarmAgentSpawn[] spawns = declarations.OrderBy(spawn => spawn.Id.Value, StringComparer.Ordinal).ToArray();
        if (spawns.Select(spawn => spawn.Id).Distinct().Count() != spawns.Length)
        {
            throw new InvalidDataException("Agent authoring contains duplicate instance IDs.");
        }
        TinyFarmState copy = source.DeepCopy();
        var actors = copy.Actors.ToList();
        var placements = copy.ActorScenes.ToList();
        var energy = copy.ActorEnergy.ToList();
        var items = copy.Items.ToList();
        var products = copy.InventoryStacks.ToList();
        int version = source.Version;
        foreach (TinyFarmAgentSpawn spawn in spawns)
        {
            ValidateIdentity(spawn.Id.Value, "instance ID");
            if (string.IsNullOrWhiteSpace(spawn.Name) || spawn.Facing is < ActorFacing.Down or > ActorFacing.Up)
            {
                throw Invalid(spawn, "needs a name and a valid facing");
            }
            SceneDefinition scene;
            try
            {
                scene = definitions.Scenes.Get(spawn.Scene);
            }
            catch (KeyNotFoundException exception)
            {
                throw new InvalidDataException($"Agent '{spawn.Id}' references unknown scene '{spawn.Scene}'.", exception);
            }
            LocationId location = spawn.Location ?? TinyFarmScenes.LocationForScene(scene.Id);
            if (!TinyFarmScenes.SceneAgreesWithLocation(scene.Id, location)
                || !TinyFarmScenes.IsInBounds(scene, spawn.Position)
                || TinyFarmScenes.IsBlocked(scene, spawn.Position))
            {
                throw Invalid(spawn, "has a blocked/out-of-bounds placement or a mismatched semantic location");
            }
            int existing = actors.FindIndex(actor => actor.Id == spawn.Id);
            var placement = new ActorSceneState(spawn.Id, scene.Id, spawn.Position) { Facing = spawn.Facing };
            if (spawn.Template is null)
            {
                if (existing < 0)
                {
                    throw Invalid(spawn, "cannot place an existing agent that does not exist");
                }
                actors[existing] = actors[existing] with { Name = spawn.Name, Location = location };
                placements[placements.FindIndex(candidate => candidate.Actor == spawn.Id)] = placement;
                continue;
            }
            if (existing >= 0)
            {
                throw Invalid(spawn, "cannot spawn over an existing agent");
            }
            TinyFarmAgentTemplate template = spawn.Template;
            ValidateTemplate(spawn, template, definitions);
            var owned = new List<ItemId>();
            ItemId? weapon = null;
            ItemId? tool = null;
            foreach (TinyFarmAgentItemSeed seed in template.Items.OrderBy(seed => seed.Key, StringComparer.Ordinal))
            {
                ItemId item = new(spawn.Id.Value + ".item." + seed.Key);
                if (items.Any(candidate => candidate.Id == item))
                {
                    throw Invalid(spawn, $"item '{item}' collides with an existing item");
                }
                owned.Add(item);
                items.Add(new ItemState(item, seed.Name, seed.Price, null, spawn.Id, EquipmentSlot: seed.Slot));
                if (seed.Equip && seed.Slot == EquipmentSlot.Weapon)
                {
                    weapon = item;
                }
                if (seed.Equip && seed.Slot == EquipmentSlot.Tool)
                {
                    tool = item;
                }
            }
            var data = new TinyFarmAgentState(template.Id, template.Kind, template.Control, template.Health,
                template.Level, template.Conditions.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                new TinyFarmEquipment(weapon, tool), template.ObjectPose, template.Appearance);
            actors.Add(new ActorState(spawn.Id, spawn.Name, location, template.Money, owned,
                template.Control == TinyFarmAgentControl.Human, data));
            placements.Add(placement);
            if (template.Control != TinyFarmAgentControl.Human)
            {
                energy.Add(new ActorEnergyState(spawn.Id, template.Energy, false));
            }
            products.AddRange(template.Products.Select(seed => new InventoryStack(spawn.Id, seed.Product, seed.Count)));
            version = TinyFarmState.AgentAuthoringSaveVersion;
        }
        var result = new TinyFarmState(version, copy.Minute,
            actors.OrderBy(actor => actor.Id.Value, StringComparer.Ordinal).ToArray(), items, copy.Facts, copy.Favor,
            definitions.Identity, products, copy.ShopStock, copy.FarmPlots, placements, energy,
            copy.SelectedHotbarSlot, copy.ForageNodes, copy.Trees, copy.Enemies, copy.Slice, copy.Equipment);
        if (result.Actors.Count(actor => actor.IsPlayer) != 1 || !result.Actor(TinyFarmIds.Player).IsPlayer)
        {
            throw new InvalidDataException("The current TinyFarm host supports exactly one human binding, on the existing player ID.");
        }
        TinyFarmChunkedSaveCodec.ValidateWorld(result, definitions);
        return result;
    }

    internal static void ValidateAgent(ActorState actor)
    {
        TinyFarmAgentState agent = actor.Agent!;
        ValidateIdentity(agent.TemplateId, "template ID");
        if (agent.Kind is < TinyFarmAgentKind.Character or > TinyFarmAgentKind.Object
            || agent.Control is < TinyFarmAgentControl.Human or > TinyFarmAgentControl.Idle
            || agent.Level < 1 || agent.Conditions is null || agent.Equipment is null || agent.Appearance is null
            || agent.Health is { Current: < 0 } or { Maximum: <= 0 }
            || agent.Health is not null && agent.Health.Current > agent.Health.Maximum
            || agent.Conditions.Distinct(StringComparer.Ordinal).Count() != agent.Conditions.Count
            || actor.IsPlayer != (agent.Control == TinyFarmAgentControl.Human)
            || agent.Control == TinyFarmAgentControl.Human && actor.Id != TinyFarmIds.Player
            || agent.Kind == TinyFarmAgentKind.Object && (agent.Control != TinyFarmAgentControl.Idle || agent.ObjectPose is null)
            || agent.Kind == TinyFarmAgentKind.Character && agent.ObjectPose is not null
            || agent.ObjectPose is not null && agent.ObjectPose is not TinyFarmObjectPose.Closed and not TinyFarmObjectPose.Open
            || agent.Appearance.OverworldSprite is < TinyFarmAgentSprite.Gardener or > TinyFarmAgentSprite.ObjectMarker
            || agent.Appearance.ScalePercent is < 25 or > 400
            || agent.Equipment.Weapon is ItemId weapon && !actor.Inventory.Contains(weapon)
            || agent.Equipment.Tool is ItemId tool && !actor.Inventory.Contains(tool)
            || agent.Equipment.Weapon is not null && agent.Equipment.Weapon == agent.Equipment.Tool)
        {
            throw new InvalidDataException($"Agent '{actor.Id}' has invalid authored state or equipment ownership.");
        }
        foreach (string condition in agent.Conditions)
        {
            ValidateIdentity(condition, "condition ID");
        }
    }

    private static void ValidateTemplate(TinyFarmAgentSpawn spawn, TinyFarmAgentTemplate template, TinyFarmDefinitions definitions)
    {
        if (template.Items is null || template.Products is null || template.Conditions is null
            || template.Money < 0 || template.Energy is < TinyFarmEnergy.MinimumUnits or > TinyFarmEnergy.MaximumUnits
            || template.Items.Select(seed => seed.Key).Distinct(StringComparer.Ordinal).Count() != template.Items.Count
            || template.Products.Select(seed => seed.Product).Distinct().Count() != template.Products.Count
            || template.Items.Count(seed => seed.Equip && seed.Slot == EquipmentSlot.Weapon) > 1
            || template.Items.Count(seed => seed.Equip && seed.Slot == EquipmentSlot.Tool) > 1)
        {
            throw Invalid(spawn, "has invalid defaults, duplicate inventory keys or duplicate equipment slots");
        }
        foreach (TinyFarmAgentItemSeed item in template.Items)
        {
            ValidateIdentity(item.Key, "local item key");
            if (string.IsNullOrWhiteSpace(item.Name) || item.Price < 0
                || item.Slot is not null && item.Slot is not EquipmentSlot.Weapon and not EquipmentSlot.Tool
                || item.Equip && item.Slot is null)
            {
                throw Invalid(spawn, "has an invalid identity-item seed");
            }
        }
        foreach (TinyFarmAgentProductSeed seed in template.Products)
        {
            if (seed.Count <= 0 || !definitions.Items.Any(item => item.Id == seed.Product))
            {
                throw Invalid(spawn, $"has unknown product '{seed.Product}' or a non-positive stack");
            }
        }
        var data = new TinyFarmAgentState(template.Id, template.Kind, template.Control, template.Health,
            template.Level, template.Conditions, new TinyFarmEquipment(null, null), template.ObjectPose, template.Appearance);
        ValidateAgent(new ActorState(spawn.Id, spawn.Name, TinyFarmIds.Farmhouse, template.Money, [],
            template.Control == TinyFarmAgentControl.Human, data));
        if (template.Control == TinyFarmAgentControl.Schedule)
        {
            // Resolve every minute of the supported week at authoring time, so a missing window fails here.
            for (int minute = 0; minute < 7 * 1440; minute++)
            {
                try
                {
                    TinyFarmScheduleDecision decision = TinyFarmNpcSchedule.Decide(definitions.Schedules, spawn.Id, minute);
                    _ = definitions.Scenes.GetAnchor(decision.SelectedAnchor);
                }
                catch (Exception exception) when (exception is KeyNotFoundException or InvalidDataException or InvalidOperationException)
                {
                    throw new InvalidDataException($"Agent '{spawn.Id}' has an incomplete schedule at minute {minute}.", exception);
                }
            }
        }
    }

    private static void ValidateIdentity(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character =>
            !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
        {
            throw new InvalidDataException($"Agent {label} must use ASCII letters, digits, dot, underscore or hyphen.");
        }
    }

    private static InvalidDataException Invalid(TinyFarmAgentSpawn spawn, string reason)
    {
        return new InvalidDataException($"Agent '{spawn.Id}' {reason}.");
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(TinyFarmAgentSpawn[]))]
internal partial class TinyFarmAgentAuthoringJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
