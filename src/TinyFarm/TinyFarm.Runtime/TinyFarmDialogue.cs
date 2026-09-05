using Ariadne.OptFlow;
using Ariadne.OptFlow.Commands;
using Ariadne.OptFlow.Presentation;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Persistence;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace TinyFarm.Core;

public enum TinyFarmDialogueAction
{
    Advance,
    ChoiceUp,
    ChoiceDown,
    Confirm,
    Cancel,
}

public sealed record TinyFarmDialogueInputRecord(int Index, TinyFarmDialogueAction Action);

public sealed record TinyFarmDialogueDominatusChunk(string Id, byte[] Payload);

public sealed record TinyFarmDialogueCheckpoint(
    string DialogueId,
    TinyFarmDialogueDominatusChunk[] DominatusChunks,
    int SelectedChoiceIndex,
    bool IsActive,
    bool IsCancelled,
    string? PendingOperationId = null);

public sealed record GiveMaraWildMintConsequence(ItemId Item) : IActuationCommand;

public static class TinyFarmDialogueProofState
{
    public static TinyFarmState Create(TinyFarmDefinitions definitions, bool hasWildMint)
    {
        TinyFarmState state = TinyFarmM21ControlStates.Create(definitions);
        ActorSceneState mara = state.ActorScene(TinyFarmIds.Mara);
        int playerSceneIndex = state.MutableActorScenes.FindIndex(item => item.Actor == TinyFarmIds.Player);
        state.MutableActorScenes[playerSceneIndex] = new ActorSceneState(
            TinyFarmIds.Player,
            mara.Scene,
            mara.WorldPosition,
            mara.Facing);
        int playerIndex = state.MutableActors.FindIndex(actor => actor.Id == TinyFarmIds.Player);
        state.MutableActors[playerIndex] = state.MutableActors[playerIndex] with
        {
            Location = TinyFarmIds.TownSquare,
            Inventory = hasWildMint ? [TinyFarmIds.WildMint] : []
        };
        int mintIndex = state.MutableItems.FindIndex(item => item.Id == TinyFarmIds.WildMint);
        state.MutableItems[mintIndex] = state.MutableItems[mintIndex] with
        {
            Owner = hasWildMint ? TinyFarmIds.Player : null,
            GroundLocation = hasWildMint ? null : TinyFarmIds.Riverside,
            GroundScene = null,
            GroundPosition = null
        };
        return state;
    }
}

public static class TinyFarmMaraDialogue
{
    public const string DialogueId = "tinyfarm.mara.wild-mint";
    public static readonly BbKey<bool> SupperSlice = new("mara-dialogue.supper-slice");
    public static readonly BbKey<bool> SupperDone = new("mara-dialogue.supper-done");
    public static readonly BbKey<bool> HasWildMint = new("mara-dialogue.has-wild-mint");
    public static readonly BbKey<string> Choice = new("mara-dialogue.choice");
    public static readonly BbKey<bool> ConsequenceAccepted = new("mara-dialogue.consequence-accepted");
    public static readonly BbKey<bool> MintTransferCompleted = new("mara-dialogue.mint-transfer-completed");
    public static readonly BbKey<bool> Completed = new("mara-dialogue.completed");

    public static IReadOnlyList<DialoguePresentationOperation> Operations { get; } =
    [
        Line("mara.supper-opening", "Mara", "A proper supper needs mushrooms, mint, and one fewer slime. Very traditional."),
        Line("mara.supper-help", "Mara", "Plant a turnip for tomorrow. Gather river mushrooms, cook them at home, and clear Old Burrow. Bring me the mint when you are ready."),
        Line("mara.supper-ready", "Mara", "The stove smells wonderful. The burrow is quiet. Is that the mint for our supper?"),
        Line("mara.supper-thanks", "Mara", "A seed for tomorrow, a meal for today. You made this place a little more like home. Supper is on me."),
        Line("mara.supper-after", "Mara", "You saved supper. Even the turnips are impressed, and they are a difficult audience."),
        Line("mara.greeting", "Mara", "Morning. The farm looks steadier every time I pass."),
        Line("mara.mint-notice", "Mara", "Is that wild mint? The kitchen has been missing its clean scent."),
        Line("mara.no-mint", "Mara", "The riverbank mint should be high enough to gather today."),
        Line("mara.shared-weather", "Mara", "Clouds are holding west. We should get a dry afternoon."),
        ChoiceOperation(
            "mara.mint-choice",
            "What do you say?",
            Diag.Option("give-mint", "Give Mara the wild mint"),
            Diag.Option("keep-mint", "Keep it for your own kitchen")),
        ChoiceOperation(
            "mara.town-choice",
            "What do you ask?",
            Diag.Option("ask-town", "Ask how town is doing"),
            Diag.Option("goodbye", "Say goodbye")),
        Line("mara.mint-thanks", "Mara", "Thank you. I will put it to good use before noon."),
        Line("mara.mint-rejected", "Mara", "It seems the mint is no longer yours to give."),
        Line("mara.mint-kept", "Mara", "Of course. Fresh mint is worth keeping."),
        Line("mara.town-answer", "Mara", "Quiet enough to hear the well rope creak. That is a good day."),
        Line("mara.goodbye", "Mara", "I will let you get back to the rows."),
    ];

    public static HfsmGraph CreateGraph()
    {
        var graph = new HfsmGraph { Root = "greeting" };
        graph.Add("greeting", Node(Greeting));
        graph.Add("weather", Node(Weather));
        graph.Add("branch", Node(Branch));
        graph.Add("mint-choice", Node(MintChoice));
        graph.Add("give-mint", Node(GiveMint));
        graph.Add("keep-mint", Node(KeepMint));
        graph.Add("town-choice", Node(TownChoice));
        graph.Add("town-answer", Node(TownAnswer));
        graph.Add("goodbye", Node(Goodbye));
        graph.Add("complete", Node(Complete));
        return graph;
    }

    private static IEnumerator<AiStep> Greeting(AiCtx context)
    {
        if (context.Bb.GetOrDefault(SupperDone, false))
        {
            yield return Show("mara.supper-after");
            yield return Ai.Goto("complete");
            yield break;
        }
        yield return Show(context.Bb.GetOrDefault(SupperSlice, false) ? "mara.supper-opening" : "mara.greeting");
        yield return Ai.Push("weather");
        yield return Ai.Goto("branch");
    }

    private static IEnumerator<AiStep> Weather(AiCtx context)
    {
        yield return Show("mara.shared-weather");
        yield return Ai.Pop();
    }

    private static IEnumerator<AiStep> Branch(AiCtx context)
    {
        if (context.Bb.GetOrDefault(HasWildMint, false))
        {
            yield return Show(context.Bb.GetOrDefault(SupperSlice, false) ? "mara.supper-ready" : "mara.mint-notice");
            yield return Ai.Goto("mint-choice");
            yield break;
        }

        yield return Show(context.Bb.GetOrDefault(SupperSlice, false) ? "mara.supper-help" : "mara.no-mint");
        yield return Ai.Goto("town-choice");
    }

    private static IEnumerator<AiStep> MintChoice(AiCtx context)
    {
        yield return Choose("mara.mint-choice");
        yield return Ai.Goto(context.Bb.GetOrDefault(Choice, "") == "give-mint" ? "give-mint" : "keep-mint");
    }

    private static IEnumerator<AiStep> GiveMint(AiCtx context)
    {
        if (!context.Bb.GetOrDefault(MintTransferCompleted, false))
        {
            yield return Ai.Perform(
                Operation.Site("mara.give-wild-mint"),
                new GiveMaraWildMintConsequence(TinyFarmIds.WildMint));
            context.Bb.Set(MintTransferCompleted, true);
        }
        yield return Show(context.Bb.GetOrDefault(ConsequenceAccepted, false)
            ? (context.Bb.GetOrDefault(SupperSlice, false) ? "mara.supper-thanks" : "mara.mint-thanks")
            : "mara.mint-rejected");
        yield return Ai.Goto("complete");
    }

    private static IEnumerator<AiStep> KeepMint(AiCtx context)
    {
        yield return Show("mara.mint-kept");
        yield return Ai.Goto("complete");
    }

    private static IEnumerator<AiStep> TownChoice(AiCtx context)
    {
        yield return Choose("mara.town-choice");
        yield return Ai.Goto(context.Bb.GetOrDefault(Choice, "") == "ask-town" ? "town-answer" : "goodbye");
    }

    private static IEnumerator<AiStep> TownAnswer(AiCtx context)
    {
        yield return Show("mara.town-answer");
        yield return Ai.Goto("complete");
    }

    private static IEnumerator<AiStep> Goodbye(AiCtx context)
    {
        yield return Show("mara.goodbye");
        yield return Ai.Goto("complete");
    }

    private static IEnumerator<AiStep> Complete(AiCtx context)
    {
        context.Bb.Set(Completed, true);
        yield return Ai.Succeed();
    }

    private static AiNode Node(Func<AiCtx, IEnumerator<AiStep>> run) => new(run);

    private static AiStep Show(string id)
    {
        DialoguePresentationOperation operation = Get(id);
        return Diag.Line(new DiagOperationId(id), operation.Text, operation.SpeakerId);
    }

    private static AiStep Choose(string id)
    {
        DialoguePresentationOperation operation = Get(id);
        return Diag.Choose(new DiagOperationId(id), operation.Text, operation.Choices!, Choice);
    }

    public static DialoguePresentationOperation Get(string id)
    {
        return Operations.Single(operation => operation.Id == id);
    }

    private static DialoguePresentationOperation Line(string id, string? speaker, string text)
    {
        return new DialoguePresentationOperation(
            id,
            DialoguePresentationOperationKind.Line,
            speaker,
            text);
    }

    private static DialoguePresentationOperation ChoiceOperation(
        string id,
        string prompt,
        params DiagChoice[] choices)
    {
        return new DialoguePresentationOperation(
            id,
            DialoguePresentationOperationKind.Choice,
            "Mara",
            prompt,
            choices);
    }
}

public sealed class TinyFarmDialogueCoordinator
{
    private readonly TinyFarmSimulationHost host;
    private readonly DialoguePresentationProjector projector = new(
        TinyFarmMaraDialogue.DialogueId,
        TinyFarmMaraDialogue.Operations);
    private ActuatorHost actuatorHost = null!;
    private AiWorld world = null!;
    private AiAgent agent = null!;
    private TinyFarmDialogueSurface surface = null!;
    private TinyFarmDialogueConsequenceHandler consequence = null!;
    private readonly List<TinyFarmDialogueInputRecord> inputTape = [];
    private readonly List<string> trace = [];
    private DialoguePresentationSnapshot? cachedPresentation;

    public TinyFarmDialogueCoordinator(TinyFarmSimulationHost host)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public bool IsActive { get; private set; }
    public bool IsCancelled { get; private set; }
    public int SelectedChoiceIndex { get; private set; }
    public IntentResult? LastConsequenceResult => consequence?.LastResult;
    public int ConsequenceEmissionCount => consequence?.EmissionCount ?? 0;
    public int DialogueDispatchCount => surface?.DispatchCount ?? 0;
    public IReadOnlyList<TinyFarmDialogueInputRecord> InputTape => inputTape;
    public IReadOnlyList<string> Trace => trace;
    public ActorId? SpeakingActor => IsActive ? TinyFarmIds.Mara : null;

    public DialoguePresentationSnapshot? Presentation => IsActive ? cachedPresentation : null;

    public bool TryBeginFrom(TinyFarmStepResult step)
    {
        ArgumentNullException.ThrowIfNull(step);
        bool maraConversationAccepted = step.Results.Any(result =>
            result.Status == IntentResultStatus.Accepted
            && result.Envelope.Intent is InteractIntent
            && result.Events.Any(item =>
                item.Kind == GameEventKind.Conversation
                && item.Target == TinyFarmIds.Mara));
        if (!maraConversationAccepted || IsActive)
        {
            return false;
        }

        Begin();
        return true;
    }

    public void Apply(TinyFarmDialogueAction action, bool record = true)
    {
        if (!IsActive)
        {
            return;
        }
        if (record)
        {
            inputTape.Add(new TinyFarmDialogueInputRecord(inputTape.Count, action));
        }

        switch (action)
        {
            case TinyFarmDialogueAction.ChoiceUp:
                MoveChoice(-1);
                break;
            case TinyFarmDialogueAction.ChoiceDown:
                MoveChoice(1);
                break;
            case TinyFarmDialogueAction.Advance:
            case TinyFarmDialogueAction.Confirm:
                CompleteCurrent();
                break;
            case TinyFarmDialogueAction.Cancel:
                IsCancelled = true;
                IsActive = false;
                cachedPresentation = null;
                break;
        }
    }

    public TinyFarmDialogueCheckpoint Capture()
    {
        if (!IsActive)
        {
            return new TinyFarmDialogueCheckpoint(
                TinyFarmMaraDialogue.DialogueId,
                [],
                SelectedChoiceIndex,
                false,
                IsCancelled,
                null);
        }

        TinyFarmDialogueDominatusChunk[] chunks = DominatusSave
            .CreateCheckpointChunks(DominatusCheckpointBuilder.Capture(world))
            .Select(chunk => new TinyFarmDialogueDominatusChunk(chunk.Id.Value, chunk.Payload))
            .ToArray();
        return new TinyFarmDialogueCheckpoint(
            TinyFarmMaraDialogue.DialogueId,
            chunks,
            SelectedChoiceIndex,
            true,
            IsCancelled,
            Presentation?.OperationId);
    }

    public void Restore(TinyFarmDialogueCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.DialogueId != TinyFarmMaraDialogue.DialogueId)
        {
            throw new InvalidDataException($"Unknown TinyFarm dialogue '{checkpoint.DialogueId}'.");
        }

        IsCancelled = checkpoint.IsCancelled;
        SelectedChoiceIndex = checkpoint.SelectedChoiceIndex;
        if (!checkpoint.IsActive)
        {
            IsActive = false;
            cachedPresentation = null;
            return;
        }

        BuildWorld();
        SaveChunk[] chunks = checkpoint.DominatusChunks
            .Select(chunk => new SaveChunk(new ChunkId(chunk.Id), chunk.Payload))
            .ToArray();
        DominatusCheckpoint restored = DominatusSave.ReadCheckpointChunks(chunks).checkpoint;
        DominatusCheckpointBuilder.Restore(world, restored);
        agent.Bb.Set(
            TinyFarmMaraDialogue.ConsequenceAccepted,
            checkpoint.PendingOperationId == "mara.mint-thanks"
            || host.Session.State.Item(TinyFarmIds.WildMint).Owner == TinyFarmIds.Mara);
        DialoguePresentationOperation pending = checkpoint.PendingOperationId is string operationId
            ? TinyFarmMaraDialogue.Get(operationId)
            : projector.RecoverPending(agent);
        world.Tick(0);
        surface.Restore(agent, pending);
        IsActive = true;
        Reproject();
        RecordPresentation();
    }

    private void Begin()
    {
        BuildWorld();
        bool hasWildMint = host.Session.State.Actors
            .Single(actor => actor.Id == TinyFarmIds.Player)
            .Inventory.Contains(TinyFarmIds.WildMint);
        bool supper = host.Session.State.Facts.Contains(WorldFact.SupperRequested);
        agent.Bb.Set(TinyFarmMaraDialogue.SupperSlice, supper);
        agent.Bb.Set(TinyFarmMaraDialogue.SupperDone, TinyFarmSupper.IsComplete(host.Session.State));
        agent.Bb.Set(TinyFarmMaraDialogue.HasWildMint, supper ? TinyFarmSupper.IsReady(host.Session.State) : hasWildMint);
        IsActive = true;
        IsCancelled = false;
        SelectedChoiceIndex = 0;
        cachedPresentation = null;
        TickUntilPresentation();
    }

    private void BuildWorld()
    {
        surface = new TinyFarmDialogueSurface(TinyFarmMaraDialogue.Operations);
        consequence = new TinyFarmDialogueConsequenceHandler(host);
        actuatorHost = new ActuatorHost();
        actuatorHost.Register<DiagLineCommand>(surface);
        actuatorHost.Register<DiagChooseCommand>(surface);
        actuatorHost.Register<GiveMaraWildMintConsequence>(consequence);
        world = new AiWorld(actuatorHost);
        agent = new AiAgent(new HfsmInstance(TinyFarmMaraDialogue.CreateGraph()));
        world.Add(agent);
    }

    private void CompleteCurrent()
    {
        DialoguePresentationSnapshot presentation = Presentation
            ?? throw new InvalidOperationException("Dialogue presentation is not active.");
        if (presentation.IsAwaitingChoice)
        {
            surface.Complete(presentation.Choices[presentation.SelectedChoiceIndex].Id);
        }
        else if (presentation.CanAdvance)
        {
            surface.Complete();
        }
        else
        {
            return;
        }

        SelectedChoiceIndex = 0;
        TickUntilPresentation();
    }

    private void MoveChoice(int delta)
    {
        int count = Presentation?.Choices.Count ?? 0;
        if (count > 0)
        {
            SelectedChoiceIndex = (SelectedChoiceIndex + delta + count) % count;
            Reproject();
        }
    }

    private void TickUntilPresentation()
    {
        int guard = 0;
        do
        {
            world.Tick(0);
            guard++;
        }
        while (!agent.Bb.GetOrDefault(TinyFarmMaraDialogue.Completed, false)
               && surface.PendingId is null
               && guard < 32);

        if (guard >= 32)
        {
            throw new InvalidOperationException("TinyFarm dialogue failed to converge.");
        }
        if (agent.Bb.GetOrDefault(TinyFarmMaraDialogue.Completed, false))
        {
            IsActive = false;
            cachedPresentation = null;
            return;
        }
        Reproject();
        RecordPresentation();
    }

    private void RecordPresentation()
    {
        string? operationId = Presentation?.OperationId;
        if (operationId is not null && (trace.Count == 0 || trace[^1] != operationId))
        {
            trace.Add(operationId);
        }
    }

    private void Reproject()
    {
        cachedPresentation = projector.Project(
            agent,
            surface.ActiveOperation,
            SelectedChoiceIndex,
            completed: false,
            cancelled: IsCancelled);
    }
}

internal sealed class TinyFarmDialogueConsequenceHandler : IActuationHandler<GiveMaraWildMintConsequence>
{
    private readonly TinyFarmSimulationHost host;

    public TinyFarmDialogueConsequenceHandler(TinyFarmSimulationHost host)
    {
        this.host = host;
    }

    public int EmissionCount { get; private set; }
    public IntentResult? LastResult { get; private set; }

    public ActuatorHost.HandlerResult Handle(
        ActuatorHost host,
        AiCtx context,
        ActuationId id,
        GiveMaraWildMintConsequence command)
    {
        EmissionCount++;
        GameIntent intent = this.host.Session.State.Facts.Contains(WorldFact.SupperRequested)
            ? new CompleteSupperIntent()
            : new GiveIntent(command.Item, TinyFarmIds.Mara);
        TinyFarmStepResult step = this.host.ExecuteIntent(intent);
        LastResult = step.Results.Single(result => result.Envelope.Source == IntentSourceKind.Human);
        context.Bb.Set(
            TinyFarmMaraDialogue.ConsequenceAccepted,
            LastResult.Status == IntentResultStatus.Accepted);
        return ActuatorHost.HandlerResult.CompletedOk();
    }
}

internal sealed class TinyFarmDialogueSurface :
    IActuationHandler<DiagLineCommand>,
    IActuationHandler<DiagChooseCommand>
{
    private readonly IReadOnlyDictionary<(string Text, string? Speaker), DialoguePresentationOperation> lines;
    private readonly IReadOnlyDictionary<string, DialoguePresentationOperation> choices;
    private AiAgent? activeAgent;

    public TinyFarmDialogueSurface(IEnumerable<DialoguePresentationOperation> operations)
    {
        DialoguePresentationOperation[] materialized = operations.ToArray();
        lines = materialized
            .Where(operation => operation.Kind == DialoguePresentationOperationKind.Line)
            .ToDictionary(operation => (operation.Text, operation.SpeakerId));
        choices = materialized
            .Where(operation => operation.Kind == DialoguePresentationOperationKind.Choice)
            .ToDictionary(operation => operation.Text, StringComparer.Ordinal);
    }

    public DialoguePresentationOperation? ActiveOperation { get; private set; }
    public ActuationId? PendingId { get; private set; }
    public int DispatchCount { get; private set; }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx context, ActuationId id, DiagLineCommand command)
    {
        ActiveOperation = lines[(command.Text, command.Speaker)];
        return Begin(context.Agent, id);
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx context, ActuationId id, DiagChooseCommand command)
    {
        ActiveOperation = choices[command.Prompt];
        return Begin(context.Agent, id);
    }

    public void Restore(AiAgent agent, DialoguePresentationOperation operation)
    {
        DiagOperationKind kind = operation.Kind == DialoguePresentationOperationKind.Choice
            ? DiagOperationKind.Choose
            : DiagOperationKind.Line;
        long id = agent.Bb.GetOrDefault(
            Diag.Inspect(new DiagOperationId(operation.Id), kind).PendingIdKey,
            0L);
        activeAgent = agent;
        ActiveOperation = operation;
        PendingId = id == 0 ? null : new ActuationId(id);
    }

    public void Complete(string? choiceId = null)
    {
        if (activeAgent is null || PendingId is not ActuationId id || ActiveOperation is null)
        {
            throw new InvalidOperationException("There is no pending TinyFarm dialogue operation.");
        }
        if (ActiveOperation.Kind == DialoguePresentationOperationKind.Choice)
        {
            activeAgent.Events.Publish(new ActuationCompleted<string>(id, true, null, choiceId ?? string.Empty));
        }
        activeAgent.Events.Publish(new ActuationCompleted(id, true, null, choiceId));
        activeAgent.InFlightActuations.Remove(new PendingActuation(id.Value, null));
        ActiveOperation = null;
        PendingId = null;
    }

    private ActuatorHost.HandlerResult Begin(AiAgent agent, ActuationId id)
    {
        activeAgent = agent;
        PendingId = id;
        DispatchCount++;
        return ActuatorHost.HandlerResult.DeferredAccepted();
    }
}
