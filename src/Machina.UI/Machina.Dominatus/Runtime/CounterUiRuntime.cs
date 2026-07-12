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

public sealed class CounterUiRuntime
{
    public static readonly BbKey<int> CountKey = new("counter.count");

    private readonly AiWorld _world;
    private readonly AiAgent _agent;
    private int _nextActionSequence;

    public CounterUiRuntime()
    {
        var graph = new HfsmGraph { Root = new StateId("Counter") }
            .Add(new StateId("Counter"), CounterNode);

        _agent = new AiAgent(new HfsmInstance(graph));
        _world = new AiWorld(new ActuatorHost());
        _world.Add(_agent);
        _agent.Bb.Set(CountKey, 0);
    }

    public int Count => _agent.Bb.GetOrDefault(CountKey, 0);

    public UiNode BuildUi()
    {
        var count = Count;

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
        _nextActionSequence++;
        _agent.Events.Publish(new UiActionEvent(action.Name, _nextActionSequence));
    }

    public void TickUntilIdle(int maxTicks = 1)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            _world.Tick(0f);
        }
    }

    private static IEnumerator<AiStep> CounterNode(AiCtx ctx)
    {
        var lastProcessedSequence = 0;

        while (true)
        {
            yield return Ai.Event<UiActionEvent>(
                filter: evt => evt.Sequence > lastProcessedSequence,
                onConsumed: (agent, evt) =>
                {
                    lastProcessedSequence = evt.Sequence;

                    if (!string.Equals(evt.Name, "increment", StringComparison.Ordinal))
                    {
                        return;
                    }

                    var currentCount = agent.Bb.GetOrDefault(CountKey, 0);
                    agent.Bb.Set(CountKey, currentCount + 1);
                },
                cursorStart: EventCursorStart.IncludeExisting);
        }
    }
}
