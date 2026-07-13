using Aurelian.Core.Engine.Commands;
using Aurelian.Core.Engine.Frames;
using Machina.Presentation.Input;
using System.Collections.Immutable;

namespace Aurelian.Machina;

/// <summary>
/// Integration-only mapping from explicit Machina frontend lifecycle messages
/// to the narrow host facts Aurelian presently consumes.
/// </summary>
public static class AurelianHostInputTranslator
{
    /// <summary>
    /// Translates one ordered frontend-routing result without executing any
    /// lifecycle policy. Resize facts retain their last observed extent and
    /// each close message becomes an explicit backend-owned command.
    /// </summary>
    public static AurelianHostInputTranslation Translate(
        IEnumerable<MachinaFrontendMessage> frontendMessages)
    {
        ArgumentNullException.ThrowIfNull(frontendMessages);

        AurelianHostExtent? latestHostExtent = null;
        ImmutableArray<AurelianCloseRequest>.Builder closeRequests =
            ImmutableArray.CreateBuilder<AurelianCloseRequest>();

        foreach (MachinaFrontendMessage frontendMessage in frontendMessages)
        {
            switch (frontendMessage)
            {
                case MachinaFrontendSurfaceResized resized:
                    latestHostExtent = new AurelianHostExtent(
                        checked((uint)resized.Size.Width),
                        checked((uint)resized.Size.Height));
                    break;
                case MachinaFrontendCloseRequested closeRequested:
                    closeRequests.Add(Translate(closeRequested));
                    break;
            }
        }

        return new AurelianHostInputTranslation(
            new AurelianHostLifecycleInput(latestHostExtent, CloseRequested: false),
            closeRequests.ToImmutable());
    }

    public static AurelianHostLifecycleInput TranslateLifecycle(
        IEnumerable<MachinaFrontendMessage> frontendMessages)
    {
        return Translate(frontendMessages).Lifecycle;
    }

    public static AurelianCloseRequest Translate(MachinaFrontendCloseRequested message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new AurelianCloseRequest();
    }
}

/// <summary>
/// Aurelian-owned values produced by translating one frontend batch. The
/// integration host selects when to pass them to the frame loop.
/// </summary>
public sealed record AurelianHostInputTranslation(
    AurelianHostLifecycleInput Lifecycle,
    ImmutableArray<AurelianCloseRequest> CloseRequests);
