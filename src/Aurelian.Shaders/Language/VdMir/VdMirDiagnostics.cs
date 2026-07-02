using Aurelian.Shaders.Language.Ast;
using Aurelian.Shaders.Language.Diagnostics;

namespace Aurelian.Shaders.Language.VdMir;

public static class VdMirDiagnosticCodes
{
    public const string UnsupportedDeclaration = "VDM1001";
    public const string UnsupportedShaderShape = "VDM1002";
    public const string UnsupportedStageMethod = "VDM1003";
    public const string UnsupportedType = "VDM1004";
    public const string UnsupportedStatement = "VDM1005";
    public const string UnsupportedExpression = "VDM1006";
    public const string MissingFunctionBody = "VDM1007";
    public const string UnsupportedEntryPointShape = "VDM1008";
    public const string DuplicateEntryPoint = "VDM1009";
}

public sealed record VdMirDiagnostic(
    string Code,
    SdslvDiagnosticSeverity Severity,
    string Message,
    SdslvSpan Span);
