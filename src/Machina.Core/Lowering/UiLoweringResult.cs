using Machina.Core.Actions;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Rows;

namespace Machina.Core.Lowering;

public sealed record UiLoweringResult(
    IReadOnlyList<LayoutRow> Rows,
    IReadOnlyDictionary<NodeId, UiStyle> Styles,
    IReadOnlyDictionary<NodeId, TextStyle> TextStyles,
    IReadOnlyDictionary<NodeId, UiSemantics> Semantics,
    IReadOnlyDictionary<NodeId, UiAction> Actions);
