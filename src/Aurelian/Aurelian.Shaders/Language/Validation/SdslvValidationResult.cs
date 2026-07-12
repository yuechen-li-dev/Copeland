using Aurelian.Shaders.Language.Ast;
using Aurelian.Shaders.Language.Diagnostics;

namespace Aurelian.Shaders.Language.Validation;

public sealed record SdslvValidationResult(
    SdslvModule Module,
    IReadOnlyList<SdslvDiagnostic> Diagnostics)
{
    public bool Success => Diagnostics.All(x => x.Severity != SdslvDiagnosticSeverity.Error);
}
