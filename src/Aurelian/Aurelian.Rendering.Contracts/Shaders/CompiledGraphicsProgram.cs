namespace Aurelian.Rendering.Contracts.Shaders;

public enum CompiledGraphicsStage
{
    Vertex,
    Fragment,
}

public enum CompiledGraphicsResourceKind
{
    Texture2D,
    Sampler,
    UniformBuffer,
}

public sealed record CompiledVertexInput(
    int Order,
    string Name,
    string Type,
    string PhysicalType,
    int Location,
    string? SemanticSpace);

public sealed record CompiledPixelTarget(
    int Order,
    string Name,
    string PhysicalType,
    int Target);

public sealed record CompiledGraphicsResource(
    int Order,
    string Name,
    CompiledGraphicsResourceKind Kind,
    string Type,
    int Set,
    int Binding,
    IReadOnlyList<CompiledGraphicsStage> Visibility);

public sealed record CompiledMaterialField(
    int Order,
    string Name,
    string PhysicalType,
    int Offset,
    int Size,
    int Alignment);

public sealed record CompiledMaterialLayout(
    string Name,
    int Size,
    int Set,
    int Binding,
    IReadOnlyList<CompiledGraphicsStage> Visibility,
    IReadOnlyList<CompiledMaterialField> Fields);

public sealed record CompiledGraphicsProgram(
    string FormatVersion,
    string Name,
    string FeatureLevel,
    string CompilerProfile,
    string VdMirSha256,
    CompiledShaderProgram Shaders,
    IReadOnlyList<CompiledVertexInput> VertexInputs,
    IReadOnlyList<CompiledPixelTarget> PixelTargets,
    IReadOnlyList<CompiledGraphicsResource> Resources,
    CompiledMaterialLayout? Material)
{
    public const string CurrentFormatVersion = "aurelian.compiled-graphics-program/0";
}
