using Aurelian.World.Scenes;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Runtime;

namespace Aurelian.Runtime.Dominatus.Inspection;

/// <summary>Scene activation leases on the existing inspected Dominatus world, with no extra scheduler.</summary>
public static class ScenePolicyBinding
{
    public static IDisposable Bind<TState>(SceneAgent<TState> agent, AurelianAgentRuntime runtime,
        Func<HfsmInstance> createBrain)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(createBrain);
        _ = agent.State;
        AiAgent policy = runtime.Add(agent.Id, createBrain());
        return new PolicyLease(runtime, agent.Id, policy);
    }

    private sealed class PolicyLease(AurelianAgentRuntime runtime, string id, AiAgent policy) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            runtime.RemoveIfOwned(id, policy);
        }
    }
}
