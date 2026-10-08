using System.Numerics;
using Aurelian.World.Agents;

namespace Aurelian.World.Scenes;

/// <summary>
/// Reusable immutable configuration. Factories must allocate fresh mutable state and policy instances.
/// Placement validation is pure; activation returns an owned lease detached on despawn or unmount.
/// </summary>
public abstract record AgentDefinition<TState>(AgentTemplate Template)
{
    /// <summary>Explicit version/configuration identity used by saves; never derived by reflection.</summary>
    public abstract string Identity { get; }

    public abstract TState CreateState(ScenePlacement placement);

    public virtual void ValidatePlacement(ScenePlacement placement) { }

    public virtual IDisposable? Activate(SceneAgent<TState> agent) => null;

    public virtual Matrix4x4 WorldTransform(TState state, ScenePlacement placement) => placement.WorldTransform;

    /// <summary>Presentation only. Bodies cannot contain agents or gameplay collision declarations.</summary>
    public virtual SceneGroup? Present(TState state) => null;
}

public abstract class SceneAgent : IDisposable
{
    private IDisposable? activation;
    private bool disposed;
    private SceneGroup? lastBody;
    private ScenePlan? lastBodyPlan;

    protected SceneAgent(ScenePlacement placement)
    {
        Placement = placement;
    }

    public string Id => Placement.Id;
    public ScenePlacement Placement { get; }
    public abstract Matrix4x4 WorldTransform { get; }
    internal abstract SceneGroup? Presentation { get; }
    internal abstract IDisposable? Activate();
    internal abstract void DisposeState();

    internal ScenePlan? ProjectBody()
    {
        SceneGroup? body = Presentation;
        if (body is null)
        {
            lastBody = null;
            lastBodyPlan = null;
            return null;
        }
        if (!ReferenceEquals(body, lastBody))
        {
            ScenePlan candidate = SceneCompiler.Compile(body);
            if (candidate.Agents.Length != 0 || candidate.Boxes.Any(box => box.Collision != SceneCollision.None))
            {
                throw new InvalidDataException($"Agent '{Id}' presentation cannot spawn agents or define collision.");
            }
            lastBody = body;
            lastBodyPlan = candidate;
        }
        return lastBodyPlan;
    }

    internal void Attach()
    {
        activation = Activate();
    }

    protected void RequireLive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        var failures = new List<Exception>();
        try
        {
            activation?.Dispose();
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
        try
        {
            DisposeState();
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
        if (failures.Count > 0)
        {
            throw new AggregateException($"Scene agent '{Id}' failed to release owned resources.", failures);
        }
    }
}

/// <summary>The single authoritative typed state of a scene agent.</summary>
public sealed class SceneAgent<TState> : SceneAgent
{
    private GameAgent<TState> agent;

    internal SceneAgent(ScenePlacement placement, AgentDefinition<TState> definition) : base(placement)
    {
        Definition = definition;
        agent = AgentAuthoring.Create(
            new AgentSpawn<ScenePlacement>(placement.Id, placement.Name, placement, definition.Template),
            _ => { }, spawn => definition.CreateState(spawn.Placement));
        if (agent.State is null)
        {
            throw new InvalidDataException($"Scene agent '{Id}' factory returned null state.");
        }
    }

    public AgentDefinition<TState> Definition { get; }
    public GameAgent<TState> Agent
    {
        get
        {
            RequireLive();
            return agent;
        }
    }

    public TState State
    {
        get => Agent.State;
        set
        {
            RequireLive();
            ArgumentNullException.ThrowIfNull(value);
            agent = agent with { State = value };
        }
    }

    public override Matrix4x4 WorldTransform
    {
        get
        {
            Matrix4x4 result = Definition.WorldTransform(State, Placement);
            SceneTransform.ValidateMatrix(result);
            return result;
        }
    }

    internal override SceneGroup? Presentation => Definition.Present(State);
    internal override IDisposable? Activate() => Definition.Activate(this);

    internal override void DisposeState()
    {
        if (agent.State is IDisposable state)
        {
            state.Dispose();
        }
    }
}
