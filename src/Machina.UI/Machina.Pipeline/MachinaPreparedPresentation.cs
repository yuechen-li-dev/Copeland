using Machina.Core.Lowering;
using Machina.Layout.Documents;
using Machina.Presentation;
using Machina.Runtime.Input;

namespace Machina.Pipeline;

/// <summary>
/// Immutable Machina-owned preparation result. Raster realization is intentionally external.
/// </summary>
public sealed record MachinaPreparedPresentation(
    UiLoweringResult Lowering,
    LayoutDocument Document,
    ResolvedLayoutDocument Resolved,
    UiHitTestIndex HitTest,
    MachinaPresentationFrame PresentationFrame);
