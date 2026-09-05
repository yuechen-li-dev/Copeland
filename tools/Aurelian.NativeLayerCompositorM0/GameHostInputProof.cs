using Aurelian.GameHost;
using InputMan.Aurelian;
using TinyFarm.Core;
using TinyFarm.InputMan;

sealed class ProofHostWindow(HostSurfaceSize surfaceSize) : IAurelianGameWindow
{
    private bool focused = true;
    public HostSurfaceSize SurfaceSize { get; private set; } = surfaceSize;
    public bool IsFocused => focused;
    public bool ShouldClose => false;
    public bool Disposed { get; private set; }
    public event Action<HostSurfaceSize>? Resized;
    public event Action<bool>? FocusChanged;
    public void PumpEvents() { }
    public void SetFocus(bool value) { focused = value; FocusChanged?.Invoke(value); }
    public void Resize(HostSurfaceSize value) { SurfaceSize = value; Resized?.Invoke(value); }
    public void Dispose() => Disposed = true;
}

sealed class ProofGameApplication(AurelianInputAdapter input) : IAurelianGameApplication
{
    private readonly TinyFarmInputController controller = new();
    public IReadOnlyList<TinyFarmInputCommand> Commands { get; private set; } = [];
    public bool Disposed { get; private set; }
    public void OnResize(HostSurfaceSize size) { }
    public void OnSimulationTick(AurelianHostFrame frame) => Commands = controller.Map(input.CurrentFrame);
    public void OnRender(AurelianHostFrame frame) { }
    public TIntent SingleIntent<TIntent>() where TIntent : GameIntent
    {
        SubmitGameIntent command = Commands.OfType<SubmitGameIntent>().Single();
        return command.Intent is TIntent intent
            ? intent
            : throw new InvalidOperationException($"Expected {typeof(TIntent).Name}, got {command.Intent.GetType().Name}.");
    }
    public void Dispose() => Disposed = true;
}
