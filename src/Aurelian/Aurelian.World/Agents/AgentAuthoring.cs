namespace Aurelian.World.Agents;

public enum AgentKind
{
    Character,
    Object,
    Creature,
}

public enum AgentControl
{
    Human,
    Autonomous,
    Passive,
}

/// <summary>Semantic defaults; controllers and domain state belong to the game.</summary>
public sealed record AgentTemplate(string Id, AgentKind Kind, AgentControl Control)
{
    public static AgentTemplate Character(string id, AgentControl control = AgentControl.Autonomous)
        => new(id, AgentKind.Character, control);

    public static AgentTemplate Object(string id)
        => new(id, AgentKind.Object, AgentControl.Passive);

    public static AgentTemplate Creature(string id)
        => new(id, AgentKind.Creature, AgentControl.Autonomous);
}

public sealed record AgentSpawn<TPlacement>(string Id, string Name, TPlacement Placement, AgentTemplate Template);

/// <summary>One authored identity and one authoritative typed state, without component bags.</summary>
public sealed record GameAgent<TState>(string Id, string Name, AgentTemplate Template, TState State);

/// <summary>
/// Generalizes TinyFarm's deterministic template/spawn creation. Failed batches publish no agents.
/// Domain callbacks validate placement and create state without mutating a live world.
/// </summary>
public static class AgentAuthoring
{
    public static IReadOnlyList<TDeclaration> OrderDeclarations<TDeclaration>(
        IEnumerable<TDeclaration> declarations, Func<TDeclaration, string> identity)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        var ordered = declarations.OrderBy(identity, StringComparer.Ordinal).ToArray();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (TDeclaration declaration in ordered)
        {
            string id = identity(declaration);
            ValidateIdentity(id, "instance ID");
            if (!identities.Add(id))
            {
                throw new InvalidDataException($"Agent authoring contains duplicate instance ID '{id}'.");
            }
        }
        return Array.AsReadOnly(ordered);
    }

    public static GameAgent<TState> Create<TPlacement, TState>(AgentSpawn<TPlacement> spawn,
        Action<TPlacement> validatePlacement, Func<AgentSpawn<TPlacement>, TState> createState)
    {
        ValidateSpawn(spawn, validatePlacement);
        return new GameAgent<TState>(spawn.Id, spawn.Name, spawn.Template, createState(spawn));
    }

    private static void ValidateSpawn<TPlacement>(AgentSpawn<TPlacement> spawn, Action<TPlacement> validatePlacement)
    {
        ValidateIdentity(spawn.Id, "instance ID");
        ValidateTemplate(spawn.Template);
        if (string.IsNullOrWhiteSpace(spawn.Name))
        {
            throw new InvalidDataException($"Agent '{spawn.Id}' needs a name.");
        }
        validatePlacement(spawn.Placement);
    }

    public static IReadOnlyList<GameAgent<TState>> CreateBatch<TPlacement, TState>(
        IEnumerable<AgentSpawn<TPlacement>> spawns, IEnumerable<string> existingIds,
        Action<TPlacement> validatePlacement, Func<AgentSpawn<TPlacement>, TState> createState)
    {
        var ordered = OrderDeclarations(spawns, spawn => spawn.Id);
        var existing = new HashSet<string>(existingIds, StringComparer.Ordinal);
        foreach (var spawn in ordered)
        {
            if (existing.Contains(spawn.Id))
            {
                throw new InvalidDataException($"Cannot spawn over existing agent '{spawn.Id}'.");
            }
            ValidateSpawn(spawn, validatePlacement);
        }
        return Array.AsReadOnly(ordered.Select(spawn =>
            new GameAgent<TState>(spawn.Id, spawn.Name, spawn.Template, createState(spawn))).ToArray());
    }

    public static void ValidateTemplate(AgentTemplate template)
    {
        ValidateIdentity(template.Id, "template ID");
        if (!Enum.IsDefined(template.Kind) || !Enum.IsDefined(template.Control)
            || template.Kind == AgentKind.Object && template.Control != AgentControl.Passive
            || template.Kind == AgentKind.Creature && template.Control != AgentControl.Autonomous)
        {
            throw new InvalidDataException($"Agent template '{template.Id}' has invalid kind/control defaults.");
        }
    }

    public static void ValidateHealth(int current, int maximum)
    {
        if (maximum <= 0 || current < 0 || current > maximum)
        {
            throw new InvalidDataException("Agent health must be between zero and a positive maximum.");
        }
    }

    public static void ValidateIdentity(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character =>
            !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
        {
            throw new InvalidDataException($"Agent {label} must use ASCII letters, digits, dot, underscore or hyphen.");
        }
    }
}
