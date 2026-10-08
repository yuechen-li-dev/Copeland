using Ariadne.OptFlow;
using Ariadne.OptFlow.Commands;
using Ariadne.OptFlow.Presentation;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Runtime;

namespace Aurelian.Ariadne.VnDemo;

public sealed record AuthoredDialogueStep(
    string Id,
    DialoguePresentationOperationKind Kind,
    string? Speaker,
    string Text,
    string BackgroundKey,
    string? PortraitKey,
    string? ExpressionKey,
    IReadOnlyList<DiagChoice>? Choices = null)
{
    public DialoguePresentationOperation Presentation => new(Id, Kind, Speaker, Text, Choices);
}

public sealed class DialogueSurfaceActuator :
    IActuationHandler<DiagLineCommand>,
    IActuationHandler<DiagChooseCommand>
{
    private readonly IReadOnlyDictionary<(string Text, string? Speaker), AuthoredDialogueStep> lines;
    private readonly IReadOnlyDictionary<string, AuthoredDialogueStep> choices;

    public DialogueSurfaceActuator(IEnumerable<AuthoredDialogueStep> definitions)
    {
        AuthoredDialogueStep[] materialized = definitions.ToArray();
        lines = materialized
            .Where(step => step.Kind == DialoguePresentationOperationKind.Line)
            .ToDictionary(step => (step.Text, step.Speaker));
        choices = materialized
            .Where(step => step.Kind == DialoguePresentationOperationKind.Choice)
            .ToDictionary(step => step.Text, StringComparer.Ordinal);
    }

    public AuthoredDialogueStep? ActiveStep { get; private set; }
    public AiAgent? ActiveAgent { get; private set; }
    public ActuationId? PendingId { get; private set; }
    public int DispatchCount { get; private set; }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, DiagLineCommand command)
    {
        ActiveStep = lines[(command.Text, command.Speaker)];
        return Begin(ctx.Agent, id);
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, DiagChooseCommand command)
    {
        ActiveStep = choices[command.Prompt];
        return Begin(ctx.Agent, id);
    }

    public void Restore(AiAgent agent, AuthoredDialogueStep step)
    {
        DiagOperationKind kind = step.Kind == DialoguePresentationOperationKind.Choice
            ? DiagOperationKind.Choose
            : DiagOperationKind.Line;
        long id = agent.Bb.GetOrDefault(Diag.Inspect(step.Id, kind).PendingIdKey, 0L);
        ActiveAgent = agent;
        ActiveStep = step;
        PendingId = id == 0 ? null : new ActuationId(id);
    }

    public void Complete(string? choiceId = null)
    {
        if (ActiveAgent is null || PendingId is not ActuationId id || ActiveStep is null)
        {
            throw new InvalidOperationException("There is no pending dialogue operation to complete.");
        }
        if (ActiveStep.Kind == DialoguePresentationOperationKind.Choice)
        {
            ActiveAgent.Events.Publish(new ActuationCompleted<string>(id, true, null, choiceId ?? ""));
        }
        ActiveAgent.Events.Publish(new ActuationCompleted(id, true, null, choiceId));
        ActiveAgent.InFlightActuations.Remove(new Dominatus.Core.Persistence.PendingActuation(id.Value, null));
        PendingId = null;
        ActiveStep = null;
    }

    private ActuatorHost.HandlerResult Begin(AiAgent agent, ActuationId id)
    {
        ActiveAgent = agent;
        PendingId = id;
        DispatchCount++;
        return ActuatorHost.HandlerResult.DeferredAccepted();
    }
}
