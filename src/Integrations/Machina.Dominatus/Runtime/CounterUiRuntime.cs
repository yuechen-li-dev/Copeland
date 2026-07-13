using System.Collections;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Standard.Authoring;

namespace Machina.Dominatus.Runtime;

/// <summary>
/// Minimal integration smoke proof for Dominatus-hosted UI action ingress.
/// This is not a general component lifecycle API or a presenter dependency.
/// </summary>
public sealed class CounterUiRuntime
{
    public static readonly BbKey<int> CountKey = new("counter.count");

    private readonly AiWorld world;
    private readonly AiAgent agent;
    private int nextActionSequence;

    public CounterUiRuntime()
    {
        var graph = new HfsmGraph { Root = new StateId("Counter") }
            .Add(new StateId("Counter"), CounterNode);

        agent = new AiAgent(new HfsmInstance(graph));
        world = new AiWorld(new ActuatorHost());
        world.Add(agent);
        agent.Bb.Set(CountKey, 0);
    }

    public int Count => agent.Bb.GetOrDefault(CountKey, 0);

    public UiNode BuildUi()
    {
        int count = Count;

        return UI.Container(
            id: "root",
            child: StandardUI.Card(
                id: "counter-card",
                width: 320,
                height: 180,
                child: UI.Column(
                    id: "content",
                    gap: 12,
                    children:
                    [
                        UI.Text("Machina UI", id: "title", color: ColorToken.White, size: TextSize.H1),
                        UI.Text($"Count: {count}", id: "count", color: ColorToken.Gray, size: TextSize.Md),
                        StandardUI.Button("Increment", id: "increment", action: UiAction.Named("increment"))
                    ])));
    }

    public void SendAction(UiAction action)
    {
        nextActionSequence++;
        agent.Events.Publish(new UiActionEvent(action.Name, nextActionSequence));
    }

    public void TickUntilIdle(int maxTicks = 1)
    {
        for (int index = 0; index < maxTicks; index++)
        {
            world.Tick(0f);
        }
    }

    private static IEnumerator<AiStep> CounterNode(AiCtx context)
    {
        var lastProcessedSequence = 0;

        while (true)
        {
            yield return Ai.Event<UiActionEvent>(
                filter: inputEvent => inputEvent.Sequence > lastProcessedSequence,
                onConsumed: (agent, inputEvent) =>
                {
                    lastProcessedSequence = inputEvent.Sequence;

                    if (!string.Equals(inputEvent.Name, "increment", StringComparison.Ordinal))
                    {
                        return;
                    }

                    int currentCount = agent.Bb.GetOrDefault(CountKey, 0);
                    agent.Bb.Set(CountKey, currentCount + 1);
                },
                cursorStart: EventCursorStart.IncludeExisting);
        }
    }
}
