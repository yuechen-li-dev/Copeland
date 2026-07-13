using Aurelian.Core.Engine;
using Aurelian.Core.Engine.Frames;
using Aurelian.Core.Engine.Runtime;
using Aurelian.Core.Presentation.Screens;
using Aurelian.Graphics.Vulkan.Presentation;
using Aurelian.Rendering.Contracts.Compositor;
using Aurelian.Runtime.Sessions;

namespace Aurelian.VisibleTriangle;

internal static class Program
{
    private const int Success = 0;
    private const int EnvironmentOrRuntimeFailure = 2;
    private const int UnexpectedFailure = 3;
    private const int DefaultFrameCount = 3;
    private const int MaximumFrameCount = 300;
    private const string DefaultPresenter = "silk";

    public static async Task<int> Main(string[] args)
    {
        bool enableValidation = args.Contains("--validation", StringComparer.OrdinalIgnoreCase);
        bool skipHold = args.Contains("--no-hold", StringComparer.OrdinalIgnoreCase);
        int frameCount = ParseFrameCount(args);
        string presenter = ParsePresenter(args);

        Console.WriteLine("Aurelian A71 visible triangle sample");
        Console.WriteLine("Path: Presenter/Silk.NET backend -> PresenterScreenStack -> VisibleTriangleWorldScreen(world) -> prepared Vulkan setup -> AurelianEngine -> AurelianRuntimeSession -> AurelianFrameLoop -> runtime tick -> frame pump -> runtime compositor policy -> core compositor bridge -> Vulkan compositor -> presenter present");
        Console.WriteLine($"Validation: {(enableValidation ? "enabled" : "disabled")}");
        Console.WriteLine($"Presenter backend: {presenter}");
        Console.WriteLine($"Selected finite frame count: {frameCount} (default {DefaultFrameCount}, max {MaximumFrameCount}).");

        if (!string.Equals(presenter, DefaultPresenter, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Unsupported presenter backend '{presenter}'. M14b currently supports only '--presenter {DefaultPresenter}'.");
            return EnvironmentOrRuntimeFailure;
        }

        VisibleTriangleSampleFrame? sample = null;
        AurelianRuntimeSession? runtimeSession = null;
        bool runtimeStarted = false;

        try
        {
            var shaderProgram = VisibleTriangleShaderAssets.LoadSmokeTriangleShader(Console.Error);
            sample = VisibleTriangleSampleFrame.Create(enableValidation, frameCount, shaderProgram);
            Console.WriteLine($"Prepared visible Vulkan setup created ({sample.SwapchainDescription}); swapchain images will be acquired per frame.");
            Console.WriteLine("Presenter events will be pumped before each acquire and after each present; close requests stop the finite frame loop cleanly.");
            Console.WriteLine($"Engine status after start: {sample.Engine.Status}; graphics mode: {sample.Engine.Options.Graphics.Mode} ({sample.Engine.Options.Graphics.Ownership}).");

            runtimeSession = new AurelianRuntimeSession();
            AurelianRuntimeResult runtimeStart = runtimeSession.Start();
            if (!runtimeStart.Success)
            {
                Console.Error.WriteLine("Runtime session start failed:");
                Console.Error.WriteLine(FormatDiagnostics(runtimeStart));
                return EnvironmentOrRuntimeFailure;
            }

            runtimeStarted = true;
            Console.WriteLine("Dominatus-backed runtime session started.");

            var runtimeTicker = new AurelianRuntimeSessionTickerAdapter(runtimeSession);
            var runtimeTickStep = new AurelianRuntimeTickFrameStep(runtimeTicker);
            var worldScreen = new VisibleTriangleWorldScreen(sample);
            PresenterScreenStack screenStack = VisibleTrianglePresenterScreenStack.CreateStack(worldScreen);
            PrintScreenStack(screenStack);

            AurelianFrameLoopResult loopResult = await VisibleTrianglePresenterScreenStack
                .RunWorldScreenAsync(screenStack, runtimeTickStep)
                .ConfigureAwait(false);
            PrintLoopResult(loopResult);
            if (sample.CloseRequested)
            {
                Console.WriteLine("Window close requested; stopped frame loop.");
            }

            PrintSampleDiagnostics(sample.InputProvider, sample.PresentationMechanism, sample.PresenterBackend);

            if (!skipHold && loopResult.Success)
            {
                Console.WriteLine("Keeping the window responsive briefly (pass --no-hold to exit immediately). ");
                DateTimeOffset end = DateTimeOffset.UtcNow.AddSeconds(2);
                while (DateTimeOffset.UtcNow < end && !sample.CloseRequested)
                {
                    sample.PumpEvents();
                    await Task.Delay(16).ConfigureAwait(false);
                }
            }

            return loopResult.Success ? Success : EnvironmentOrRuntimeFailure;
        }
        catch (VisibleTriangleSampleException ex)
        {
            Console.Error.WriteLine("A71 visible triangle sample could not run in this environment or failed during the manifest-backed shader/Vulkan sample path.");
            Console.Error.WriteLine(ex.Message);
            return EnvironmentOrRuntimeFailure;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("A71 visible triangle sample hit an unexpected exception.");
            Console.Error.WriteLine(ex);
            return UnexpectedFailure;
        }
        finally
        {
            if (runtimeStarted && runtimeSession is not null)
            {
                AurelianRuntimeResult runtimeStop = runtimeSession.Stop();
                if (!runtimeStop.Success)
                {
                    Console.Error.WriteLine("Runtime session stop returned diagnostics:");
                    Console.Error.WriteLine(FormatDiagnostics(runtimeStop));
                }
                else
                {
                    Console.WriteLine("Dominatus-backed runtime session stopped.");
                }
            }

            if (sample is not null)
            {
                if (sample.Engine.Status == AurelianEngineStatus.Started)
                {
                    AurelianEngineResult stop = sample.Engine.Stop();
                    if (!stop.Success)
                    {
                        Console.Error.WriteLine($"Engine stop returned status {stop.Status}: {FormatDiagnostics(stop)}");
                    }
                    else
                    {
                        Console.WriteLine("Aurelian engine stopped.");
                    }
                }

                sample.Dispose();
            }
        }
    }

    private static void PrintScreenStack(PresenterScreenStack screenStack)
    {
        IReadOnlyList<ScreenLayerSlot> layers = screenStack.LayerOrder.DeclaredSlots;
        Console.WriteLine($"Presenter screen stack layers: {string.Join(", ", layers.Select(static layer => layer.Key.Value))}.");

        IReadOnlyList<IPresenterScreen> visibleScreens = screenStack.VisibleScreensInCompositionOrder();
        Console.WriteLine($"Visible screens in composition order: {string.Join(", ", visibleScreens.Select(static screen => screen.Layer.Value))}.");
    }

    private static string ParsePresenter(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--presenter", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
            {
                Console.Error.WriteLine($"Missing --presenter value; defaulting to '{DefaultPresenter}'.");
                return DefaultPresenter;
            }

            return args[i + 1];
        }

        return DefaultPresenter;
    }

    private static int ParseFrameCount(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--frames", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out int value) || value <= 0)
            {
                Console.Error.WriteLine($"Invalid --frames value; defaulting to {DefaultFrameCount}. Use a positive integer up to {MaximumFrameCount}.");
                return DefaultFrameCount;
            }

            if (value > MaximumFrameCount)
            {
                Console.Error.WriteLine($"Requested --frames {value} exceeds max {MaximumFrameCount}; capping to {MaximumFrameCount}.");
                return MaximumFrameCount;
            }

            return value;
        }

        return DefaultFrameCount;
    }

    private static void PrintLoopResult(AurelianFrameLoopResult result)
    {
        Console.WriteLine($"Frame loop status: {result.Status}; stop reason: {result.StopReason}; attempted: {result.FramesAttempted}; completed: {result.FramesCompleted}.");

        if (result.Diagnostics.Count > 0)
        {
            Console.WriteLine("Frame loop diagnostics:");
            Console.WriteLine(FormatDiagnostics(result));
        }

        foreach (AurelianFrameLoopIterationResult iteration in result.Iterations)
        {
            Console.WriteLine($"Frame {iteration.FrameId.Value}: frame={iteration.FrameResult.Status}; presented={iteration.Presented}; compositor={iteration.FrameResult.CompositorResult?.Status}.");
            if (iteration.RuntimeTickResult is not null)
            {
                Console.WriteLine($"Frame {iteration.FrameId.Value}: runtime tick={iteration.RuntimeTickResult.Status}; runtime result={iteration.RuntimeTickResult.RuntimeResult?.Status}; tick index={iteration.RuntimeTickResult.RuntimeResult?.TickIndex}.");
                if (iteration.RuntimeTickResult.Diagnostics.Count > 0)
                {
                    Console.WriteLine(FormatDiagnostics(iteration.RuntimeTickResult));
                }
            }

            Console.WriteLine($"Compositor dispatch status: {iteration.FrameResult.CompositorResult?.DispatchResult?.Status}; target: {FormatTarget(iteration.FrameResult.CompositorResult?.DispatchResult?.Target)}.");
            if (iteration.FrameResult.Diagnostics.Count > 0)
            {
                Console.WriteLine(FormatDiagnostics(iteration.FrameResult));
            }
        }
    }

    private static void PrintSampleDiagnostics(
        SilkNetFrameInputProvider inputProvider,
        VulkanPresentationMechanism presentationMechanism,
        IPresenterBackend presenterBackend)
    {
        Console.WriteLine($"Presenter pump count: {presenterBackend.PumpCount}; close requested: {presenterBackend.CloseRequested}.");

        if (inputProvider.Frames.Count > 0)
        {
            Console.WriteLine("Sample frame acquire/present state:");
            foreach (VisibleTriangleFrameState frame in inputProvider.Frames.Values.OrderBy(static state => state.FrameId.Value))
            {
                Console.WriteLine($"Frame {frame.FrameId.Value}: acquired swapchain image {frame.SwapchainImageIndex}; target={frame.PresentationTarget}; output={frame.PlantOutput}.");
            }
        }

        foreach (string diagnostic in presenterBackend.Diagnostics)
        {
            Console.WriteLine($"Presenter diagnostic: {diagnostic}");
        }

        foreach (string diagnostic in inputProvider.Diagnostics)
        {
            Console.WriteLine($"Input provider diagnostic: {diagnostic}");
        }

        foreach (string diagnostic in presentationMechanism.Diagnostics)
        {
            Console.WriteLine($"Presentation diagnostic: {diagnostic}");
        }
    }

    private static string FormatDiagnostics(AurelianFrameResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(AurelianEngineResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(AurelianFrameLoopResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(AurelianRuntimeResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(AurelianRuntimeTickFrameStepResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatTarget(PresentationTargetRef? target)
    {
        if (target is null)
        {
            return "<none>";
        }

        PresentationTargetRef value = target.Value;
        return $"plant={value.PlantId}, image={value.SwapchainImageIndex}, frame={value.FrameId}";
    }
}
