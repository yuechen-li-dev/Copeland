using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Machina.Runtime.State;

namespace TinyFarm.InputMan;

/// <summary>
/// A transient Dominatus world for UI workflows, separate from the saved simulation.
/// HFSM frame entry creates a widget scope; frame cancellation disposes it.
/// </summary>
internal sealed class TinyFarmUiFlow : IDisposable
{
    private readonly AiWorld world = new();
    private readonly AiAgent agent;
    private readonly List<ScreenFrame> frames = new();
    private AiStep? navigation;
    private bool disposed;

    public TinyFarmUiFlow()
    {
        var graph = new HfsmGraph { Root = Id(TinyFarmScreen.Title) };
        foreach (TinyFarmScreen screen in Enum.GetValues<TinyFarmScreen>())
        {
            graph.Add(Id(screen), context => Enter(screen, context));
        }
        agent = new AiAgent(new HfsmInstance(graph));
        world.Add(agent);
        agent.Brain.Initialize(world, agent);
    }

    public TinyFarmScreen Screen
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return frames[^1].Screen;
        }
    }
    public UiStateScope Scope
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return frames[^1].Scope;
        }
    }
    public IReadOnlyList<TinyFarmScreen> Path => frames.Select(frame => frame.Screen).ToArray();

    public void Push(TinyFarmScreen screen)
    {
        Scope.Suspend();
        Navigate(new Push(Id(screen), "Open UI screen"));
    }

    public void Back()
    {
        if (frames.Count > 1)
        {
            Navigate(new Pop("Close UI screen"));
        }
    }

    public void ReturnTo(TinyFarmScreen screen)
    {
        if (!frames.Any(frame => frame.Screen == screen))
        {
            Reset(screen);
            return;
        }
        while (Screen != screen)
        {
            Back();
        }
    }

    public void Reset(TinyFarmScreen screen)
    {
        while (frames.Count > 1)
        {
            Back();
        }
        Navigate(new Goto(Id(screen), "Reset UI workflow"));
    }

    private void Navigate(AiStep step)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        navigation = step;
        world.Tick(0);
        if (navigation is not null)
        {
            throw new InvalidOperationException("Dominatus did not consume the UI navigation request.");
        }
    }

    private IEnumerator<AiStep> Enter(TinyFarmScreen screen, AiCtx context)
    {
        // This factory runs on HFSM entry, even if the node is never ticked before
        // another transition. Iterator finally blocks alone cannot cover that case.
        var frame = new ScreenFrame(screen, new UiStateScope());
        frames.Add(frame);
        context.Cancel.Register(() =>
        {
            try
            {
                frame.Scope.Dispose();
            }
            finally
            {
                frames.Remove(frame);
            }
        });
        return AwaitNavigation();
    }

    private IEnumerator<AiStep> AwaitNavigation()
    {
        while (true)
        {
            if (navigation is not { } next)
            {
                yield return new WaitUntil(_ => navigation is not null);
                continue;
            }
            navigation = null;
            yield return next;
        }
    }

    private static StateId Id(TinyFarmScreen screen)
    {
        return new StateId("ui." + screen);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        // An empty active path exits every Dominatus frame. Remove the agent too,
        // so this terminal UI world cannot tick and re-enter its root afterward.
        disposed = true;
        agent.Brain.RestoreActivePath(world, agent, []);
        world.Remove(agent);
    }

    private sealed record ScreenFrame(TinyFarmScreen Screen, UiStateScope Scope);
}
