using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Diagnostics;
using Copeland.Browser.Wasm.M0.Copeland;

namespace Copeland.Browser.Wasm.M0;

[SupportedOSPlatform("browser")]
public static partial class BrowserBridge
{
    private const int IncrementEvent = 0;
    private const int ResetEvent = 1;
    private static int currentCount;

    [JSExport]
    public static string Initialize()
    {
        currentCount = 0;
        return RenderSnapshot();
    }

    [JSExport]
    public static string Dispatch(int eventDiscriminant)
    {
        currentCount = eventDiscriminant switch
        {
            IncrementEvent => Main.ApplyIncrementEvent(currentCount),
            ResetEvent => Main.ApplyResetEvent(currentCount),
            _ => throw new ArgumentOutOfRangeException(nameof(eventDiscriminant), eventDiscriminant, "The browser host sent an unknown CounterEvent discriminant."),
        };

        return RenderSnapshot();
    }

    [JSExport]
    public static int RunWorkload(int iterations)
    {
        return Main.Workload(iterations);
    }

    [JSExport]
    public static string MeasureBoundary(int iterations)
    {
        int checksum = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();
        for (int index = 0; index < iterations; index += 1)
        {
            checksum = Main.ApplyIncrementEvent(checksum);
        }

        stopwatch.Stop();
        return $"{checksum}:{stopwatch.Elapsed.TotalMilliseconds:F3}";
    }

    private static string RenderSnapshot()
    {
        return $"Count: {currentCount}";
    }
}
