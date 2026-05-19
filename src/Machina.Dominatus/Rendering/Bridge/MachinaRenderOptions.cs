using Machina.Core.Styling;

namespace Machina.Dominatus.Rendering.Bridge;

public sealed record MachinaRenderOptions(
    int Width,
    int Height,
    ColorToken? ClearColor = null);
