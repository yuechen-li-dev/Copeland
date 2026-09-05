using Aurelian.GameHost;
using Aurelian.Audio;
using Aurelian.Composition;
using Aurelian.Simulation;
using InputMan.Aurelian;
using InputMan.Core;
using TinyFarm.Core;
using TinyFarm.InputMan;
using Xunit;

namespace Aurelian.GameHost.Tests;

public sealed class GameHostInputM2Tests
{
    [Fact]
    public void KeyboardAndGamepad_LowerThroughSameLogicalMoveToTypedIntent()
    {
        SpatialMoveIntent keyboard = MoveWith(adapter => adapter.RecordButton(Controls.Key(KeyboardKey.W), true));
        SpatialMoveIntent gamepad = MoveWith(adapter =>
        {
            adapter.ConnectGamepad(0);
            adapter.RecordAxis(Controls.Gamepad(GamepadAxis.LeftY), 1f);
        });
        Assert.Equal(keyboard, gamepad);
    }

    [Fact]
    public void InputMoveFlowsThroughSpatialResolverToAuthoritativeWorldProjection()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        var session = new TinyFarmSession(
            TinyFarmM21ControlStates.Create(definitions),
            definitions);
        ScenePosition before = session.State.ActorScene(TinyFarmIds.Player).WorldPosition;
        var engine = new InputManEngine(GameControls.CreateProfile());
        using var adapter = new AurelianInputAdapter(engine);
        adapter.SetContexts(GameControls.Gameplay);
        adapter.RecordButton(Controls.Key(KeyboardKey.D), true);
        adapter.BeginFrame(Frame(1));
        SubmitGameIntent command = Assert.IsType<SubmitGameIntent>(
            Assert.Single(new TinyFarmInputController().Map(adapter.CurrentFrame)));

        TinyFarmStepResult step = session.Step(command.Intent, evaluateNpcDecisions: false);
        ActorSceneState authoritative = session.State.ActorScene(TinyFarmIds.Player);
        TinyFarmActorView projected = TinyFarmFrameProjector
            .Project(session.State, definitions)
            .Actors
            .Single(actor => actor.Id == TinyFarmIds.Player);

        Assert.Equal(IntentResultStatus.Accepted, step.Results.Single().Status);
        Assert.Equal(before.XUnits + ScenePosition.UnitsPerTile / 8, authoritative.WorldPosition.XUnits);
        Assert.Equal(before.YUnits, authoritative.WorldPosition.YUnits);
        Assert.Equal(authoritative.WorldPosition.XUnits, projected.Position.X);
        Assert.Equal(authoritative.WorldPosition.YUnits, projected.Position.Y);
    }

    [Fact]
    public void UiContextConsumesSharedConfirmAndGameplayResumesWhenClosed()
    {
        var engine = new InputManEngine(GameControls.CreateProfile());
        using var adapter = new AurelianInputAdapter(engine);
        var policy = new InputContextPolicy(adapter, GameControls.Gameplay, GameControls.Ui, GameControls.Rebind);
        LayerId uiLayer = new("tiny-farm-machina-ui");
        policy.Apply(
            new LayerInputRoutingResult(true, uiLayer, uiLayer, null, [uiLayer]),
            uiLayer,
            uiIsOpaque: true,
            rebinding: false);
        adapter.RecordButton(Controls.Key(KeyboardKey.E), true);
        adapter.BeginFrame(Frame(1));
        Assert.True(adapter.CurrentFrame.WasPressed(GameControls.UiConfirm));
        Assert.False(adapter.CurrentFrame.WasPressed(GameControls.Interact));
        var routedKeys = new List<LayerKeyChanged>();
        var uiBridge = new LogicalUiInputBridge(GameControls.UiConfirm, GameControls.UiCancel);
        IReadOnlyList<LayerInputRoutingResult> routed = uiBridge.Route(adapter.CurrentFrame, inputEvent =>
        {
            routedKeys.Add(Assert.IsType<LayerKeyChanged>(inputEvent));
            return new LayerInputRoutingResult(true, uiLayer, uiLayer, null, [uiLayer]);
        });
        Assert.Equal([new LayerKeyChanged(LayerKey.Enter, true), new LayerKeyChanged(LayerKey.Enter, false)], routedKeys);
        Assert.Equal(2, routed.Count);

        adapter.RecordButton(Controls.Key(KeyboardKey.E), false);
        adapter.BeginFrame(Frame(2));
        policy.Apply(
            new LayerInputRoutingResult(false, null, null, null, []),
            uiLayer,
            uiIsOpaque: false,
            rebinding: false);
        adapter.RecordButton(Controls.Key(KeyboardKey.E), true);
        adapter.BeginFrame(Frame(3));
        Assert.True(adapter.CurrentFrame.WasPressed(GameControls.Interact));
    }

    [Fact]
    public void FocusLossAndGamepadDisconnect_ClearHeldStateWithoutStalePress()
    {
        var engine = new InputManEngine(GameControls.CreateProfile());
        using var adapter = new AurelianInputAdapter(engine);
        adapter.SetContexts(GameControls.Gameplay);
        adapter.RecordButton(Controls.Key(KeyboardKey.W), true);
        adapter.BeginFrame(Frame(1));
        Assert.NotEqual(0f, adapter.CurrentFrame.GetAxis2(GameControls.Move).Y);

        adapter.OnFocusChanged(false);
        adapter.BeginFrame(Frame(2));
        Assert.Equal(0f, adapter.CurrentFrame.GetAxis2(GameControls.Move).Y);
        adapter.OnFocusChanged(true);
        adapter.BeginFrame(Frame(3));
        Assert.Equal(0f, adapter.CurrentFrame.GetAxis2(GameControls.Move).Y);

        adapter.ConnectGamepad(0);
        adapter.RecordAxis(Controls.Gamepad(GamepadAxis.LeftY), 1f);
        adapter.BeginFrame(Frame(4));
        Assert.NotEqual(0f, adapter.CurrentFrame.GetAxis2(GameControls.Move).Y);
        adapter.DisconnectGamepad(0);
        adapter.BeginFrame(Frame(5));
        Assert.Equal(0f, adapter.CurrentFrame.GetAxis2(GameControls.Move).Y);
    }

    [Fact]
    public void HostPropagatesResizeFramesAndDisposesInAuthorityOrder()
    {
        var log = new List<string>();
        var window = new FakeWindow(log);
        var input = new FakeInput(log);
        var compositor = new FakeCompositor(log);
        var app = new FakeApplication(log);
        using (var host = new AurelianGameHost(window, input, compositor, app, "InputM2Proof"))
        {
            window.Resize(new HostSurfaceSize(1920, 1080));
            Assert.True(host.RunFrame(TimeSpan.FromMilliseconds(16)));
        }

        Assert.Contains("compositor.resize:1920x1080", log);
        Assert.Contains("app.resize:1920x1080", log);
        Assert.True(log.IndexOf("app.dispose") < log.IndexOf("input.dispose"));
        Assert.True(log.IndexOf("input.dispose") < log.IndexOf("compositor.dispose"));
        Assert.True(log.IndexOf("compositor.dispose") < log.IndexOf("window.dispose"));
    }

    [Fact]
    public void HostOwnsAudioPumpFocusAndDisposal()
    {
        var log = new List<string>();
        var window = new FakeWindow(log);
        var audio = new FakeAudio(log);
        using (var host = new AurelianGameHost(
            window,
            new FakeInput(log),
            new FakeCompositor(log),
            new FakeApplication(log),
            "AudioM4Proof",
            audio))
        {
            Assert.True(host.RunFrame(TimeSpan.FromMilliseconds(16)));
        }

        Assert.Contains("audio.focus:true", log);
        Assert.Contains("audio.update:16", log);
        Assert.Contains("audio.dispose", log);
    }

    [Fact]
    public void HostDeltaFlowsThroughCadenceFactsBeforeApplicationSemantics()
    {
        var log = new List<string>();
        var semantic = new FakeCadenceApplication(log);
        var scheduler = new CadenceScheduler(
            [new CadenceDefinition(new CadenceId("simulation"), RationalRate.PerSecond(20), 0)],
            TimeSpan.FromSeconds(1));
        var cadenceApplication = new AurelianCadenceApplication(scheduler, semantic);
        using var host = new AurelianGameHost(
            new FakeWindow(log),
            new FakeInput(log),
            new FakeCompositor(log),
            cadenceApplication,
            "CadenceM5Proof");

        Assert.True(host.RunFrame(TimeSpan.FromMilliseconds(100)));

        Assert.Equal(2, semantic.DueCount);
        Assert.Contains("cadence:simulation:1", log);
        Assert.Contains("cadence:simulation:2", log);
    }

    private static SpatialMoveIntent MoveWith(Action<AurelianInputAdapter> input)
    {
        var engine = new InputManEngine(GameControls.CreateProfile());
        using var adapter = new AurelianInputAdapter(engine);
        adapter.SetContexts(GameControls.Gameplay);
        input(adapter);
        adapter.BeginFrame(Frame(1));
        var controller = new TinyFarmInputController();
        return Assert.IsType<SpatialMoveIntent>(Assert.IsType<SubmitGameIntent>(Assert.Single(controller.Map(adapter.CurrentFrame))).Intent);
    }

    private static AurelianHostFrame Frame(ulong sequence) => new(sequence, TimeSpan.FromMilliseconds(16), TimeSpan.FromMilliseconds(16 * (double)sequence));

    private sealed class FakeWindow(List<string> log) : IAurelianGameWindow
    {
        public HostSurfaceSize SurfaceSize { get; private set; } = new(1280, 720);
        public bool IsFocused => true;
        public bool ShouldClose => false;
        public event Action<HostSurfaceSize>? Resized;
        public event Action<bool>? FocusChanged { add { } remove { } }
        public void PumpEvents() => log.Add("window.pump");
        public void Resize(HostSurfaceSize size) { SurfaceSize = size; Resized?.Invoke(size); }
        public void Dispose() => log.Add("window.dispose");
    }

    private sealed class FakeInput(List<string> log) : IAurelianHostInput
    {
        public void BeginFrame(AurelianHostFrame frame) => log.Add("input.frame");
        public void OnFocusChanged(bool focused) => log.Add($"input.focus:{focused}");
        public void Dispose() => log.Add("input.dispose");
    }

    private sealed class FakeCompositor(List<string> log) : IAurelianHostCompositor
    {
        public void Resize(HostSurfaceSize size) => log.Add($"compositor.resize:{size.Width}x{size.Height}");
        public void Present(AurelianHostFrame frame) => log.Add("compositor.present");
        public void Dispose() => log.Add("compositor.dispose");
    }

    private sealed class FakeApplication(List<string> log) : IAurelianGameApplication
    {
        public void OnResize(HostSurfaceSize size) => log.Add($"app.resize:{size.Width}x{size.Height}");
        public void OnSimulationTick(AurelianHostFrame frame) => log.Add("app.tick");
        public void OnRender(AurelianHostFrame frame) => log.Add("app.render");
        public void Dispose() => log.Add("app.dispose");
    }

    private sealed class FakeCadenceApplication(List<string> log) : IAurelianCadenceApplication
    {
        public SimulationExecutionRate ExecutionRate => SimulationExecutionRate.Normal;
        public int DueCount { get; private set; }

        public void OnResize(HostSurfaceSize size)
        {
            log.Add("cadence.resize");
        }

        public void OnCadenceAdvance(AurelianHostFrame frame, CadenceAdvanceResult advance)
        {
            foreach (DueWorkFact due in advance.DueWork)
            {
                DueCount++;
                log.Add($"cadence:{due.Cadence.Value}:{due.Tick}");
            }
        }

        public void OnRender(AurelianHostFrame frame)
        {
            log.Add("cadence.render");
        }

        public void Dispose()
        {
            log.Add("cadence.dispose");
        }
    }

    private sealed class FakeAudio(List<string> log) : IAurelianAudioRuntime
    {
        public void Update(TimeSpan elapsed) => log.Add($"audio.update:{elapsed.TotalMilliseconds:0}");
        public void SetFocused(bool focused) => log.Add($"audio.focus:{focused.ToString().ToLowerInvariant()}");
        public void Dispose() => log.Add("audio.dispose");
    }
}
