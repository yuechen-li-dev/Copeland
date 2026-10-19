using System.Text.Json.Serialization;

namespace Copeland.TS.Gpu.VdMir;

public sealed record VdMirSourceSpan(string File, int Start, int Length);

public sealed record VdMirRelatedSpan(string Message, VdMirSourceSpan Span);

public sealed record VdMirGenericSpecialization(
    string Identity,
    string Declaration,
    string Function,
    IReadOnlyList<string> TypeArguments,
    IReadOnlyList<string> StaticArguments,
    VdMirSourceSpan CallSource,
    VdMirSourceSpan DeclarationSource,
    int BodyBindings);

public sealed record VdMirDiagnostic(
    string Code,
    string CanonicalCode,
    string Category,
    string Message,
    VdMirSourceSpan PrimarySpan,
    IReadOnlyList<VdMirRelatedSpan> RelatedSpans);

public sealed record VdMirFlowTapeState(
    int Position,
    string State,
    string Fragment,
    string Path,
    string? ParentPath,
    VdMirSourceSpan Source,
    VdMirSourceSpan? CompositionSite);

public sealed record VdMirFlowTape(string Flow, string FrameType, string StateType, IReadOnlyList<VdMirFlowTapeState> States);

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
    VdMirSourceSpan Source)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VdMirResourceAccess? Access { get; init; }
}

public sealed record VdMirStorageField(string Path, string Type, int Offset);

/// <summary>Packed 32-bit storage words. This law is independent of uniform/register layout.</summary>
public sealed record VdMirStorageLayout(string Type, int Stride, IReadOnlyList<VdMirStorageField> Fields);

public sealed record VdMirWorkgroupArray(string Name, string ElementType, int Count, VdMirSourceSpan Source);

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
    IReadOnlyList<VdMirStatement>? ElseBody = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] VdMirStatement? Initializer = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] VdMirStatement? Increment = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaximumIterations { get; init; }
}

public sealed record VdMirFunction(
    string Name,
    IReadOnlyList<VdMirParameter> Parameters,
    string ReturnType,
    IReadOnlyList<VdMirStatement> Statements,
    VdMirSourceSpan Source);

/// <summary>Local value carrier. No resource-buffer ABI is implied.</summary>
public sealed record VdMirEnumField(string Name, string Type, VdMirSourceSpan Source);

public sealed record VdMirEnumCase(string Name, uint Tag, string PayloadType, IReadOnlyList<VdMirEnumField> Payload, VdMirSourceSpan Source)
{
    public VdMirValueType? PayloadLayout { get; init; }
}

public sealed record VdMirEnum(string Name, IReadOnlyList<VdMirEnumCase> Cases,
    IReadOnlyList<VdMirEnumField> CarrierFields, VdMirSourceSpan Source)
{
    public VdMirValueType? CarrierLayout { get; init; }
}

public sealed record VdMirValueField(string Name, string Type, int Offset, int Size, int Alignment, VdMirSourceSpan Source,
    string? PhysicalType = null);

public sealed record VdMirValueType(string Name, string Kind, string? ElementType, IReadOnlyList<int> Shape,
    IReadOnlyList<VdMirValueField> Fields, int Size, int Alignment, string StorageOrder, VdMirSourceSpan Source);

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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<VdMirDiagnostic>? Warnings { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<VdMirStorageLayout>? StorageLayouts { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<VdMirEnum>? Enums { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<VdMirValueType>? ValueTypes { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<VdMirGenericSpecialization>? GenericSpecializations { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<VdMirFlowTape>? FlowTapes { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<VdMirWorkgroupArray>? WorkgroupArrays { get; init; }

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
