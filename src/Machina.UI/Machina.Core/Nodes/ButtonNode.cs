using Machina.Core.Actions;
using Machina.Core.Styling;

namespace Machina.Core.Nodes;

public sealed record ButtonNode(
    string Text,
    UiAction? Action = null,
    bool Disabled = false,
    UiStyle? Style = null) : UiNode;
