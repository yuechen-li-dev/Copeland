using Aurelian.Shaders.Language.Ast;

namespace Aurelian.Shaders.Language.Diagnostics;

public sealed record SdslvDiagnostic(
    string Code,
    SdslvDiagnosticSeverity Severity,
    SdslvDiagnosticPhase Phase,
    string Message,
    SdslvSpan Span);
