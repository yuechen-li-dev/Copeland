namespace Aurelian.GameHost;

public readonly record struct HostSurfaceSize(int Width, int Height)
{
    public HostSurfaceSize EnsureValid()
    {
        if (Width <= 0 || Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Width), "Host surface extent must be positive.");
        }
        return this;
    }
}

public readonly record struct AurelianHostFrame(ulong Sequence, TimeSpan Elapsed, TimeSpan Total);

public interface IAurelianGameWindow : IDisposable
{
    HostSurfaceSize SurfaceSize { get; }
    bool IsFocused { get; }
    bool ShouldClose { get; }
    event Action<HostSurfaceSize>? Resized;
    event Action<bool>? FocusChanged;
    void PumpEvents();
}

public interface IAurelianHostInput : IDisposable
{
    void BeginFrame(AurelianHostFrame frame);
    void OnFocusChanged(bool focused);
}

public interface IAurelianHostCompositor : IDisposable
{
    void Resize(HostSurfaceSize size);
    void Present(AurelianHostFrame frame);
}

public interface IAurelianGameApplication : IDisposable
{
    void OnResize(HostSurfaceSize size);
    void OnSimulationTick(AurelianHostFrame frame);
    void OnRender(AurelianHostFrame frame);
}

/// <summary>Bounded native host. It owns execution mechanics, never game state or resolver authority.</summary>
public sealed class AurelianGameHost : IDisposable
{
    private readonly IAurelianGameWindow window;
    private readonly IAurelianHostInput input;
    private readonly IAurelianHostCompositor compositor;
    private readonly IAurelianGameApplication application;
    private bool disposed;
    private ulong sequence;
    private TimeSpan total;

    public AurelianGameHost(
        IAurelianGameWindow window,
        IAurelianHostInput input,
        IAurelianHostCompositor compositor,
        IAurelianGameApplication application,
        string applicationName)
    {
        this.window = window ?? throw new ArgumentNullException(nameof(window));
        this.input = input ?? throw new ArgumentNullException(nameof(input));
        this.compositor = compositor ?? throw new ArgumentNullException(nameof(compositor));
        this.application = application ?? throw new ArgumentNullException(nameof(application));
        ApplicationName = string.IsNullOrWhiteSpace(applicationName)
            ? throw new ArgumentException("Application name must not be empty.", nameof(applicationName))
            : applicationName;
        ApplicationDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ApplicationName);
        ConfigurationRoot = Path.Combine(ApplicationDataRoot, "config");
        window.Resized += HandleResize;
        window.FocusChanged += HandleFocus;
        HandleResize(window.SurfaceSize.EnsureValid());
        HandleFocus(window.IsFocused);
    }

    public string ApplicationName { get; }
    public string ApplicationDataRoot { get; }
    public string ConfigurationRoot { get; }

    public bool RunFrame(TimeSpan elapsed)
    {
        ThrowIfDisposed();
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }

        window.PumpEvents();
        if (window.ShouldClose)
        {
            return false;
        }

        total += elapsed;
        var frame = new AurelianHostFrame(++sequence, elapsed, total);
        input.BeginFrame(frame);
        application.OnSimulationTick(frame);
        application.OnRender(frame);
        compositor.Present(frame);
        return true;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        window.Resized -= HandleResize;
        window.FocusChanged -= HandleFocus;

        List<Exception>? failures = null;
        DisposeOne(application, ref failures);
        DisposeOne(input, ref failures);
        DisposeOne(compositor, ref failures);
        DisposeOne(window, ref failures);
        if (failures is not null) throw new AggregateException("Aurelian host disposal failed.", failures);
    }

    private void HandleResize(HostSurfaceSize size)
    {
        size.EnsureValid();
        compositor.Resize(size);
        application.OnResize(size);
    }

    private void HandleFocus(bool focused) => input.OnFocusChanged(focused);

    private static void DisposeOne(IDisposable item, ref List<Exception>? failures)
    {
        try { item.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
