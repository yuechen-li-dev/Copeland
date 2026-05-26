using Dominatus.Core.Runtime;
using Machina.Core.Lowering;
using Machina.Layout.Documents;
using Machina.Renderer.Raster.Dominatus.Models;
using Machina.Runtime.Input;

namespace Machina.Pipeline;

public sealed record MachinaFrame(
    UiLoweringResult Lowering,
    LayoutDocument Document,
    ResolvedLayoutDocument Resolved,
    UiHitTestIndex HitTest,
    IReadOnlyList<IActuationCommand> RenderCommands,
    RasterFrame RasterFrame);
