using Aurelian.Shaders.Language.Diagnostics;

namespace Aurelian.Shaders.Language.Emission.Hlsl;

public sealed record HlslEmissionResult(
    string Hlsl,
    IReadOnlyList<SdslvDiagnostic> Diagnostics)
{
    public bool Success => Diagnostics.All(x => x.Severity != SdslvDiagnosticSeverity.Error);
}
