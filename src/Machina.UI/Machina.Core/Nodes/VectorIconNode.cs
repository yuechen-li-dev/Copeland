using Machina.Core.Styling;
using Machina.Core.Assets;

namespace Machina.Core.Nodes;

public sealed record VectorIconNode(
    MachinaVectorIconId Icon,
    double Width,
    double Height,
    ColorToken Tint) : UiNode;
