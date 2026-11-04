using System.Numerics;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Sync;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Models;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

internal sealed unsafe partial class VulkanDynamicDiffuse3D
{
    private readonly AurelianVulkanBuffer[] skyParameters = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count];
    private AurelianVulkanBuffer? environmentRadiance;
    private string? environmentRadianceKey;
    private float[] captureSky = [];

    private void PrepareSky(Graphics3DSettings lighting, DynamicDiffuseLighting3D settings, EnvironmentRadiance3D? source)
    {
        if (environmentRadiance is null || environmentRadianceKey != source?.ContentKey)
        {
            frames.Drain();
            ulong bytes = source is null ? 16 : (ulong)source.Rgba.Length * 4;
            plant.Vk.GetPhysicalDeviceProperties(plant.PhysicalDevice, out PhysicalDeviceProperties properties);
            if (bytes > properties.Limits.MaxStorageBufferRange)
                throw new NotSupportedException("AUR-GI-008: Source environment exceeds the device storage-buffer range.");
            var replacement = Buffer(bytes, VulkanBufferUsage.Storage, VulkanMemoryUsage.CpuToGpu);
            Write(replacement, source?.Rgba.ToArray() ?? [0, 0, 0, 0]);
            if (environmentRadiance is not null)
            {
                buffers.Remove(environmentRadiance);
                environmentRadiance.Dispose();
            }
            environmentRadiance = replacement;
            environmentRadianceKey = source?.ContentKey;
            stationaryFrames = 0;
        }
        // The shared environment sampler uses the existing reference packet layout.
        // Words 0..7 additionally describe the delta-direction sun for this pass.
        float[] words = new float[64 + 2048];
        Vector3 direction = Vector3.Normalize(lighting.SunDirection);
        Vector3 irradiance = lighting.SunColor * lighting.SunIntensity;
        words[0] = direction.X;
        words[1] = direction.Y;
        words[2] = direction.Z;
        words[4] = irradiance.X;
        words[5] = irradiance.Y;
        words[6] = irradiance.Z;
        words[7] = lighting.Shadows ? 1 : 0;
        words[28] = source?.Width ?? 1;
        words[29] = source?.Height ?? 1;
        words[30] = lighting.EnvironmentIntensity;
        words[42] = 64;
        words[43] = source?.ImportanceWidth ?? 1;
        words[44] = source?.ImportanceHeight ?? 1;
        words[45] = source is null ? 0 : 1;
        words[46] = settings.SecondarySkyLighting ? 1 : 0;
        words[47] = settings.EnvironmentImportanceSampling && source?.IsConstant == false ? 1 : 0;
        source?.ImportanceCdf.CopyTo(words, 64);
        if (!words.SequenceEqual(captureSky))
        {
            stationaryFrames = 0;
            captureSky = words;
        }
        Write(skyParameters[frames.Slot], words);
    }
}
