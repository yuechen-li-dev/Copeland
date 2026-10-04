using System.Runtime.InteropServices;
using Silk.NET.Windowing;

namespace TinyFarm.Native;

/// <summary>Seed focus from the real HWND; initial focus can predate event subscriptions.</summary>
internal static partial class TinyFarmNativeFocus
{
    public static bool IsFocused(IWindow window)
    {
        return window.Native?.Win32 is { } win32 && win32.Hwnd == GetForegroundWindow();
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();
}
