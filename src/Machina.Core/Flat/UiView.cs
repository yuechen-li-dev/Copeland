using Machina.Core.Actions;
using Machina.Core.Semantics;
using Machina.Core.Styling;

namespace Machina.Core.Flat;

public sealed record UiView(
    UiStyle? Style = null,
    TextStyle? TextStyle = null,
    UiSemantics? Semantics = null,
    UiAction? Action = null);
