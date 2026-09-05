using Aurelian.GameHost;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Persistence;
using Dominatus.Core.Runtime;
using InputMan.Aurelian;
using InputMan.Core;

namespace Aurelian.Ariadne.VnDemo;

public static class VnControls
{
    public static readonly ActionMapId Dialogue = new("VN.Dialogue");
    public static readonly ActionMapId Gameplay = new("VN.Gameplay");
    public static readonly ActionId Advance = new("VN.Advance");
    public static readonly ActionId Up = new("VN.Up");
    public static readonly ActionId Down = new("VN.Down");
    public static readonly ActionId Cancel = new("VN.Cancel");
    public static readonly ActionId Auto = new("VN.Auto");
    public static readonly ActionId Skip = new("VN.Skip");
    public static readonly ActionId Save = new("VN.Save");
    public static readonly ActionId Load = new("VN.Load");
    public static readonly ActionId GameplayMove = new("Gameplay.Move");

    public static InputProfile CreateProfile()
    {
        return Input.Profile(
        [
            Input.Map(Dialogue, 100,
            [
                Bind.Action(Controls.Key(KeyboardKey.Enter), Advance, consume: ConsumeMode.ControlOnly),
                Bind.Action(Controls.Key(KeyboardKey.Space), Advance, consume: ConsumeMode.ControlOnly),
                Bind.Action(Controls.Mouse(MouseButton.Primary), Advance, consume: ConsumeMode.ControlOnly),
                Bind.Action(Controls.Key(KeyboardKey.ArrowUp), Up, consume: ConsumeMode.ControlOnly),
                Bind.Action(Controls.Key(KeyboardKey.ArrowDown), Down, consume: ConsumeMode.ControlOnly),
                Bind.Action(Controls.Key(KeyboardKey.Escape), Cancel, consume: ConsumeMode.ControlOnly),
                Bind.Action(Controls.Key(KeyboardKey.A), Auto, consume: ConsumeMode.ControlOnly),
                Bind.Action(Controls.Key(KeyboardKey.S), Skip, consume: ConsumeMode.ControlOnly),
                Bind.Action(Controls.Key(KeyboardKey.F), Save, consume: ConsumeMode.ControlOnly),
                Bind.Action(Controls.Key(KeyboardKey.I), Load, consume: ConsumeMode.ControlOnly),
            ]),
            Input.Map(Gameplay, 10,
            [
                Bind.Action(Controls.Key(KeyboardKey.W), GameplayMove),
            ], canConsume: false),
        ]);
    }
}

public sealed class VnSession : IDisposable
{
    private readonly DialoguePresentationProjector projector = new(VnDialogueDefinition.Steps);
    private ActuatorHost host = null!;
    private AiWorld world = null!;
    private AiAgent agent = null!;
    private DialogueSurfaceActuator surface = null!;
    private ReturnLetterConsequenceHandler consequence = null!;
    private readonly InputManEngine inputEngine;
    private readonly AurelianInputAdapter inputAdapter;
    private ulong inputSequence;

    public VnSession()
    {
        inputEngine = new InputManEngine(VnControls.CreateProfile());
        inputAdapter = new AurelianInputAdapter(inputEngine);
        inputAdapter.SetContexts(VnControls.Dialogue);
        BuildWorld();
        TickUntilPresentation();
    }

    public bool AutoEnabled { get; private set; }
    public bool SkipEnabled { get; private set; }
    public int SelectedChoiceIndex { get; private set; }
    public bool GameplayInputLeaked { get; private set; }
    public int DialogueDispatchCount => surface.DispatchCount;
    public int ConsequenceEmissionCount => consequence.EmissionCount;
    public AiWorld World => world;
    public AiAgent Agent => agent;
    public DialogueSurfaceActuator Surface => surface;
    public bool IsTerminal => agent.Bb.GetOrDefault(VnDialogueDefinition.Completed, false);
    public Action? SaveRequested { get; set; }
    public Action? LoadRequested { get; set; }

    public DialoguePresentation Presentation => projector.Project(
        agent,
        surface.ActiveStep,
        SelectedChoiceIndex,
        AutoEnabled,
        SkipEnabled,
        IsTerminal);

    public void Advance()
    {
        DialoguePresentation presentation = Presentation;
        if (presentation.Kind == DialoguePresentationStepKind.Choice)
        {
            Choose(presentation.Choices[presentation.SelectedChoiceIndex].Id);
            return;
        }
        if (presentation.CanAdvance)
        {
            surface.Complete();
            TickUntilPresentation();
        }
    }

    public void Choose(string choiceId)
    {
        DialoguePresentation presentation = Presentation;
        if (presentation.Kind != DialoguePresentationStepKind.Choice
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

    public void ToggleAuto() => AutoEnabled = !AutoEnabled;
    public void ToggleSkip() => SkipEnabled = !SkipEnabled;

    public void Cancel()
    {
        SkipEnabled = false;
    }

    public void RequestSave() => SaveRequested?.Invoke();
    public void RequestLoad() => LoadRequested?.Invoke();

    public void PulseAutomatic()
    {
        DialoguePresentation presentation = Presentation;
        if ((AutoEnabled || SkipEnabled) && presentation.CanAdvance)
            Advance();
    }

    public void ApplyInput(InputFrame frame)
    {
        if (frame.WasPressed(VnControls.Up)) MoveChoice(-1);
        if (frame.WasPressed(VnControls.Down)) MoveChoice(1);
        if (frame.WasPressed(VnControls.Auto)) ToggleAuto();
        if (frame.WasPressed(VnControls.Skip)) ToggleSkip();
        if (frame.WasPressed(VnControls.Cancel)) Cancel();
        if (frame.WasPressed(VnControls.Save)) RequestSave();
        if (frame.WasPressed(VnControls.Load)) RequestLoad();
        if (frame.WasPressed(VnControls.Advance)) Advance();
        GameplayInputLeaked |= frame.WasPressed(VnControls.GameplayMove);
    }

    public void Press(KeyboardKey key)
    {
        inputAdapter.RecordButton(Controls.Key(key), true);
        inputAdapter.BeginFrame(new AurelianHostFrame(++inputSequence, TimeSpan.FromMilliseconds(16), TimeSpan.FromMilliseconds(inputSequence * 16)));
        ApplyInput(inputAdapter.CurrentFrame);
        inputAdapter.RecordButton(Controls.Key(key), false);
        inputAdapter.BeginFrame(new AurelianHostFrame(++inputSequence, TimeSpan.FromMilliseconds(16), TimeSpan.FromMilliseconds(inputSequence * 16)));
    }

    public VnSessionCheckpoint Capture()
    {
        return new VnSessionCheckpoint(
            DominatusSave.CreateCheckpointChunks(DominatusCheckpointBuilder.Capture(world))
                .Select(chunk => new VnDominatusChunk(chunk.Id.Value, chunk.Payload))
                .ToArray(),
            SelectedChoiceIndex,
            AutoEnabled,
            SkipEnabled);
    }

    public void Restore(VnSessionCheckpoint checkpoint)
    {
        BuildWorld();
        Dominatus.Core.Persistence.SaveChunk[] chunks = checkpoint.DominatusChunks
            .Select(chunk => new Dominatus.Core.Persistence.SaveChunk(new ChunkId(chunk.Id), chunk.Payload))
            .ToArray();
        DominatusCheckpoint restored = DominatusSave.ReadCheckpointChunks(chunks).checkpoint;
        DominatusCheckpointBuilder.Restore(world, restored);
        SelectedChoiceIndex = checkpoint.SelectedChoiceIndex;
        AutoEnabled = checkpoint.AutoEnabled;
        SkipEnabled = checkpoint.SkipEnabled;
        AuthoredDialogueStep pending = projector.RecoverPending(agent);
        surface.Restore(agent, pending);
        world.Tick(0);
    }

    public void Dispose()
    {
        inputAdapter.Dispose();
    }

    private void BuildWorld()
    {
        surface = new DialogueSurfaceActuator(VnDialogueDefinition.Steps);
        consequence = new ReturnLetterConsequenceHandler();
        host = new ActuatorHost();
        host.Register<global::Ariadne.OptFlow.Commands.DiagLineCommand>(surface);
        host.Register<global::Ariadne.OptFlow.Commands.DiagChooseCommand>(surface);
        host.Register<ReturnLetterConsequence>(consequence);
        world = new AiWorld(host);
        agent = new AiAgent(new HfsmInstance(VnDialogueDefinition.CreateGraph()));
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
        while (!IsTerminal && surface.PendingId is null && guard < 32);
        if (guard >= 32)
        {
            throw new InvalidOperationException("Dialogue failed to converge to a presentation step.");
        }
    }
}

public sealed record VnDominatusChunk(string Id, byte[] Payload);

public sealed record VnSessionCheckpoint(
    VnDominatusChunk[] DominatusChunks,
    int SelectedChoiceIndex,
    bool AutoEnabled,
    bool SkipEnabled);
