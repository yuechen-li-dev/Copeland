using Aurelian.Core.Engine.Frames;
using Aurelian.Core.Engine.Runtime;
using Machina.Presentation.Screens;

namespace Aurelian.VisibleTriangle;

internal static class VisibleTrianglePresenterScreenStack
{
    public static ScreenLayerOrder CreateLayerOrder()
    {
        ScreenLayerOrder order =
        [
            ScreenLayers.Background,
            ScreenLayers.World,
            ScreenLayers.Hud,
            ScreenLayers.Overlay,
            ScreenLayers.Debug,
            ScreenLayers.Cursor,
        ];

        return order;
    }

    public static PresenterScreenStack CreateStack(VisibleTriangleMachinaScreen worldScreen)
    {
        ArgumentNullException.ThrowIfNull(worldScreen);

        var stack = new PresenterScreenStack(CreateLayerOrder());
        stack.Add(worldScreen);
        return stack;
    }

    public static async Task<AurelianFrameLoopResult> RunWorldScreenAsync(
        PresenterScreenStack screenStack,
        AurelianRuntimeTickFrameStep runtimeTickStep,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(screenStack);
        ArgumentNullException.ThrowIfNull(runtimeTickStep);

        IReadOnlyList<IPresenterScreen> visibleScreens = screenStack.VisibleScreensInCompositionOrder();
        VisibleTriangleMachinaScreen worldScreen = visibleScreens
            .OfType<VisibleTriangleMachinaScreen>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Presenter screen stack does not contain a visible Aurelian world screen.");

        return await worldScreen.WorldScreen
            .RunFrameLoopAsync(runtimeTickStep, cancellationToken)
            .ConfigureAwait(false);
    }
}
