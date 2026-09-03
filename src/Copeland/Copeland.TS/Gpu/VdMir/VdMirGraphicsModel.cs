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
    VdMirSourceSpan? MetadataSource);

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
    VdMirSourceSpan Source);

public sealed record VdMirLinkedVarying(
    int Location,
    string Type,
    string Interpolation,
    string VertexStream,
    string VertexMember,
    string PixelStream,
    string PixelMember);

public sealed record VdMirGraphicsProgram(
    string Name,
    string VertexEntry,
    string PixelEntry,
    IReadOnlyList<VdMirLinkedVarying> Varyings,
    IReadOnlyList<VdMirStreamMember> VertexInputs,
    IReadOnlyList<VdMirStreamMember> PixelTargets,
    IReadOnlyList<VdMirResource> Resources);

public sealed record VdMirGraphicsModule(
    string Schema,
    string ConformanceSchema,
    string FeatureLevel,
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<string> Types,
    IReadOnlyList<VdMirStream> Streams,
    IReadOnlyList<VdMirFunction> Functions,
    IReadOnlyList<VdMirGraphicsEntryPoint> EntryPoints,
    VdMirGraphicsProgram? GraphicsProgram,
    IReadOnlyList<VdMirDiagnostic> Diagnostics)
{
    public const string GraphicsM2FeatureLevel = "graphics.m2";

    public bool Success => GraphicsProgram is not null && Diagnostics.Count == 0;
}
