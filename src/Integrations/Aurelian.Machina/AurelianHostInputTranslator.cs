using Aurelian.Core.Engine.Commands;
using Aurelian.Core.Engine.Frames;
using Machina.Presentation.Input;
using Machina.Runtime.Input;

namespace Aurelian.Machina;

/// <summary>
/// Integration-only mapping from Machina's normalized device batch to the
/// narrow host lifecycle facts Aurelian presently consumes.
/// </summary>
public static class AurelianHostInputTranslator
{
    public static AurelianHostLifecycleInput TranslateLifecycle(UiInputBatch inputBatch)
    {
        ArgumentNullException.ThrowIfNull(inputBatch);

        AurelianHostExtent? latestHostExtent = null;
        bool closeRequested = false;

        foreach (UiInputEvent inputEvent in inputBatch.Events)
        {
            switch (inputEvent)
            {
                case UiSurfaceResized resized:
                    latestHostExtent = new AurelianHostExtent(
                        checked((uint)resized.Size.Width),
                        checked((uint)resized.Size.Height));
                    break;
                case UiCloseRequested:
                    closeRequested = true;
                    break;
            }
        }

        return new AurelianHostLifecycleInput(latestHostExtent, closeRequested);
    }

    public static AurelianCloseRequest Translate(MachinaFrontendCloseRequested message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new AurelianCloseRequest();
    }
}
