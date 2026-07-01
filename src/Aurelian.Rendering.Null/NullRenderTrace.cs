using System.Collections.Generic;
using System.Linq;

namespace Aurelian.Rendering.Null;

public sealed record NullRenderTrace(IReadOnlyList<NullRenderPassTrace> Passes)
{
    public int PassCount => Passes.Count;

    public int DrawCount => Passes.Sum(x => x.Draws.Count);

    public static NullRenderTrace Empty { get; } = new([]);
}
