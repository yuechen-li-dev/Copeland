using System.Security.Cryptography;
using System.Text;
using Ariadne.OptFlow.Dialogue;
using Ariadne.OptFlow.Presentation;
using Dominatus.Core.Persistence;
using Dominatus.Core.Runtime;

namespace Aurelian.Ariadne.VnDemo;

public sealed class VnSession : IDisposable
{
    private readonly DialoguePresentationProjector projector = new(
        SunkillDialogue.DialogueId,
        SunkillDialogue.Steps.Select(step => step.Presentation));
    private AiWorld world = null!;
    private AiAgent agent = null!;
    private DialogueSurfaceActuator surface = null!;
    private SunkillConsequenceHandler consequence = null!;

    public VnSession()
    {
        BuildWorld();
        TickUntilPresentation();
    }

    public int SelectedChoiceIndex { get; private set; }
    public int DialogueDispatchCount => surface.DispatchCount;
    public int ConsequenceEmissionCount => consequence.EmissionCount;
    public AiAgent Agent => agent;
    public bool IsTerminal => SunkillDialogue.Lowered.IsComplete(agent);
    public DawnProtocol Protocol => SunkillDialogue.ReadProtocol(agent);
    public bool DawnEngineTested => agent.Bb.GetOrDefault(SunkillDialogue.DawnEngineTested, false);
    public bool StraussWaitedFor => agent.Bb.GetOrDefault(SunkillDialogue.StraussWaitedFor, false);

    public DialoguePresentationSnapshot Presentation => projector.Project(
        agent,
        surface.ActiveStep?.Presentation,
        SelectedChoiceIndex,
        IsTerminal);

    public void Advance()
    {
        DialoguePresentationSnapshot presentation = Presentation;
        if (presentation.OperationKind == DialoguePresentationOperationKind.Choice)
        {
            Choose(presentation.Choices[presentation.SelectedChoiceIndex].Id);
            return;
        }

        if (!presentation.CanAdvance)
        {
            return;
        }

        surface.Complete();
        TickUntilPresentation();
    }

    public void Choose(string choiceId)
    {
        DialoguePresentationSnapshot presentation = Presentation;
        if (presentation.OperationKind != DialoguePresentationOperationKind.Choice
            || presentation.Choices.All(choice => choice.Id != choiceId))
        {
            throw new InvalidOperationException($"Choice '{choiceId}' is not visible.");
        }

        surface.Complete(choiceId);
        SelectedChoiceIndex = 0;
        TickUntilPresentation();
    }

    public void MoveChoice(int delta)
    {
        int count = Presentation.Choices.Count;
        if (count == 0)
        {
            return;
        }

        SelectedChoiceIndex = (SelectedChoiceIndex + delta + count) % count;
    }

    public VnSessionCheckpoint Capture()
    {
        return new VnSessionCheckpoint(
            DominatusSave.CreateCheckpointChunks(DominatusCheckpointBuilder.Capture(world))
                .Select(chunk => new VnDominatusChunk(chunk.Id.Value, chunk.Payload))
                .ToArray(),
            SelectedChoiceIndex);
    }

    public void Restore(VnSessionCheckpoint checkpoint)
    {
        BuildWorld();
        Dominatus.Core.Persistence.SaveChunk[] chunks = checkpoint.DominatusChunks
            .Select(chunk => new Dominatus.Core.Persistence.SaveChunk(
                new ChunkId(chunk.Id),
                chunk.Payload))
            .ToArray();
        DominatusCheckpoint restored = DominatusSave.ReadCheckpointChunks(chunks).checkpoint;
        DominatusCheckpointBuilder.Restore(world, restored);
        SelectedChoiceIndex = checkpoint.SelectedChoiceIndex;

        if (!IsTerminal)
        {
            DialoguePresentationOperation recovered = projector.RecoverPending(agent);
            surface.Restore(agent, SunkillDialogue.Get(recovered.Id));
        }

        world.Tick(0);
    }

    public string SemanticHash()
    {
        DialoguePresentationSnapshot presentation = Presentation;
        string canonical = string.Join(
            "|",
            SunkillDialogue.DialogueId,
            presentation.OperationId ?? "terminal",
            presentation.SelectedChoiceIndex,
            Protocol,
            DawnEngineTested,
            StraussWaitedFor,
            IsTerminal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    public void Dispose()
    {
    }

    private void BuildWorld()
    {
        surface = new DialogueSurfaceActuator(SunkillDialogue.Steps);
        consequence = new SunkillConsequenceHandler();
        var host = new ActuatorHost();
        host.Register<global::Ariadne.OptFlow.Commands.DiagLineCommand>(surface);
        host.Register<global::Ariadne.OptFlow.Commands.DiagChooseCommand>(surface);
        host.Register<DialogueEffectCommand<SunkillConsequence>>(consequence);
        world = new AiWorld(host);
        agent = new AiAgent(SunkillDialogue.Lowered.Flow.CreateBrain());
        world.Add(agent);
    }

    private void TickUntilPresentation()
    {
        int guard = 0;
        do
        {
            world.Tick(0);
            guard++;
        }
        while (!IsTerminal && surface.PendingId is null && guard < 64);

        if (guard >= 64)
        {
            throw new InvalidOperationException("Dialogue failed to converge to a presentation step.");
        }
    }
}

public sealed record VnDominatusChunk(string Id, byte[] Payload);

public sealed record VnSessionCheckpoint(
    VnDominatusChunk[] DominatusChunks,
    int SelectedChoiceIndex);
