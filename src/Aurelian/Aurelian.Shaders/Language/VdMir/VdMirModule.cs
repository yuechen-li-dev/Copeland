using Aurelian.Shaders.Language.Ast;
using Aurelian.Shaders.Language.Diagnostics;

namespace Aurelian.Shaders.Language.VdMir;

public enum VdMirStageKind
{
    Unknown,
    Vertex,
    Pixel,
}

public enum VdMirSemanticKind
{
    None,
    Position,
    Color0,
    SvPosition,
    SvTarget0,
}

public sealed record VdMirModule(
    SdslvPath? Namespace,
    IReadOnlyList<VdMirStruct> Structs,
    IReadOnlyList<VdMirEntryPoint> EntryPoints,
    IReadOnlyList<VdMirDiagnostic> Diagnostics)
{
    public bool Success => Diagnostics.All(x => x.Severity != SdslvDiagnosticSeverity.Error);
}

public sealed record VdMirStruct(
    string Name,
    IReadOnlyList<VdMirField> Fields,
    SdslvSpan Span);

public sealed record VdMirField(
    string Name,
    VdMirType Type,
    VdMirSemanticKind Semantic,
    SdslvSpan Span);

public sealed record VdMirEntryPoint(
    string Name,
    VdMirStageKind Stage,
    IReadOnlyList<VdMirParameter> Parameters,
    VdMirType ReturnType,
    VdMirSemanticKind ReturnSemantic,
    IReadOnlyList<VdMirStatement> Statements,
    SdslvSpan Span);

public sealed record VdMirParameter(
    string Name,
    VdMirType Type,
    SdslvSpan Span);

public abstract record VdMirStatement
{
    public abstract SdslvSpan Span { get; }
}

public sealed record VdMirLocalStatement(
    string Name,
    VdMirType Type,
    VdMirExpression? Initializer,
    SdslvSpan SourceSpan) : VdMirStatement
{
    public override SdslvSpan Span => SourceSpan;
}

public sealed record VdMirAssignStatement(
    VdMirExpression Target,
    VdMirExpression Value,
    SdslvSpan SourceSpan) : VdMirStatement
{
    public override SdslvSpan Span => SourceSpan;
}

public sealed record VdMirReturnStatement(
    VdMirExpression Value,
    SdslvSpan SourceSpan) : VdMirStatement
{
    public override SdslvSpan Span => SourceSpan;
}

public sealed record VdMirExpressionStatement(
    VdMirExpression Value,
    SdslvSpan SourceSpan) : VdMirStatement
{
    public override SdslvSpan Span => SourceSpan;
}

public abstract record VdMirExpression
{
    public abstract SdslvSpan Span { get; }
}

public sealed record VdMirIdentifierExpression(
    string Name,
    SdslvSpan SourceSpan) : VdMirExpression
{
    public override SdslvSpan Span => SourceSpan;
}

public sealed record VdMirIntegerLiteralExpression(
    string Value,
    SdslvSpan SourceSpan) : VdMirExpression
{
    public override SdslvSpan Span => SourceSpan;
}

public sealed record VdMirFloatLiteralExpression(
    string Value,
    SdslvSpan SourceSpan) : VdMirExpression
{
    public override SdslvSpan Span => SourceSpan;
}

public sealed record VdMirBoolLiteralExpression(
    bool Value,
    SdslvSpan SourceSpan) : VdMirExpression
{
    public override SdslvSpan Span => SourceSpan;
}

public sealed record VdMirFieldAccessExpression(
    VdMirExpression Base,
    string Field,
    SdslvSpan SourceSpan) : VdMirExpression
{
    public override SdslvSpan Span => SourceSpan;
}

public sealed record VdMirCallExpression(
    VdMirExpression Callee,
    IReadOnlyList<VdMirExpression> Arguments,
    SdslvSpan SourceSpan) : VdMirExpression
{
    public override SdslvSpan Span => SourceSpan;
}

public sealed record VdMirBinaryExpression(
    VdMirExpression Left,
    SdslvBinaryOperator Operator,
    VdMirExpression Right,
    SdslvSpan SourceSpan) : VdMirExpression
{
    public override SdslvSpan Span => SourceSpan;
}

public sealed record VdMirUnaryExpression(
    SdslvUnaryOperator Operator,
    VdMirExpression Operand,
    SdslvSpan SourceSpan) : VdMirExpression
{
    public override SdslvSpan Span => SourceSpan;
}
