using System.Diagnostics;
using Copeland.TS.Gpu.VdMir;

namespace Copeland.TS.Gpu.Wgsl;

public sealed record WgslSourceMapping(int Line, VdMirSourceSpan Source);

public sealed record WgslGraphicsProgram(
    string Code,
    string VertexEntryPoint,
    string FragmentEntryPoint,
    string SemanticHash,
    VdMirGraphicsProgram Semantics,
    IReadOnlyList<WgslSourceMapping> SourceMappings,
    double GenerationMilliseconds);

public sealed record WgslGraphicsResult(WgslGraphicsProgram? Program, IReadOnlyList<VdMirDiagnostic> Diagnostics)
{
    public bool Success => Program is not null && Diagnostics.Count == 0;
}

/// <summary>Pure managed direct graphics backend. No native compiler, files, or processes.</summary>
public static class WgslGraphicsBackend
{
    public const string CompatibilityVersion = "vd-wgsl/1";

    public static WgslGraphicsResult Compile(GpuCompilationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Lower(GpuGraphicsBinder.Compile(request));
    }

    public static WgslGraphicsResult Lower(VdMirGraphicsModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (!module.Success)
        {
            return new WgslGraphicsResult(null, module.Diagnostics.Count > 0 ? module.Diagnostics :
                [new VdMirDiagnostic("COPE-WGSL-MODULE-0001", "COPE-WGSL-MODULE-0001", "wgsl",
                    "A successfully linked graphics VD-MIR module is required.", new VdMirSourceSpan("", 0, 0), [])]);
        }

        var timer = Stopwatch.StartNew();
        try
        {
            WgslPreparedModule prepared = new WgslGraphicsPreparation(module).Prepare();
            var emitted = WgslGraphicsEmitter.Emit(prepared);
            return new WgslGraphicsResult(new WgslGraphicsProgram(emitted.Code, "vd_vertex", "vd_fragment",
                WgslSemanticIdentity.Hash(module), module.GraphicsProgram!,
                emitted.Mappings, timer.Elapsed.TotalMilliseconds), []);
        }
        catch (WgslPreparationException error)
        {
            return new WgslGraphicsResult(null,
                [new VdMirDiagnostic(error.Code, error.Code, "wgsl", error.Message, error.SourceSpan, [])]);
        }
    }
}

internal sealed class WgslPreparationException(string code, string message, VdMirSourceSpan source) : Exception(message)
{
    public string Code { get; } = code;
    public VdMirSourceSpan SourceSpan { get; } = source;
}

internal sealed record WgslField(string Name, string Type, int? Location = null, string? Builtin = null,
    string? Interpolation = null, int? Alignment = null, int? Size = null);
internal sealed record WgslStructure(string Name, IReadOnlyList<WgslField> Fields);
internal sealed record WgslResource(string Name, string Type, int Group, int Binding, bool Uniform);
internal sealed record WgslFunction(VdMirFunction Function, VdMirGraphicsStage? Stage = null);
internal sealed record WgslPreparedModule(IReadOnlyList<WgslStructure> Structures,
    IReadOnlyList<WgslResource> Resources, IReadOnlyList<WgslFunction> Functions);
