using System.Collections.Generic;
using System.Linq;

namespace Aurelian.Rendering.Contracts.Compositor;

public sealed record RequiredPlantOutputSet(
    ulong FrameId,
    CompositorPolicyKind Policy,
    IReadOnlyList<PlantOutputRef> RequiredOutputs)
{
    public bool IsSatisfiedBy(IReadOnlyList<PlantOutputReadiness> readiness)
        => RequiredOutputs.All(required => readiness.Any(output => output.Output == required
            && (output.Status == PlantOutputReadinessStatus.Ready
                || output.Status == PlantOutputReadinessStatus.Reused)));
}
