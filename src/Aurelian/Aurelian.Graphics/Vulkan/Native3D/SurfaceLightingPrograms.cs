using Aurelian.Rendering.Contracts.Shaders;

namespace Aurelian.Graphics.Vulkan.Native3D;

public sealed record SurfaceLightingPrograms(
    CompiledGraphicsProgram Resolve,
    CompiledGraphicsProgram AmbientOcclusion,
    CompiledGraphicsProgram AmbientDenoise,
    CompiledGraphicsProgram LightTiles);
