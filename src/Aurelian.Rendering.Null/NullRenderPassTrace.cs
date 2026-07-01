using System.Collections.Generic;

namespace Aurelian.Rendering.Null;

public sealed record NullRenderPassTrace(
    string Name,
    string Target,
    string Pipeline,
    string Shader,
    IReadOnlyList<NullRenderDrawTrace> Draws);
