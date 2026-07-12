using Machina.Core.Actions;
using Machina.Core.Semantics;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;

namespace Machina.Runtime.Input;

public sealed record UiHitTestResult(
    NodeId NodeId,
    Rect Rect,
    UiAction Action,
    UiSemantics? Semantics);
