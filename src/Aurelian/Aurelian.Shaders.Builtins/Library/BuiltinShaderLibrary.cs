using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;

namespace Aurelian.Shaders.Library;

public enum ShaderFamily
{
    Ui2D,
    Effects2D,
    Effects3D,
    ComputeMath,
    Surfaces,
    Lighting,
    Shadows,
    IndirectLighting,
    Atmosphere,
    Transparency,
    PostProcessing,
    Deformation,
    Queries,
}

public enum ShaderSourceKind
{
    Graphics,
    Compute,
    Module,
}

/// <summary>Name is the stable compiler module ID. SourcePath is the repository authoring location.</summary>
public sealed record BuiltinShaderSource(string Name, ShaderFamily Family, ShaderSourceKind Kind)
{
    public string SourcePath => $"Assets/{Family}/{Name}";
}

/// <summary>Engine-owned Visual TypeScript sources. Repository folders group authoring files;
/// imports and compiled snapshots retain their existing flat module IDs and source bytes.</summary>
public static class BuiltinShaderLibrary
{
    public static IReadOnlyList<BuiltinShaderSource> Sources { get; } = Array.AsReadOnly<BuiltinShaderSource>(
    [
        new("AnalyticShape2D.v.ts", ShaderFamily.Ui2D, ShaderSourceKind.Graphics),
        new("MsdfText.v.ts", ShaderFamily.Ui2D, ShaderSourceKind.Graphics),
        new("ProfileMsdf.v.ts", ShaderFamily.Ui2D, ShaderSourceKind.Graphics),

        new("ReactiveFluid2D.v.ts", ShaderFamily.Effects2D, ShaderSourceKind.Graphics),
        new("SemanticFog.v.ts", ShaderFamily.Effects2D, ShaderSourceKind.Graphics),
        new("SoftShockwave.v.ts", ShaderFamily.Effects2D, ShaderSourceKind.Graphics),

        new("Particles3D.v.ts", ShaderFamily.Effects3D, ShaderSourceKind.Compute),
        new("ParticleBillboard3D.v.ts", ShaderFamily.Effects3D, ShaderSourceKind.Graphics),

        new("Solid3D.v.ts", ShaderFamily.Surfaces, ShaderSourceKind.Graphics),
        new("StaticModel3D.v.ts", ShaderFamily.Surfaces, ShaderSourceKind.Graphics),
        new("SurfaceSolid3D.v.ts", ShaderFamily.Surfaces, ShaderSourceKind.Graphics),
        new("SurfaceModel3D.v.ts", ShaderFamily.Surfaces, ShaderSourceKind.Graphics),
        new("SurfaceResolve3D.v.ts", ShaderFamily.Surfaces, ShaderSourceKind.Graphics),
        new("SurfaceLighting.v.ts", ShaderFamily.Surfaces, ShaderSourceKind.Module),
        new("SubsurfaceDiffuse3D.v.ts", ShaderFamily.Surfaces, ShaderSourceKind.Graphics),
        new("SubsurfaceMerge3D.v.ts", ShaderFamily.Surfaces, ShaderSourceKind.Graphics),

        new("Lighting3D.v.ts", ShaderFamily.Lighting, ShaderSourceKind.Module),
        new("LightTiles3D.v.ts", ShaderFamily.Lighting, ShaderSourceKind.Graphics),
        new("EnvironmentCompile3D.v.ts", ShaderFamily.Lighting, ShaderSourceKind.Graphics),
        new("AmbientOcclusion3D.v.ts", ShaderFamily.Lighting, ShaderSourceKind.Graphics),
        new("AmbientDenoise3D.v.ts", ShaderFamily.Lighting, ShaderSourceKind.Graphics),

        new("Shadow3D.v.ts", ShaderFamily.Shadows, ShaderSourceKind.Graphics),
        new("ShadowDistanceField.v.ts", ShaderFamily.Shadows, ShaderSourceKind.Module),

        new("CompiledDiffuseLighting.v.ts", ShaderFamily.IndirectLighting, ShaderSourceKind.Module),
        new("DiffuseCache3D.v.ts", ShaderFamily.IndirectLighting, ShaderSourceKind.Graphics),
        new("DiffuseProbeConnections3D.v.ts", ShaderFamily.IndirectLighting, ShaderSourceKind.Graphics),
        new("DiffuseProbeLighting.v.ts", ShaderFamily.IndirectLighting, ShaderSourceKind.Module),
        new("DiffuseProbeQuery3D.v.ts", ShaderFamily.IndirectLighting, ShaderSourceKind.Graphics),
        new("DiffuseProbes3D.v.ts", ShaderFamily.IndirectLighting, ShaderSourceKind.Graphics),
        new("DiffuseProbeView3D.v.ts", ShaderFamily.IndirectLighting, ShaderSourceKind.Graphics),
        new("DiffuseRayHit.v.ts", ShaderFamily.IndirectLighting, ShaderSourceKind.Module),
        new("DiffuseRefitQuery3D.v.ts", ShaderFamily.IndirectLighting, ShaderSourceKind.Graphics),

        new("HeightFog.v.ts", ShaderFamily.Atmosphere, ShaderSourceKind.Module),
        new("HeightFog3D.v.ts", ShaderFamily.Atmosphere, ShaderSourceKind.Graphics),
        new("VolumeInject3D.v.ts", ShaderFamily.Atmosphere, ShaderSourceKind.Graphics),
        new("VolumeIntegrate3D.v.ts", ShaderFamily.Atmosphere, ShaderSourceKind.Graphics),
        new("VolumeLighting.v.ts", ShaderFamily.Atmosphere, ShaderSourceKind.Module),
        new("VolumeResolve3D.v.ts", ShaderFamily.Atmosphere, ShaderSourceKind.Graphics),

        new("TransparentModel3D.v.ts", ShaderFamily.Transparency, ShaderSourceKind.Graphics),
        new("TransparencyResolve3D.v.ts", ShaderFamily.Transparency, ShaderSourceKind.Graphics),
        new("RefractiveModel3D.v.ts", ShaderFamily.Transparency, ShaderSourceKind.Graphics),
        new("RefractionResolve3D.v.ts", ShaderFamily.Transparency, ShaderSourceKind.Graphics),

        new("Bloom3D.v.ts", ShaderFamily.PostProcessing, ShaderSourceKind.Graphics),
        new("TemporalGeometry.v.ts", ShaderFamily.PostProcessing, ShaderSourceKind.Module),
        new("TemporalPolicy.v.ts", ShaderFamily.PostProcessing, ShaderSourceKind.Module),
        new("TemporalResolve3D.v.ts", ShaderFamily.PostProcessing, ShaderSourceKind.Graphics),
        new("ToneMap3D.v.ts", ShaderFamily.PostProcessing, ShaderSourceKind.Graphics),

        new("HumanoidSkinning.v.ts", ShaderFamily.Deformation, ShaderSourceKind.Compute),
        new("Cloth3D.v.ts", ShaderFamily.Deformation, ShaderSourceKind.Compute),
        new("RayQuery.v.ts", ShaderFamily.Queries, ShaderSourceKind.Compute),
        new("KernelMath.v.ts", ShaderFamily.ComputeMath, ShaderSourceKind.Module),
        new("BFloat16.v.ts", ShaderFamily.ComputeMath, ShaderSourceKind.Module),
        new("TensorMap.v.ts", ShaderFamily.ComputeMath, ShaderSourceKind.Compute),
        new("HeadNormalize.v.ts", ShaderFamily.ComputeMath, ShaderSourceKind.Compute),
        new("Sgemm.v.ts", ShaderFamily.ComputeMath, ShaderSourceKind.Compute),
        new("StreamingLinear.v.ts", ShaderFamily.ComputeMath, ShaderSourceKind.Compute),
        new("CooperativeSgemm.v.ts", ShaderFamily.ComputeMath, ShaderSourceKind.Compute),
        new("RowReduction.v.ts", ShaderFamily.ComputeMath, ShaderSourceKind.Compute),
        new("Normalization.v.ts", ShaderFamily.ComputeMath, ShaderSourceKind.Compute),
        new("Activation.v.ts", ShaderFamily.ComputeMath, ShaderSourceKind.Compute),
        new("Rope.v.ts", ShaderFamily.ComputeMath, ShaderSourceKind.Compute),
        new("FftBitReverse.v.ts", ShaderFamily.ComputeMath, ShaderSourceKind.Compute),
        new("FftButterfly.v.ts", ShaderFamily.ComputeMath, ShaderSourceKind.Compute),
        new("Im2Col.v.ts", ShaderFamily.ComputeMath, ShaderSourceKind.Compute),
        new("AttentionMask.v.ts", ShaderFamily.ComputeMath, ShaderSourceKind.Compute),
    ]);

    private static readonly IReadOnlyDictionary<string, BuiltinShaderSource> ByName =
        Sources.ToDictionary(source => source.Name, StringComparer.Ordinal);

    public static BuiltinShaderSource Get(string name)
    {
        if (!ByName.TryGetValue(name, out var source))
        {
            throw new FileNotFoundException($"AUR-SHADER-LIBRARY-001: Unknown built-in shader module '{name}'.", name);
        }
        return source;
    }

    public static string Read(string name)
    {
        Get(name);
        return TryRead(name) ?? throw new FileNotFoundException(
            $"AUR-SHADER-LIBRARY-002: Built-in shader '{name}' is missing from Aurelian.Shaders.Builtins.", name);
    }

    /// <summary>Reader for GpuSourceLoader and optional adapter composition. Unknown modules return null.</summary>
    public static string? TryRead(string modulePath)
    {
        if (!ByName.ContainsKey(modulePath))
        {
            return null;
        }
        using Stream? stream = typeof(BuiltinShaderLibrary).Assembly.GetManifestResourceStream(
            "Aurelian.Shaders.Sources." + modulePath);
        if (stream is null)
        {
            throw new FileNotFoundException($"AUR-SHADER-LIBRARY-002: Embedded shader '{modulePath}' is unavailable.", modulePath);
        }
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static IReadOnlyList<GpuSourceFile> LoadSources(string name)
    {
        Get(name);
        return GpuSourceLoader.Load(name, TryRead);
    }

    public static void RequireKind(string name, ShaderSourceKind kind)
    {
        var source = Get(name);
        if (source.Kind != kind)
        {
            throw new ArgumentException($"AUR-SHADER-LIBRARY-003: '{name}' is {source.Kind}; requested {kind}.", nameof(name));
        }
    }
}
