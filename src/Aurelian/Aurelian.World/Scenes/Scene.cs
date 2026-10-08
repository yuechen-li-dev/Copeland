using System.Collections.Immutable;
using System.Numerics;
using Aurelian.World.Agents;

namespace Aurelian.World.Scenes;

public enum SceneCollision
{
    None,
    Solid,
}

public abstract record SceneNode(string Id)
{
    public SceneTransform Transform { get; init; } = SceneTransform.Identity;
}

public sealed record SceneGroup(string Id, ImmutableArray<SceneNode> Children) : SceneNode(Id);
public sealed record SceneInstanceNode(string Id, SceneGroup Fragment) : SceneNode(Id);
public sealed record SceneBox(string Id, Vector3 HalfSize, Vector4 Color, SceneCollision Collision) : SceneNode(Id);
public readonly record struct SceneVertex(Vector3 Position, Vector3 Normal, Vector4 Color);
public sealed record SceneMesh(string Id, ImmutableArray<SceneVertex> Vertices) : SceneNode(Id);

public abstract record SceneAgentNode(string Id, string Name) : SceneNode(Id)
{
    public abstract AgentTemplate Template { get; }
    public abstract string DefinitionIdentity { get; }
    internal abstract void Validate(ScenePlacement placement);
    internal abstract SceneAgent Create(ScenePlacement placement);
}

public sealed record SceneAgentNode<TState>(string Id, string Name, AgentDefinition<TState> Definition)
    : SceneAgentNode(Id, Name)
{
    public override AgentTemplate Template => Definition.Template;
    public override string DefinitionIdentity => Definition.Identity;
    internal override void Validate(ScenePlacement placement)
    {
        ArgumentNullException.ThrowIfNull(Definition);
        AgentAuthoring.ValidateTemplate(Definition.Template);
        if (string.IsNullOrWhiteSpace(Definition.Identity))
        {
            throw new InvalidDataException($"Scene agent '{placement.Id}' needs a definition identity.");
        }
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidDataException($"Scene agent '{placement.Id}' needs a name.");
        }
        Definition.ValidatePlacement(placement);
    }

    internal override SceneAgent Create(ScenePlacement placement)
    {
        return new SceneAgent<TState>(placement, Definition);
    }
}

/// <summary>Ordinary immutable authoring; all helpers copy child collections.</summary>
public static class Scene
{
    public static SceneGroup World(string id, IEnumerable<SceneNode> children)
    {
        return Group(id, children);
    }

    public static SceneGroup Group(string id, IEnumerable<SceneNode> children, Vector3? at = null)
    {
        return new SceneGroup(id, children.ToImmutableArray())
        {
            Transform = SceneTransform.At(at ?? Vector3.Zero),
        };
    }

    public static SceneInstanceNode Instance(string id, SceneGroup fragment, Vector3? at = null)
    {
        return new SceneInstanceNode(id, fragment)
        {
            Transform = SceneTransform.At(at ?? Vector3.Zero),
        };
    }

    public static SceneBox Box(string id, Vector3 size, Vector4 color,
        Vector3? at = null, SceneCollision collision = SceneCollision.None)
    {
        return new SceneBox(id, size / 2, color, collision)
        {
            Transform = SceneTransform.At(at ?? Vector3.Zero),
        };
    }

    public static SceneMesh Mesh(string id, IEnumerable<SceneVertex> vertices, Vector3? at = null)
    {
        return new SceneMesh(id, vertices.ToImmutableArray())
        {
            Transform = SceneTransform.At(at ?? Vector3.Zero),
        };
    }

    public static SceneAgentNode<TState> Agent<TState>(string id, AgentDefinition<TState> definition,
        Vector3? at = null, string? name = null)
    {
        return new SceneAgentNode<TState>(id, name ?? id, definition)
        {
            Transform = SceneTransform.At(at ?? Vector3.Zero),
        };
    }
}
