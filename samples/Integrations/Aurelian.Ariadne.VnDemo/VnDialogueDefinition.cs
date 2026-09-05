using Ariadne.OptFlow;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace Aurelian.Ariadne.VnDemo;

public static class VnDialogueDefinition
{
    public const string DialogueId = "after-school.letter";
    public static readonly BbKey<string> Choice = new("after-school.choice");
    public static readonly BbKey<bool> Apologized = new("after-school.apologized");
    public static readonly BbKey<bool> LetterReturned = new("after-school.letter-returned");
    public static readonly BbKey<bool> Completed = new("after-school.completed");

    public static IReadOnlyList<AuthoredDialogueStep> Steps { get; } =
    [
        Step("after-school.intro", DialoguePresentationStepKind.Narration, null,
            "The last bell is gone. Sunset holds the classroom in amber.", null, null),
        Step("after-school.rei-angry", DialoguePresentationStepKind.Line, "Rei Kuroda",
            "You read my letter. Without asking.", "rei", "angry"),
        Step("after-school.mika-warning", DialoguePresentationStepKind.Line, "Mika Aono",
            "Rei, wait. Let them answer before this gets worse.", "mika", "concerned"),
        new AuthoredDialogueStep(
            "after-school.response",
            DialoguePresentationStepKind.Choice,
            null,
            "How do you answer?",
            "classroom.sunset",
            "rei",
            "angry",
            [
                Diag.Option("apologize", "Apologize and return the sealed letter"),
                Diag.Option("deflect", "Insist that it was already open"),
            ]),
        Step("after-school.apology", DialoguePresentationStepKind.Line, "Rei Kuroda",
            "...You kept the seal intact. Fine. I believe you.", "rei", "soft"),
        Step("after-school.deflect", DialoguePresentationStepKind.Line, "Rei Kuroda",
            "That is exactly the answer I was afraid you would give.", "rei", "angry"),
        Step("after-school.end", DialoguePresentationStepKind.Narration, null,
            "Outside, the track team starts another lap. Inside, a smaller story has changed course.", null, null),
    ];

    public static HfsmGraph CreateGraph()
    {
        var graph = new HfsmGraph { Root = "intro" };
        graph.Add("intro", Node(Intro));
        graph.Add("confrontation", Node(Confrontation));
        graph.Add("mika", Node(Mika));
        graph.Add("choice", Node(ResponseChoice));
        graph.Add("return-letter", Node(ReturnLetter));
        graph.Add("apology", Node(Apology));
        graph.Add("deflect", Node(Deflect));
        graph.Add("ending", Node(Ending));
        return graph;
    }

    private static IEnumerator<AiStep> Intro(AiCtx ctx)
    {
        yield return Line("after-school.intro");
        yield return Ai.Goto("confrontation");
    }

    private static IEnumerator<AiStep> Confrontation(AiCtx ctx)
    {
        yield return Line("after-school.rei-angry");
        yield return Ai.Push("mika");
        yield return Ai.Goto("choice");
    }

    private static IEnumerator<AiStep> Mika(AiCtx ctx)
    {
        yield return Line("after-school.mika-warning");
        yield return Ai.Pop();
    }

    private static IEnumerator<AiStep> ResponseChoice(AiCtx ctx)
    {
        AuthoredDialogueStep step = Get("after-school.response");
        yield return Diag.Choose(new DiagOperationId(step.Id), step.Text, step.Choices!, Choice);
        if (ctx.Bb.GetOrDefault(Choice, "") == "apologize")
        {
            ctx.Bb.Set(Apologized, true);
            yield return Ai.Goto("return-letter");
            yield break;
        }
        yield return Ai.Goto("deflect");
    }

    private static IEnumerator<AiStep> ReturnLetter(AiCtx ctx)
    {
        yield return Ai.Perform(Operation.Site("after-school.return-letter"), new ReturnLetterConsequence("sealed-letter"));
        ctx.Bb.Set(LetterReturned, true);
        yield return Ai.Goto("apology");
    }

    private static IEnumerator<AiStep> Apology(AiCtx ctx)
    {
        yield return Line("after-school.apology");
        yield return Ai.Goto("ending");
    }

    private static IEnumerator<AiStep> Deflect(AiCtx ctx)
    {
        yield return Line("after-school.deflect");
        yield return Ai.Goto("ending");
    }

    private static IEnumerator<AiStep> Ending(AiCtx ctx)
    {
        yield return Line("after-school.end");
        ctx.Bb.Set(Completed, true);
        yield return Ai.Succeed();
    }

    private static AiNode Node(Func<AiCtx, IEnumerator<AiStep>> run) => new(run);

    private static AiStep Line(string id)
    {
        AuthoredDialogueStep step = Get(id);
        return Diag.Line(new DiagOperationId(step.Id), step.Text, step.Speaker);
    }

    public static AuthoredDialogueStep Get(string id)
    {
        return Steps.Single(step => step.Id == id);
    }

    private static AuthoredDialogueStep Step(
        string id,
        DialoguePresentationStepKind kind,
        string? speaker,
        string text,
        string? portrait,
        string? expression)
    {
        return new AuthoredDialogueStep(
            id,
            kind,
            speaker,
            text,
            "classroom.sunset",
            portrait,
            expression);
    }
}

public sealed record ReturnLetterConsequence(string ItemId) : IActuationCommand;

public sealed class ReturnLetterConsequenceHandler : IActuationHandler<ReturnLetterConsequence>
{
    public int EmissionCount { get; private set; }

    public ActuatorHost.HandlerResult Handle(
        ActuatorHost host,
        AiCtx context,
        ActuationId id,
        ReturnLetterConsequence command)
    {
        EmissionCount++;
        return ActuatorHost.HandlerResult.CompletedOk();
    }
}
