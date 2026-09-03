using System.Text.Json.Serialization;

namespace Copeland.TS.Gpu.VdMir;

public sealed record VdMirSourceSpan(string File, int Start, int Length);

public sealed record VdMirRelatedSpan(string Message, VdMirSourceSpan Span);

public sealed record VdMirDiagnostic(
    string Code,
    string CanonicalCode,
    string Category,
    string Message,
    VdMirSourceSpan PrimarySpan,
    IReadOnlyList<VdMirRelatedSpan> RelatedSpans);

public enum VdMirResourceAccess
{
    Readonly,
    Readwrite,
}

public sealed record VdMirResource(
    string Name,
    string ElementType,
    VdMirResourceAccess Access,
    int Set,
    int Binding,
    VdMirSourceSpan Source,
    VdMirSourceSpan BindingSource);

public sealed record VdMirParameter(
    string Name,
    string Type,
    string? Builtin,
    VdMirSourceSpan Source);

public sealed record VdMirExpression(
    string Kind,
    string Type,
    VdMirSourceSpan Source,
    string? Value = null,
    IReadOnlyList<VdMirExpression>? Operands = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? MemberNames = null);

public sealed record VdMirStatement(
    string Kind,
    VdMirSourceSpan Source,
    string? Name = null,
    string? Type = null,
    bool Mutable = false,
    VdMirExpression? Expression = null,
    IReadOnlyList<VdMirStatement>? Body = null,
    IReadOnlyList<VdMirStatement>? ElseBody = null);

public sealed record VdMirFunction(
    string Name,
    IReadOnlyList<VdMirParameter> Parameters,
    string ReturnType,
    IReadOnlyList<VdMirStatement> Statements,
    VdMirSourceSpan Source);

public sealed record VdMirComputeEntryPoint(
    string Name,
    string EmittedName,
    int NumThreadsX,
    int NumThreadsY,
    int NumThreadsZ,
    IReadOnlyList<VdMirParameter> Builtins,
    VdMirSourceSpan Source);

public sealed record VdMirComputeModule(
    string Schema,
    string ConformanceSchema,
    string FeatureLevel,
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<string> Types,
    IReadOnlyList<VdMirResource> Resources,
    IReadOnlyList<VdMirFunction> Functions,
    VdMirComputeEntryPoint? EntryPoint,
    IReadOnlyList<VdMirDiagnostic> Diagnostics)
{
    public const string CurrentSchema = "vdmir.semantic.v1";
    public const string CanonicalConformanceSchema = "sdslv.conformance.v1";
    public const string ComputeM1FeatureLevel = "compute.m1";

    public bool Success => EntryPoint is not null && Diagnostics.Count == 0;
}

public enum CopelandCompilerProfile
{
    Host,
    Gpu,
}

public sealed record GpuSourceFile(string Path, string Source);

public sealed record GpuCompilationRequest(
    IReadOnlyList<GpuSourceFile> Sources,
    CopelandCompilerProfile Profile = CopelandCompilerProfile.Gpu);
