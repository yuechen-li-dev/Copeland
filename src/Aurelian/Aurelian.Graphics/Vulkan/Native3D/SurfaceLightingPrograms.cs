using Aurelian.Rendering.Contracts.Shaders;

namespace Aurelian.Graphics.Vulkan.Native3D;

public sealed record SurfaceLightingPrograms(
    CompiledGraphicsProgram Resolve,
    CompiledGraphicsProgram AmbientOcclusion,
    CompiledGraphicsProgram AmbientDenoise,
    CompiledGraphicsProgram LightTiles)
{
    public CompiledGraphicsProgram? TailTemporal { get; init; }
    public CompiledGraphicsProgram? TailSpatial { get; init; }
    public CompiledGraphicsProgram? TailCompose { get; init; }
    public byte[]? RestirSpatial { get; init; }
    public byte[]? SampledLocalLights { get; init; }
    public byte[]? LocalRayShadows { get; init; }
    public CompiledGraphicsProgram? HeightFog { get; init; }
    public CompiledGraphicsProgram? SubsurfaceDiffuse { get; init; }
    public CompiledGraphicsProgram? SubsurfaceMerge { get; init; }
    public CompiledGraphicsProgram? TransparentModel { get; init; }
    public CompiledGraphicsProgram? TransparencyResolve { get; init; }
    public CompiledGraphicsProgram? ParticleBillboards { get; init; }
    public CompiledGraphicsProgram? VolumeInject { get; init; }
    public CompiledGraphicsProgram? VolumeIntegrate { get; init; }
    public CompiledGraphicsProgram? VolumeResolve { get; init; }
    public CompiledGraphicsProgram? RefractiveModel { get; init; }
    public CompiledGraphicsProgram? RefractionResolve { get; init; }
    public CompiledGraphicsProgram? WaterTemporal { get; init; }
}
