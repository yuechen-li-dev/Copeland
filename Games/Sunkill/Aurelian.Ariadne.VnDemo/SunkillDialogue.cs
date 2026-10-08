using Ariadne.OptFlow.Dialogue;
using Ariadne.OptFlow.Presentation;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Runtime;

namespace Aurelian.Ariadne.VnDemo;

public enum DawnProtocol
{
    None,
    ImmediateShutter,
    StraussDelay,
}

public sealed record SunkillConsequence(DawnProtocol Protocol);

public static class SunkillDialogue
{
    public const string DialogueId = "sunkill.dawn-engine";
    public static readonly BbKey<string> SelectedProtocol = new("sunkill.selected-protocol");
    public static readonly BbKey<bool> DawnEngineTested = new("sunkill.dawn-engine-tested");
    public static readonly BbKey<bool> StraussWaitedFor = new("sunkill.strauss-waited-for");

    public static DialogueDefinition<SunkillConsequence> Definition { get; } =
        Dialogue.Define<SunkillConsequence>(
            DialogueId,
            dialogue =>
            {
                dialogue.Narrate(
                    "intro",
                    "New Mexico, 1946. The night shift has chained the sunrise to a machine and misplaced the instruction manual.");
                dialogue.Say(
                    "oppenheimer-arrival",
                    "oppenheimer",
                    "General, the Dawn Engine is charged. In six minutes it will either sterilize every vampire in the county or improve them.");
                dialogue.Say(
                    "groves-order",
                    "groves",
                    "Those are not adjacent outcomes, Doctor.");
                dialogue.Say(
                    "oppenheimer-shutters",
                    "oppenheimer",
                    "History rarely offers adjacent outcomes. It offers a lever and someone important arriving late.");
                dialogue.Narrate(
                    "countdown",
                    "Beyond the armored glass, the desert waits with the patience of a loaded gun.");
                dialogue.Choice("protocol", "The interlock key is warm in your hand.", choices =>
                {
                    choices.Option(
                        "open-shutters",
                        "Open the shutters. Test the Dawn Engine now.",
                        branch =>
                        {
                            branch.Emit(
                                "commit-immediate-shutter",
                                new SunkillConsequence(DawnProtocol.ImmediateShutter));
                            branch.Say(
                                "open-result",
                                "oppenheimer",
                                "Excellent. If the sun screams, record the pitch.");
                        });
                    choices.Option(
                        "wait-for-strauss",
                        "Wait until Strauss arrives. Regulations deserve a witness.",
                        branch =>
                        {
                            branch.Emit(
                                "commit-strauss-delay",
                                new SunkillConsequence(DawnProtocol.StraussDelay));
                            branch.Say(
                                "wait-result",
                                "groves",
                                "Lock the shutters. If Strauss is a vampire, this becomes very funny very quickly.");
                        });
                });
                dialogue.Say(
                    "oppenheimer-convergence",
                    "oppenheimer",
                    "Either way, dawn has been informed that it now reports to us.");
                dialogue.Narrate(
                    "end",
                    "Somewhere below the horizon, morning signs nothing and begins.");
                dialogue.End("return-to-menu");
            });

    public static DialogueLoweringResult<SunkillConsequence> Lowered { get; } =
        DialogueLowerer.Lower(Definition);

    public static IReadOnlyList<AuthoredDialogueStep> Steps { get; } =
    [
        Line("intro", null,
            "New Mexico, 1946. The night shift has chained the sunrise to a machine and misplaced the instruction manual.",
            portrait: null),
        Line("oppenheimer-arrival", "oppenheimer",
            "General, the Dawn Engine is charged. In six minutes it will either sterilize every vampire in the county or improve them.",
            portrait: "oppenheimer"),
        Line("groves-order", "groves",
            "Those are not adjacent outcomes, Doctor.",
            portrait: null),
        Line("oppenheimer-shutters", "oppenheimer",
            "History rarely offers adjacent outcomes. It offers a lever and someone important arriving late.",
            portrait: "oppenheimer"),
        Line("countdown", null,
            "Beyond the armored glass, the desert waits with the patience of a loaded gun.",
            portrait: null),
        new AuthoredDialogueStep(
            ChoiceOperationId("protocol"),
            DialoguePresentationOperationKind.Choice,
            null,
            "The interlock key is warm in your hand.",
            "sunkill.bunker",
            "oppenheimer",
            null,
            [
                new global::Ariadne.OptFlow.Commands.DiagChoice(
                    "open-shutters",
                    "Open the shutters. Test the Dawn Engine now."),
                new global::Ariadne.OptFlow.Commands.DiagChoice(
                    "wait-for-strauss",
                    "Wait until Strauss arrives. Regulations deserve a witness."),
            ]),
        Line("open-result", "oppenheimer",
            "Excellent. If the sun screams, record the pitch.",
            portrait: "oppenheimer"),
        Line("wait-result", "groves",
            "Lock the shutters. If Strauss is a vampire, this becomes very funny very quickly.",
            portrait: null),
        Line("oppenheimer-convergence", "oppenheimer",
            "Either way, dawn has been informed that it now reports to us.",
            portrait: "oppenheimer"),
        Line("end", null,
            "Somewhere below the horizon, morning signs nothing and begins.",
            portrait: null),
    ];

    public static AuthoredDialogueStep Get(string operationId)
    {
        return Steps.Single(step => step.Id == operationId);
    }

    public static DawnProtocol ReadProtocol(AiAgent agent)
    {
        string value = agent.Bb.GetOrDefault(SelectedProtocol, DawnProtocol.None.ToString());
        return Enum.TryParse(value, out DawnProtocol protocol) ? protocol : DawnProtocol.None;
    }

    private static AuthoredDialogueStep Line(
        string localId,
        string? speaker,
        string text,
        string? portrait)
    {
        return new AuthoredDialogueStep(
            LineOperationId(localId),
            DialoguePresentationOperationKind.Line,
            speaker,
            text,
            "sunkill.bunker",
            portrait,
            null);
    }

    private static string LineOperationId(string localId)
    {
        return $"dialogue.{DialogueId}.line.{localId}";
    }

    private static string ChoiceOperationId(string localId)
    {
        return $"dialogue.{DialogueId}.choice.{localId}";
    }
}

public sealed class SunkillConsequenceHandler :
    IActuationHandler<DialogueEffectCommand<SunkillConsequence>>
{
    public int EmissionCount { get; private set; }

    public ActuatorHost.HandlerResult Handle(
        ActuatorHost host,
        AiCtx context,
        ActuationId id,
        DialogueEffectCommand<SunkillConsequence> command)
    {
        EmissionCount++;
        context.Bb.Set(SunkillDialogue.SelectedProtocol, command.Consequence.Protocol.ToString());
        context.Bb.Set(
            SunkillDialogue.DawnEngineTested,
            command.Consequence.Protocol == DawnProtocol.ImmediateShutter);
        context.Bb.Set(
            SunkillDialogue.StraussWaitedFor,
            command.Consequence.Protocol == DawnProtocol.StraussDelay);
        return ActuatorHost.HandlerResult.CompletedOk();
    }
}
