using Ariadne.OptFlow;
using Ariadne.OptFlow.Commands;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Runtime;

namespace Aurelian.Ariadne.VnDemo;

public enum DialoguePresentationStepKind
{
    Line,
    Narration,
    Choice,
    Terminal,
}

public sealed record DialoguePresentationChoice(string Id, string Text, int DeclarationIndex);

public sealed record DialoguePresentation(
    string DialogueId,
    string StepId,
    DialoguePresentationStepKind Kind,
    string? Speaker,
    string Text,
    string BackgroundKey,
    string? PortraitKey,
    string? ExpressionKey,
    IReadOnlyList<DialoguePresentationChoice> Choices,
    int SelectedChoiceIndex,
    bool CanAdvance,
    bool AutoEnabled,
    bool SkipEnabled,
    long? PendingActuationId);

public sealed record AuthoredDialogueStep(
    string Id,
    DialoguePresentationStepKind Kind,
    string? Speaker,
    string Text,
    string BackgroundKey,
    string? PortraitKey,
    string? ExpressionKey,
    IReadOnlyList<DiagChoice>? Choices = null)
{
    public DialoguePresentation Project(long? pendingActuationId, int selectedChoiceIndex, bool auto, bool skip)
    {
        DialoguePresentationChoice[] visibleChoices = (Choices ?? [])
            .Select((choice, index) => new DialoguePresentationChoice(choice.Key, choice.Text, index))
            .ToArray();
        int selected = visibleChoices.Length == 0
            ? 0
            : Math.Clamp(selectedChoiceIndex, 0, visibleChoices.Length - 1);
        return new DialoguePresentation(
            VnDialogueDefinition.DialogueId,
            Id,
            Kind,
            Speaker,
            Text,
            BackgroundKey,
            PortraitKey,
            ExpressionKey,
            visibleChoices,
            selected,
            Kind is DialoguePresentationStepKind.Line or DialoguePresentationStepKind.Narration,
            auto,
            skip,
            pendingActuationId);
    }
}

public sealed class DialoguePresentationProjector
{
    private readonly IReadOnlyDictionary<string, AuthoredDialogueStep> steps;

    public DialoguePresentationProjector(IEnumerable<AuthoredDialogueStep> steps)
    {
        this.steps = steps.ToDictionary(step => step.Id, StringComparer.Ordinal);
    }

    public DialoguePresentation Project(
        AiAgent agent,
        AuthoredDialogueStep? active,
        int selectedChoiceIndex,
        bool auto,
        bool skip,
        bool terminal = false)
    {
        if (terminal)
        {
            return new DialoguePresentation(
                VnDialogueDefinition.DialogueId,
                "after-school.terminal",
                DialoguePresentationStepKind.Terminal,
                null,
                "END OF SCENE",
                "classroom.sunset",
                null,
                null,
                [],
                0,
                false,
                auto,
                skip,
                null);
        }
        AuthoredDialogueStep resolved = active ?? RecoverPending(agent);
        DiagOperationKind operationKind = resolved.Kind == DialoguePresentationStepKind.Choice
            ? DiagOperationKind.Choose
            : DiagOperationKind.Line;
        DiagOperationInspection inspection = Diag.Inspect(resolved.Id, operationKind);
        long pending = agent.Bb.GetOrDefault(inspection.PendingIdKey, 0L);
        return resolved.Project(pending == 0 ? null : pending, selectedChoiceIndex, auto, skip);
    }

    public AuthoredDialogueStep RecoverPending(AiAgent agent)
    {
        foreach (AuthoredDialogueStep step in steps.Values.OrderBy(step => step.Id, StringComparer.Ordinal))
        {
            DiagOperationKind kind = step.Kind == DialoguePresentationStepKind.Choice
                ? DiagOperationKind.Choose
                : DiagOperationKind.Line;
            DiagOperationInspection inspection = Diag.Inspect(step.Id, kind);
            if (agent.Bb.GetOrDefault(inspection.StartedKey, false))
            {
                return step;
            }
        }
        string keys = string.Join(", ", agent.Bb.EnumerateSnapshotEntries().Select(entry => $"{entry.Key}={entry.Value}"));
        throw new InvalidOperationException($"No pending authored dialogue operation exists in restored semantic state. Blackboard keys: {keys}");
    }
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
            .Where(step => step.Kind is DialoguePresentationStepKind.Line or DialoguePresentationStepKind.Narration)
            .ToDictionary(step => (step.Text, step.Speaker));
        choices = materialized
            .Where(step => step.Kind == DialoguePresentationStepKind.Choice)
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
        DiagOperationKind kind = step.Kind == DialoguePresentationStepKind.Choice
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
        if (ActiveStep.Kind == DialoguePresentationStepKind.Choice)
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
