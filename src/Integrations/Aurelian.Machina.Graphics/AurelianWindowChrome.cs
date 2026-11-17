using System.Runtime.InteropServices;
using Machina.Standard.Windowing;
using Silk.NET.Windowing;

namespace Aurelian.Machina;

/// <summary>
/// Optional Win32 client-drawn title bar. Keeps native resize, system menu,
/// caption dragging/double-click and system commands. No graphics work occurs
/// inside the subclass callback. Dispose before the native window.
/// </summary>
public sealed class AurelianWindowChrome : IDisposable
{
    private readonly IWindow window;
    private readonly nint handle;
    private readonly SubclassProc callback;
    private bool disposed;
    private bool captionCapture;
    public WindowTitleBarLayout Layout { get; private set; }
    public WindowCaptionButton Hovered { get; private set; }
    public WindowCaptionButton Pressed { get; private set; }
    public bool IsMaximized => IsZoomed(handle);
    public int CallbackFailures { get; private set; }
    public Exception? LastError { get; private set; }
    public long VisualRevision { get; private set; }

    public AurelianWindowChrome(IWindow window, double captionHeight = 32)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Custom chrome currently requires Windows.");
        this.window = window;
        handle = window.Native?.Win32?.Hwnd ?? throw new ArgumentException("Window has no native handle.", nameof(window));
        Layout = new(window.Size.X, captionHeight);
        callback = WindowProc;
        if (!SetWindowSubclass(handle, callback, 0x4D414348, 0))
            throw new InvalidOperationException("Could not attach Machina window chrome.");
        ApplyFrame();
    }

    public void Update()
    {
        Layout = Layout with { Width = Math.Max(1, window.Size.X) };
        if (!GetCursorPos(out var point)) return;
        nint underPointer = WindowFromPoint(point);
        ScreenToClient(handle, ref point);
        var hovered = Layout.At(point.X, point.Y);
        if (underPointer != handle)
            hovered = WindowCaptionButton.None;
        if (Hovered != hovered) { Hovered = hovered; VisualRevision++; }
    }

    /// <summary>Use for keyboard/accessibility actions; close still reaches the host's Closing handler.</summary>
    public void Invoke(WindowCaptionButton button)
    {
        uint command = button switch
        {
            WindowCaptionButton.Minimize => 0xF020,
            WindowCaptionButton.Maximize => IsMaximized ? 0xF120u : 0xF030u,
            WindowCaptionButton.Close => 0xF060,
            _ => 0
        };
        if (command != 0) PostMessage(handle, 0x0112, command, 0);
    }

    public void Restore() => PostMessage(handle, 0x0112, 0xF120, 0);

    public int HitTestClient(int x, int y)
    {
        int border = Math.Max(4, (int)Math.Round(8 * GetDpiForWindow(handle) / 96.0));
        if (!IsMaximized)
        {
            bool left = x < border, right = x >= window.Size.X - border;
            bool top = y < border, bottom = y >= window.Size.Y - border;
            if (top) return left ? 13 : right ? 14 : 12;
            if (bottom) return left ? 16 : right ? 17 : 15;
            if (left) return 10;
            if (right) return 11;
        }
        return Layout.At(x, y) switch
        {
            WindowCaptionButton.Minimize => 8,
            WindowCaptionButton.Maximize => 9, // HTMAXBUTTON preserves the Windows 11 Snap entry point.
            WindowCaptionButton.Close => 20,
            _ => y >= 0 && y < Layout.Height ? 2 : 1
        };
    }

    private nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        try
        {
            if (message == 0x0083 && wParam != 0) // WM_NCCALCSIZE: replace the caption with client content.
            {
                if (IsMaximized)
                {
                    var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                    if (GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor))
                        Marshal.StructureToPtr(monitor.Work, lParam, false);
                }
                return 0;
            }
            if (message == 0x0084) // WM_NCHITTEST uses signed screen coordinates (including negative monitors).
            {
                var point = new NativePoint((short)((long)lParam & 0xFFFF), (short)(((long)lParam >> 16) & 0xFFFF));
                ScreenToClient(hwnd, ref point);
                return HitTestClient(point.X, point.Y);
            }
            if (message == 0x00A0 || message == 0x02A2) // non-client movement/leave, including Snap hover.
                DwmDefWindowProc(hwnd, message, wParam, lParam, out _);
            if (message == 0x00A1 && CaptionButton(wParam) is { } down && down != WindowCaptionButton.None)
            {
                Pressed = down;
                SetCapture(handle);
                captionCapture = true;
                VisualRevision++;
                return 0;
            }
            if (captionCapture && message is 0x00A2 or 0x0202)
            {
                var point = new NativePoint((short)((long)lParam & 0xFFFF), (short)(((long)lParam >> 16) & 0xFFFF));
                if (message == 0x00A2) ScreenToClient(handle, ref point);
                var clicked = Layout.At(point.X, point.Y);
                var original = Pressed;
                captionCapture = false;
                Pressed = WindowCaptionButton.None;
                ReleaseCapture();
                VisualRevision++;
                if (clicked == original) Invoke(clicked);
                return 0;
            }
            if (message is 0x00A2 or 0x0202 or 0x0215 or 0x0008)
            {
                if (Pressed != WindowCaptionButton.None) { Pressed = WindowCaptionButton.None; VisualRevision++; }
                if (captionCapture) { captionCapture = false; ReleaseCapture(); }
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        {
            CallbackFailures++;
            LastError = error;
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private static WindowCaptionButton CaptionButton(nuint hit) => hit switch
    {
        8 => WindowCaptionButton.Minimize,
        9 => WindowCaptionButton.Maximize,
        20 => WindowCaptionButton.Close,
        _ => WindowCaptionButton.None
    };

    private void ApplyFrame() => SetWindowPos(handle, 0, 0, 0, 0, 0, 0x0027); // FRAMECHANGED | NO* placement flags

    public void Dispose()
    {
        if (disposed) return;
        if (captionCapture) ReleaseCapture();
        RemoveWindowSubclass(handle, callback, 0x4D414348);
        disposed = true;
        ApplyFrame();
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint(int x, int y) { public int X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(nint hwnd, ref NativePoint point);
    [DllImport("user32.dll")] private static extern nint SetCapture(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("dwmapi.dll")] private static extern bool DwmDefWindowProc(nint hwnd, uint message, nuint wParam, nint lParam, out nint result);
}
