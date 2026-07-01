using System.Collections.Generic;

namespace Aurelian.Rendering.Contracts.Compositor;

public sealed record CompositorDiagnostics(
    double? AgreementRate,
    IReadOnlyDictionary<string, double> Metrics)
{
    public static CompositorDiagnostics Empty { get; } =
        new(null, new Dictionary<string, double>());
}
