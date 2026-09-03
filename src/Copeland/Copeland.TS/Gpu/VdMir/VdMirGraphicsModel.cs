namespace Copeland.TS.Gpu.VdMir;

public enum VdMirGraphicsStage
{
    Vertex,
    Pixel,
}

public enum VdMirStreamRole
{
    StageValue,
    Resource,
    Builtin,
}

public enum VdMirGraphicsResourceKind
{
    Texture2D,
    Sampler,
    Material,
}

public sealed record VdMirSemanticSpace(
    string Name,
    string PhysicalType,
    VdMirSourceSpan Source);

public sealed record VdMirStreamMember(
    int Order,
    string Name,
    string Type,
    VdMirStreamRole Role,
    int? Location,
    string? Builtin,
    int? Target,
    string Interpolation,
    VdMirSourceSpan Source,
    VdMirSourceSpan? MetadataSource,
    string? PhysicalType = null,
    string? SemanticSpace = null);

public sealed record VdMirStream(
    string Id,
    string Name,
    VdMirStreamRole Role,
    IReadOnlyList<VdMirStreamMember> Members,
    VdMirSourceSpan Source);

public sealed record VdMirGraphicsEntryPoint(
    string Name,
    string EmittedName,
    VdMirGraphicsStage Stage,
    string InputStream,
    string OutputStream,
    VdMirSourceSpan Source,
    IReadOnlyList<string>? BuiltinStreams = null,
    IReadOnlyList<string>? ResourceStreams = null);

public sealed record VdMirLinkedVarying(
    int Location,
    string Type,
    string Interpolation,
    string VertexStream,
    string VertexMember,
    string PixelStream,
    string PixelMember,
    string? PhysicalType = null,
    string? SemanticSpace = null);

public sealed record VdMirGraphicsResource(
    int Order,
    string Stream,
    string Name,
    VdMirGraphicsResourceKind Kind,
    string Type,
    string? ElementType,
    VdMirResourceAccess Access,
    int Set,
    int Binding,
    IReadOnlyList<VdMirGraphicsStage> Visibility,
    string? MaterialId,
    VdMirSourceSpan Source,
    VdMirSourceSpan BindingSource);

public sealed record VdMirMaterialField(
    int Order,
    string Name,
    string Type,
    string PhysicalType,
    int Offset,
    int Size,
    int Alignment,
    VdMirSourceSpan Source);

public sealed record VdMirMaterial(
    string Id,
    string Name,
    IReadOnlyList<VdMirMaterialField> Fields,
    int Size,
    int Set,
    int Binding,
    IReadOnlyList<VdMirGraphicsStage> Visibility,
    VdMirSourceSpan Source,
    VdMirSourceSpan BindingSource);

public sealed record VdMirGraphicsProgram(
    string Name,
    string VertexEntry,
    string PixelEntry,
    IReadOnlyList<VdMirLinkedVarying> Varyings,
    IReadOnlyList<VdMirStreamMember> VertexInputs,
    IReadOnlyList<VdMirStreamMember> PixelTargets,
    IReadOnlyList<VdMirGraphicsResource> Resources,
    VdMirMaterial? Material);

public sealed record VdMirGraphicsModule(
    string Schema,
    string ConformanceSchema,
    string FeatureLevel,
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<string> Types,
    IReadOnlyList<VdMirSemanticSpace> SemanticSpaces,
    IReadOnlyList<VdMirStream> Streams,
    IReadOnlyList<VdMirMaterial> Materials,
    IReadOnlyList<VdMirFunction> Functions,
    IReadOnlyList<VdMirGraphicsEntryPoint> EntryPoints,
    VdMirGraphicsProgram? GraphicsProgram,
    IReadOnlyList<VdMirDiagnostic> Diagnostics)
{
    public const string GraphicsM2FeatureLevel = "graphics.m2";
    public const string GraphicsM3FeatureLevel = "graphics.m3";

    public bool Success => GraphicsProgram is not null && Diagnostics.Count == 0;
}
