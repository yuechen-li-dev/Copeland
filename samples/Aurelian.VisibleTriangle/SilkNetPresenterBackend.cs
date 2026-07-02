using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Aurelian.VisibleTriangle;

internal interface IPresenterBackend : IDisposable
{
    string Name { get; }

    bool CloseRequested { get; }

    int PumpCount { get; }

    IReadOnlyList<string> Diagnostics { get; }

    IReadOnlyList<string> GetRequiredVulkanInstanceExtensions();

    IWindow Window { get; }

    void PumpEvents();
}

internal sealed class SilkNetPresenterBackend : IPresenterBackend
{
    private readonly List<string> diagnostics = [];
    private bool disposed;

    private SilkNetPresenterBackend(IWindow window, string title)
    {
        Window = window;
        Title = title;
    }

    public static SilkNetPresenterBackend Create(uint width, uint height, string title, bool visible, bool vsync)
    {
        WindowOptions windowOptions = WindowOptions.DefaultVulkan;
        windowOptions.IsVisible = visible;
        windowOptions.Size = new Vector2D<int>((int)Math.Clamp(width, 1, int.MaxValue), (int)Math.Clamp(height, 1, int.MaxValue));
        windowOptions.Title = title;
        windowOptions.VSync = vsync;

        IWindow window = Silk.NET.Windowing.Window.Create(windowOptions);
        window.Initialize();

        return new SilkNetPresenterBackend(window, title);
    }

    public string Name => "silk";

    public string Title { get; }

    public IWindow Window { get; }

    public bool CloseRequested => Window.IsClosing;

    public int PumpCount { get; private set; }

    public IReadOnlyList<string> Diagnostics => diagnostics;

    public unsafe IReadOnlyList<string> GetRequiredVulkanInstanceExtensions()
    {
        ThrowIfDisposed();

        IVkSurface? surface = Window.VkSurface;
        if (surface is null)
        {
            throw new InvalidOperationException("Silk.NET presenter backend window did not expose a Vulkan surface source after initialization.");
        }

        uint count = 0;
        byte** extensions = surface.GetRequiredExtensions(out count);
        List<string> names = [];
        for (int i = 0; i < count; i++)
        {
            string? name = SilkMarshal.PtrToString((nint)extensions[i], NativeStringEncoding.UTF8);
            if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name, StringComparer.Ordinal))
            {
                names.Add(name);
            }
        }

        if (names.Count == 0)
        {
            throw new InvalidOperationException("Silk.NET presenter backend returned no required Vulkan instance extensions.");
        }

        return names;
    }

    public void PumpEvents()
    {
        ThrowIfDisposed();
        PumpCount++;
        Window.DoEvents();
        if (Window.IsClosing)
        {
            diagnostics.Add($"Window close requested after event pump {PumpCount}.");
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Window.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(SilkNetPresenterBackend));
        }
    }
}
