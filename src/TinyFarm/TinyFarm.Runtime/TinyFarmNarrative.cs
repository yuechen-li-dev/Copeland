using Ariadne.OptFlow;
using Ariadne.OptFlow.Commands;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace TinyFarm.Core;

public sealed record NarrativeLine(string Speaker, string Text);

public static partial class TinyFarmNarrative
{
    private static readonly BbKey<string> Topic = new("TinyFarm.Narrative.Topic");

    public static FlowDefinition Definition { get; } = Define();

    [DominatusFlow("tiny-farm.favor-dialogue")]
    public static partial FlowDefinition Define();

    [DominatusState("Realize", Root = true)]
    private static IEnumerator<AiStep> Realize(AiCtx context)
    {
        string topic = context.Bb.GetOrDefault(Topic, DialogueTopic.Greeting.ToString());
        (string speaker, string text) = Surface(Enum.Parse<DialogueTopic>(topic));

        yield return Diag.Line(
            id: "tiny-farm.favor-dialogue.semantic-line",
            text,
            speaker);
        yield return Ai.Succeed();
    }

    public static IReadOnlyList<NarrativeLine> Project(IEnumerable<GameEvent> events)
    {
        var lines = new List<NarrativeLine>();

        foreach (DialogueTopic topic in events
                     .Where(gameEvent => gameEvent.Dialogue is not null)
                     .Select(gameEvent => gameEvent.Dialogue!.Value))
        {
            int initialLineCount = lines.Count;
            var handler = new CaptureLineHandler(lines);
            var host = new ActuatorHost();
            host.Register(handler);

            var agent = new AiAgent(Definition.CreateBrain());
            agent.Bb.Set(Topic, topic.ToString());
            var world = new AiWorld(host);
            world.Add(agent);

            for (int tick = 0; tick < 8 && lines.Count == initialLineCount; tick++)
            {
                world.Tick(0.01f);
            }
        }

        return lines;
    }

    private static (string Speaker, string Text) Surface(DialogueTopic topic)
    {
        return topic switch
        {
            DialogueTopic.RequestLetterDelivery => (
                "Mara",
                "The river road has kept Elias from the square. Will you carry this letter to him?"),
            DialogueTopic.EliasReceivesLetter => (
                "Elias",
                "Mara's seal. I had begun to think the river had swallowed every answer."),
            DialogueTopic.FavorThanks => (
                "Mara",
                "You brought an answer home before sunset. Take these coins, and my thanks."),
            DialogueTopic.ShopGreeting => (
                "Sela",
                "Everything on the counter has a price; the stories are free."),
            DialogueTopic.HarvestComment => (
                "Sela",
                "That harvest has the bright weight of careful mornings."),
            DialogueTopic.WeekComment => (
                "Mara",
                "A week turns quickly when every day leaves something growing."),
            _ => (
                "Neighbor",
                "Good day. The town is small, but no day in it is quite empty.")
        };
    }

    private sealed class CaptureLineHandler(List<NarrativeLine> lines) : IActuationHandler<DiagLineCommand>
    {
        public ActuatorHost.HandlerResult Handle(
            ActuatorHost host,
            AiCtx context,
            ActuationId id,
            DiagLineCommand command)
        {
            lines.Add(new NarrativeLine(command.Speaker ?? "Narrator", command.Text));
            return ActuatorHost.HandlerResult.CompletedOk();
        }
    }
}
