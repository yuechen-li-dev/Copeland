using System.Collections.Immutable;
using Machina.Runtime.Input;

namespace Machina.Presentation.Input;

/// <summary>
/// Routes host lifecycle observations from the canonical, ordered UI batch
/// into Machina frontend messages. It has no platform or backend dependency.
/// </summary>
public static class MachinaFrontendInputRouter
{
    public static MachinaFrontendInputRoutingResult Route(UiInputBatch inputBatch)
    {
        ArgumentNullException.ThrowIfNull(inputBatch);

        ImmutableArray<MachinaFrontendMessage>.Builder frontendMessages =
            ImmutableArray.CreateBuilder<MachinaFrontendMessage>();

        foreach (UiInputEvent inputEvent in inputBatch.Events)
        {
            switch (inputEvent)
            {
                case UiSurfaceResized resized:
                    frontendMessages.Add(new MachinaFrontendSurfaceResized(resized.Size));
                    break;
                case UiCloseRequested:
                    frontendMessages.Add(new MachinaFrontendCloseRequested());
                    break;
            }
        }

        return new MachinaFrontendInputRoutingResult(
            inputBatch.BatchId,
            frontendMessages.ToImmutable());
    }
}

/// <summary>
/// Lifecycle-only routing output. UI-specific action routers may add their own
/// typed results while preserving this batch's event order.
/// </summary>
public sealed record MachinaFrontendInputRoutingResult(
    ulong BatchId,
    ImmutableArray<MachinaFrontendMessage> FrontendMessages)
{
    public bool RequiresRecomposition => FrontendMessages
        .OfType<MachinaFrontendSurfaceResized>()
        .Any();
}
