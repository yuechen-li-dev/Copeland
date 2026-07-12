using Aurelian.Shaders.Language.Ast;
using Aurelian.Shaders.Language.Diagnostics;

namespace Aurelian.Shaders.Language.Parsing;

public sealed record SdslvParseResult(
    SdslvModule? Module,
    IReadOnlyList<SdslvDiagnostic> Diagnostics)
{
    public bool Success => Module is not null && Diagnostics.All(x => x.Severity != SdslvDiagnosticSeverity.Error);
}
