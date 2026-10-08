using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Spatial3D;
using Aurelian.Spatial3D.Vulkan;
using Aurelian.Shaders.Compute;
using Copeland.TS.Gpu;

namespace Aurelian.Games;

public static class GameRayQueries
{
    public static VulkanSpatialRayQueries3D Create(AurelianVulkanPlant plant, SpatialWorld3D world)
    {
        return new(plant, CompileShader(), world);
    }

    private static byte[] CompileShader()
    {
        using Stream source = typeof(GameStarter).Assembly.GetManifestResourceStream("Aurelian.Games.RayQuery.v.ts")!;
        using var reader = new StreamReader(source);
        var module = GpuComputeBinder.Compile(new([new("RayQuery.v.ts", reader.ReadToEnd())]));
        if (!module.Success) throw new InvalidDataException(string.Join("; ", module.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var compiled = VdMirComputeBackend.Compile(module);
        if (compiled.Spirv.Length == 0 || !compiled.SpirvValidated)
            throw new InvalidDataException("Ray query shader compilation failed: " + compiled.DxcOutput + compiled.SpirvValidationOutput);
        return compiled.Spirv;
    }

}
