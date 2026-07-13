using Dominatus.Core.Runtime;
using Machina.Core.Lowering;
using Machina.Dominatus.Rendering.Commands;
using Machina.Layout.Documents;
using Machina.Presentation;

namespace Machina.Dominatus.Rendering.Bridge;

/// <summary>
/// Transitional JTF-M2 compatibility surface. M5 removes this Dominatus command route.
/// </summary>
public static class MachinaRenderBridge
{
    public static IReadOnlyList<IActuationCommand> BuildCommands(
        UiLoweringResult lowering,
        ResolvedLayoutDocument resolved,
        MachinaRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(lowering);
        ArgumentNullException.ThrowIfNull(resolved);

        MachinaRenderOptions renderOptions = ResolveOptions(resolved, options);
        ValidateOptions(renderOptions);
        var viewport = new MachinaPresentationViewport(renderOptions.Width, renderOptions.Height);
        MachinaPresentationFrame frame = MachinaPresentationFrameBuilder.Build(lowering, resolved, viewport);
        return LegacyMachinaRenderCommandAdapter.ToLegacyCommands(frame);
    }

    private static MachinaRenderOptions ResolveOptions(ResolvedLayoutDocument resolved, MachinaRenderOptions? options)
    {
        if (options is not null)
        {
            return options;
        }

        var rootRect = resolved.Nodes[resolved.RootId].Rect;
        return new MachinaRenderOptions(
            (int)Math.Ceiling(rootRect.Width),
            (int)Math.Ceiling(rootRect.Height));
    }

    private static void ValidateOptions(MachinaRenderOptions options)
    {
        if (options.Width <= 0)
        {
            throw new InvalidOperationException("Render width must be greater than zero.");
        }

        if (options.Height <= 0)
        {
            throw new InvalidOperationException("Render height must be greater than zero.");
        }
    }
}
