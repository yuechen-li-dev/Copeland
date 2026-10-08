using System.Collections.Immutable;

namespace Aurelian.World.Scenes;

public sealed record SceneFrame(ImmutableArray<PlacedSceneBox> Boxes, ImmutableArray<PlacedSceneMesh> Meshes)
{
    public ImmutableArray<PlacedSceneModel> Models { get; init; } = [];
}

/// <summary>Explicit mount/spawn/despawn ownership. Rendering never reinitializes simulation state.</summary>
public sealed class SceneInstance : IDisposable
{
    private readonly Dictionary<string, SceneAgent> agents = new(StringComparer.Ordinal);
    private readonly HashSet<string> identities;
    private bool disposed;

    private SceneInstance(ScenePlan plan)
    {
        Plan = plan;
        identities = plan.Identities.ToHashSet(StringComparer.Ordinal);
    }

    public ScenePlan Plan { get; }
    public IReadOnlyList<SceneAgent> Agents => agents.Values.OrderBy(agent => agent.Id, StringComparer.Ordinal).ToArray();

    internal static SceneInstance Mount(ScenePlan plan)
    {
        var instance = new SceneInstance(plan);
        try
        {
            foreach (PlacedSceneAgent declaration in plan.Agents)
            {
                SceneAgent agent = declaration.Node.Create(declaration.Placement);
                instance.agents.Add(agent.Id, agent);
            }
            foreach (SceneAgent agent in instance.Agents)
            {
                agent.Attach();
            }
            return instance;
        }
        catch (Exception failure)
        {
            try
            {
                instance.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(failure, cleanupFailure);
            }
            throw;
        }
    }

    public SceneAgent<TState> Agent<TState>(string id)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!agents.TryGetValue(id, out SceneAgent? agent))
        {
            throw new KeyNotFoundException($"Scene agent '{id}' does not exist.");
        }
        if (agent is not SceneAgent<TState> typed)
        {
            throw new InvalidOperationException($"Scene agent '{id}' has a different state type.");
        }
        return typed;
    }

    public SceneAgent<TState> Spawn<TState>(string id, AgentDefinition<TState> definition,
        SceneTransform transform, string? name = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        SceneCompiler.ValidateSegment(id);
        if (identities.Contains(id))
        {
            throw new InvalidDataException($"Scene instance ID '{id}' already exists.");
        }
        var node = Scene.Agent(id, definition, name: name) with { Transform = transform };
        var placement = new ScenePlacement(id, node.Name, transform.Matrix());
        node.Validate(placement);
        var agent = (SceneAgent<TState>)node.Create(placement);
        try
        {
            agent.Attach();
        }
        catch (Exception failure)
        {
            try
            {
                agent.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(failure, cleanupFailure);
            }
            throw;
        }
        identities.Add(id);
        agents.Add(id, agent);
        return agent;
    }

    public bool Despawn(string id)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!agents.Remove(id, out SceneAgent? agent))
        {
            return false;
        }
        identities.Remove(id);
        agent.Dispose();
        return true;
    }

    public SceneFrame Project(Func<SceneAgent, bool>? visible = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var boxes = Plan.Boxes.ToBuilder();
        var meshes = Plan.Meshes.ToBuilder();
        var models = Plan.Models.ToBuilder();
        foreach (SceneAgent agent in Agents)
        {
            if (visible is not null && !visible(agent))
            {
                continue;
            }
            ScenePlan? visual = agent.ProjectBody();
            if (visual is null)
            {
                continue;
            }
            var transform = agent.WorldTransform;
            foreach (PlacedSceneBox box in visual.Boxes)
            {
                var world = box.WorldTransform * transform;
                SceneTransform.ValidateMatrix(world);
                boxes.Add(box with { Id = agent.Id + "." + box.Id, WorldTransform = world });
            }
            foreach (PlacedSceneModel model in visual.Models)
            {
                var world = model.WorldTransform * transform;
                SceneTransform.ValidateMatrix(world);
                models.Add(model with { Id = agent.Id + "." + model.Id, WorldTransform = world });
            }
            foreach (PlacedSceneMesh mesh in visual.Meshes)
            {
                var world = mesh.WorldTransform * transform;
                SceneTransform.ValidateMatrix(world);
                meshes.Add(mesh with { Id = agent.Id + "." + mesh.Id, WorldTransform = world });
            }
        }
        return new(boxes.ToImmutable(), meshes.ToImmutable()) { Models = models.ToImmutable() };
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        var failures = new List<Exception>();
        foreach (SceneAgent agent in Agents.Reverse())
        {
            try
            {
                agent.Dispose();
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }
        }
        agents.Clear();
        if (failures.Count > 0)
        {
            throw new AggregateException("Scene unmount failed to release one or more agents.", failures);
        }
    }
}
