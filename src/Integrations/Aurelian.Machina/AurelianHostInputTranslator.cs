using Aurelian.Core.Engine.Commands;
using Aurelian.Core.Engine.Frames;
using Machina.Presentation.Input;

namespace Aurelian.Machina;

/// <summary>
/// Integration-only mapping from explicit Machina frontend lifecycle messages
/// to the narrow host facts Aurelian presently consumes.
/// </summary>
public static class AurelianHostInputTranslator
{
    public static AurelianHostLifecycleInput TranslateLifecycle(
        IEnumerable<MachinaFrontendMessage> frontendMessages)
    {
        ArgumentNullException.ThrowIfNull(frontendMessages);

        AurelianHostExtent? latestHostExtent = null;
        foreach (MachinaFrontendMessage frontendMessage in frontendMessages)
        {
            switch (frontendMessage)
            {
                case MachinaFrontendSurfaceResized resized:
                    latestHostExtent = new AurelianHostExtent(
                        checked((uint)resized.Size.Width),
                        checked((uint)resized.Size.Height));
                    break;
            }
        }

        return new AurelianHostLifecycleInput(latestHostExtent, CloseRequested: false);
    }

    public static AurelianCloseRequest Translate(MachinaFrontendCloseRequested message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new AurelianCloseRequest();
    }
}
